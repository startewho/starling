using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.JsEngineBench;

// Runs the 19 vendored scripts (see Scripts/) through the Starling JS engine in
// global strict mode, ranked on THIS machine. Results live in
// bench/engine-comparison.md.
//
// Two variants mirror the classic cold/warm split:
//   cold      — parse + compile + run every iteration
//   prepared  — reuse the compiled chunk, run on a fresh runtime each time
//
// Several scripts lean on the Dromaeo harness globals (startTest/test/prep/
// endTest/log/assert); we inject stubs for those right after the "use strict"
// directive. A script the engine cannot run is detected once in GlobalSetup and
// skipped — the measured methods stay free of try/catch so timing is never
// distorted. Skipped scripts are logged and surface as near-zero rows;
// bench/engine-comparison.md lists them explicitly.
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ScriptSuiteBench
{
    // Strict mode plus the harness stubs the dromaeo / linq scripts expect.
    // The directive must stay the first statement.
    private const string Prelude =
        "\"use strict\";\n" +
        "var startTest = function () {};\n" +
        "var test = function (name, fn) { fn(); };\n" +
        "var endTest = function () {};\n" +
        "var prep = function (fn) { fn(); };\n" +
        "var log = function () {};\n" +
        "var assert = function () {};\n";

    [ParamsSource(nameof(ScriptFiles))]
    public string FileName = "";

    public static IEnumerable<string> ScriptFiles =>
    [
        "array-stress.js",
        "dromaeo-3d-cube.js",
        "dromaeo-3d-cube-modern.js",
        "dromaeo-core-eval.js",
        "dromaeo-core-eval-modern.js",
        "dromaeo-object-array.js",
        "dromaeo-object-array-modern.js",
        "dromaeo-object-regexp.js",
        "dromaeo-object-regexp-modern.js",
        "dromaeo-object-string.js",
        "dromaeo-object-string-modern.js",
        "dromaeo-string-base64.js",
        "dromaeo-string-base64-modern.js",
        "evaluation.js",
        "evaluation-modern.js",
        "linq-js.js",
        "minimal.js",
        "stopwatch.js",
        "stopwatch-modern.js",
    ];

    private string _src = "";

    // Prepared artifact, built once per script in GlobalSetup.
    private Chunk? _chunk;

    // "This script is unsupported here" flags, set once per script.
    private bool _skipCold;
    private bool _skipPrepared;

    [GlobalSetup]
    public void Setup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Scripts", FileName);
        _src = Prelude + File.ReadAllText(path);

        // Build the reusable artifact. If it fails, the prepared variant is
        // skipped (the cold variant may still run, and vice-versa).
        try { _chunk = JsCompiler.CompileForEval(new JsParser(_src).ParseProgram()); }
        catch (Exception ex) { _chunk = null; Skip("compile", ex); }

        // Validate one run of each variant; mark unsupported ones to skip.
        _skipCold = !Validate("Starling", RunCold);
        _skipPrepared = _chunk is null || !Validate("Starling_Prepared", RunPrepared);
    }

    private bool Validate(string label, Func<JsValue> run)
    {
        try { run(); return true; }
        catch (Exception ex) { Skip(label, ex); return false; }
    }

    private void Skip(string label, Exception ex)
    {
        var msg = ex.Message.ReplaceLineEndings(" ");
        if (msg.Length > 120)
        {
            msg = msg[..120];
        }

        Console.WriteLine($"[skip] {label} / {FileName}: {ex.GetType().Name}: {msg}");
    }

    // ---- measured methods: one skip branch, then the raw call. No try/catch. ----

    [Benchmark(Baseline = true)]
    public JsValue Starling() => _skipCold ? default : RunCold();

    [Benchmark]
    public JsValue Starling_Prepared() => _skipPrepared ? default : RunPrepared();

    // ---- raw runners ----

    private JsValue RunCold() =>
        new JsVm(new JsRuntime()).Run(JsCompiler.CompileForEval(new JsParser(_src).ParseProgram()));

    private JsValue RunPrepared() =>
        new JsVm(new JsRuntime()).Run(_chunk!);
}
