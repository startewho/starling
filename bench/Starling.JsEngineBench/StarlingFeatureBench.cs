using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.JsEngineBench;

/// <summary>
/// Starling-authored microbenchmarks, one per engine optimization, run through
/// the SAME harness as <see cref="ScriptSuiteBench"/> (cold + prepared). Where
/// the vendored dromaeo suite conflates many things, each case here isolates
/// one piece of work so its cost is visible and trackable on its own:
/// <list type="bullet">
/// <item>calls — call-argument and frame-stack pooling.</item>
/// <item>prop-read-mono — monomorphic own-property read inline cache.</item>
/// <item>proto-method — one-hop prototype-chain read IC (method dispatch) + own-read IC.</item>
/// <item>prop-write-add — write IC add-transition + shape sharing (constructor fields).</item>
/// <item>prop-write-existing — existing-slot write inline cache.</item>
/// <item>obj-literal — hidden-class shape sharing across object literals.</item>
/// <item>regex-exec — regex backend (System.Text vs Pike VM, per STARLING_REGEX_ENGINE).</item>
/// <item>regex-split — regex per-match marshaling via the lean @@split path.</item>
/// <item>regex-replace — regex @@replace path (the known remaining regex gap).</item>
/// <item>bootstrap — realm bootstrap (lazy intrinsics + precomputed-shape core install).</item>
/// </list>
/// Each script ends in a numeric checksum so the loop body cannot be elided and
/// the returned value is consumed by BenchmarkDotNet. Run just these with
/// <c>--filter '*StarlingFeatureBench*'</c>; set <c>STARLING_REGEX_ENGINE</c> to
/// compare regex backends.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class StarlingFeatureBench
{
    // Strict-mode parity with ScriptSuiteBench's prelude (no dromaeo stubs
    // needed — these scripts are self-contained).
    private const string Prelude = "\"use strict\";\n";

    private static readonly Dictionary<string, string> Scripts = new(StringComparer.Ordinal)
    {
        // Call overhead: many tiny calls per iteration exercise the pooled
        // argument arrays and the pooled per-frame operand stack.
        ["calls"] =
            "function add(a, b) { return a + b; }\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 300000; i++) { s = add(s, i) + add(i, 1); }\n" +
            "s;",

        // Monomorphic own-property reads on a fixed-shape object — the read IC's
        // core case (reference-equal shape -> direct slot read, no dict lookup).
        ["prop-read-mono"] =
            "function Pt(x, y) { this.x = x; this.y = y; }\n" +
            "var o = new Pt(1, 2);\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 500000; i++) { s += o.x + o.y; }\n" +
            "s;",

        // Inherited method dispatch: v.get resolves one hop up the prototype
        // (proto-read IC), and this.x inside is an own-read IC.
        ["proto-method"] =
            "function V(x) { this.x = x; }\n" +
            "V.prototype.get = function () { return this.x; };\n" +
            "var v = new V(7);\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 400000; i++) { s += v.get(); }\n" +
            "s;",

        // Constructor field initialization: each `new P(...)` adds the same
        // properties in the same order, exercising the add-transition write IC
        // and shared-shape construction.
        ["prop-write-add"] =
            "function P(a, b, c) { this.a = a; this.b = b; this.c = c; }\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 300000; i++) { var p = new P(i, i + 1, i + 2); s += p.a + p.c; }\n" +
            "s;",

        // Reassigning an existing own writable slot — the existing-slot write IC
        // (shape match -> direct slot write).
        ["prop-write-existing"] =
            "var o = { x: 0 };\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 500000; i++) { o.x = i; s += o.x; }\n" +
            "s;",

        // Object-literal construction: many literals of the same shape should
        // share one hidden class (slot array, not a per-object dictionary).
        ["obj-literal"] =
            "var s = 0;\n" +
            "for (var i = 0; i < 300000; i++) { var o = { a: i, b: i + 1, c: i + 2, d: i + 3 }; s += o.a + o.d; }\n" +
            "s;",

        // Regex exec with captures on a translatable pattern: exercises the
        // selected regex backend (System.Text by default, Pike VM under
        // STARLING_REGEX_ENGINE=starling).
        ["regex-exec"] =
            "var re = /(\\w+)@(\\w+)\\.(\\w+)/;\n" +
            "var str = 'user@example.com';\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 100000; i++) { var m = re.exec(str); if (m) s += m[1].length + m[3].length; }\n" +
            "s;",

        // Regex split: the lean @@split per-match marshaling path.
        ["regex-split"] =
            "var str = 'a,b,c,d,e,f,g,h,i,j';\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 100000; i++) { s += str.split(/,/).length; }\n" +
            "s;",

        // Regex global replace: the @@replace path (the known remaining regex
        // gap — tracks the next regex lever).
        ["regex-replace"] =
            "var str = 'the quick brown fox jumps over';\n" +
            "var s = 0;\n" +
            "for (var i = 0; i < 100000; i++) { s += str.replace(/o/g, '0').length; }\n" +
            "s;",

        // Realm bootstrap: a fresh runtime is built per op, so a trivial script
        // measures lazy-intrinsic + precomputed-shape startup cost almost in
        // isolation.
        ["bootstrap"] = "1 + 1;",
    };

    [ParamsSource(nameof(Cases))]
    public string Case = "";

    public static IEnumerable<string> Cases => Scripts.Keys;

    private string _src = "";
    private Chunk? _chunk;

    [GlobalSetup]
    public void Setup()
    {
        _src = Prelude + Scripts[Case];
        _chunk = JsCompiler.CompileForEval(new JsParser(_src).ParseProgram());

        // Fail fast if a case throws, so a broken script never masquerades as a
        // (mis)measured benchmark.
        RunCold();
    }

    [Benchmark(Baseline = true)]
    public JsValue Starling() => RunCold();

    [Benchmark]
    public JsValue Starling_Prepared() =>
        new JsVm(new JsRuntime()).Run(_chunk!);

    private JsValue RunCold() =>
        new JsVm(new JsRuntime()).Run(JsCompiler.CompileForEval(new JsParser(_src).ParseProgram()));
}
