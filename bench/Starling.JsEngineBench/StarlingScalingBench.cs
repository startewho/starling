using BenchmarkDotNet.Attributes;
using Jint;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.JsEngineBench;

/// <summary>
/// Scaling sweep: ONE representative workload run at an increasing iteration
/// count <c>N</c>, so the engines' <b>fixed</b> cost (parse + compile + realm
/// bootstrap) and <b>marginal</b> per-iteration dispatch cost separate out as
/// N grows. Where <see cref="StarlingFeatureBench"/> pins each feature at a
/// single large N, this one answers "one-off vs medium vs lots-and-lots".
///
/// <para>Read the table down an engine's column as N increases:</para>
/// <list type="bullet">
/// <item><b>N = 1</b> — dominated by FIXED cost (one-off latency). A bytecode VM
///   pays a compile step a tree-walker skips, so Jint is expected to win here.</item>
/// <item><b>N large</b> — dominated by MARGINAL cost (steady-state throughput).
///   This is where bytecode + inline caches are expected to pull ahead — IF
///   per-instruction dispatch is cheaper than re-walking the AST. A crossover
///   exists only if Starling's marginal cost is below Jint's.</item>
/// </list>
///
/// <para>Decompose per engine from the curve:
///   fixed ≈ time at N=1;
///   marginal ≈ (t(N_hi) − t(N_lo)) / (N_hi − N_lo).</para>
///
/// <para><b>Cold vs Warm</b> isolates the compile/parse cost itself: the
/// Cold−Warm gap (largest at small N) is the parse+compile a re-run amortizes
/// away. The amortization point is the run count K where K·(warm marginal)
/// outweighs that one-time gap.</para>
///
/// The body is a monomorphic property + prototype-method + call workload — the
/// shape of code a bytecode VM with inline caches is supposed to be strongest
/// on, and (unlike the regex cases) no shared System.Text call dilutes the
/// engines' own dispatch. Both Warm and Cold build a fresh engine/realm per op,
/// so realm bootstrap stays part of the fixed cost (intentional — it is part of
/// one-off latency). Uses a short job (4 methods × 4 N); run with
/// <c>--filter '*StarlingScalingBench*'</c>.
/// </summary>
[ShortRunJob]
[MemoryDiagnoser]
public class StarlingScalingBench
{
    private const string Prelude = "\"use strict\";\n";

    // Only the loop bound varies with N; everything else is per-execution fixed
    // work (class definition + two allocations). The loop body exercises four
    // monomorphic own-property reads, a one-hop prototype-method dispatch, a
    // call, and arithmetic.
    private static string Build(int n) =>
        Prelude +
        "function Pt(x, y) { this.x = x; this.y = y; }\n" +
        "Pt.prototype.add = function (o) { return this.x + this.y + o.x + o.y; };\n" +
        "var a = new Pt(1, 2), b = new Pt(3, 4), s = 0;\n" +
        "for (var i = 0; i < " + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; i++) { s += a.add(b); }\n" +
        "s;";

    [Params(1, 100, 10_000, 1_000_000)]
    public int N;

    private string _src = "";
    private Chunk? _chunk;
    private Prepared<Acornima.Ast.Script> _jintPrepared;

    [GlobalSetup]
    public void Setup()
    {
        _src = Build(N);
        _chunk = JsCompiler.CompileForEval(new JsParser(_src).ParseProgram());
        _jintPrepared = Engine.PrepareScript(_src, strict: true);

        // Fail fast if a script throws on either engine.
        Jint_Warm();
        Starling_Warm();
    }

    // Baseline: warm Jint (pre-parsed AST), the natural steady-state reference.
    [Benchmark(Baseline = true)]
    public Engine Jint_Warm() => new Engine(o => o.Strict = true).Execute(_jintPrepared);

    [Benchmark]
    public Engine Jint_Cold() => new Engine(o => o.Strict = true).Execute(_src);

    [Benchmark]
    public JsValue Starling_Warm() => new JsVm(new JsRuntime()).Run(_chunk!);

    [Benchmark]
    public JsValue Starling_Cold() =>
        new JsVm(new JsRuntime()).Run(JsCompiler.CompileForEval(new JsParser(_src).ParseProgram()));
}
