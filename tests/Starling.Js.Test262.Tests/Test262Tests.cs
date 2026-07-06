using System.Diagnostics;
using System.Text;

namespace Starling.Js.Test262.Tests;

/// <summary>
/// Runs the tc39/test262 conformance corpus against the Starling JS engine and
/// reports a pass rate per corpus bucket. The corpus is NOT vendored — fetch it
/// first with <c>tools/fetch-test262.sh</c> (pinned SHA → testdata/test262/,
/// gitignored). When the corpus is absent the tests are inconclusive (skipped),
/// so CI without the corpus stays green.
///
/// One test per top-level corpus bucket (language, built-ins, intl402, annexB,
/// staging), each with its own ratchet floor and results files
/// (<c>results/summary-&lt;bucket&gt;.txt</c> / <c>failures-&lt;bucket&gt;.txt</c>).
/// A floor of 0 means report-only — set it to the measured baseline once the
/// bucket has one, then ratchet upward as conformance improves.
///
/// Config via environment variables:
///   STARLING_TEST262_FILTER  case-insensitive path substring filter
///   STARLING_TEST262_MAX     cap on number of files (0 = no cap)
///   STARLING_TEST262_TIMEOUT_MS  per-scenario timeout (default 5000)
///   STARLING_TEST262_FLOOR   floor override, percent (applies to every bucket run)
///   STARLING_TEST262_DIRS    ad-hoc scope for Conformance_custom_scope only
/// </summary>
[TestClass]
public class Test262Tests
{
    public TestContext TestContext { get; set; } = null!;

    // Ratchet floors, percent. Raise deliberately after a measured improvement;
    // never lower. 0 = report-only (bucket not yet baselined).
    private const double LanguageFloor = 98d;   // 98.02% (2026-07-06) — derived-return, paren-pattern, escaped keywords, sloppy-let
    private const double BuiltInsFloor = 79d;  // 79.21% after TypedArray ctor/species + Promise/RegExp generics (2026-07-06)
    private const double Intl402Floor = 69d;   // 69.77% after the NumberFormat option/decimal engine (2026-07-06)
    private const double AnnexBFloor = 85d;   // 85.38% — + catch-pattern bindings forced catch-local in eval/script tops (2026-07-06)
    private const double StagingFloor = 50d;   // baseline 50.63% (2026-07-06)

    [TestMethod]
    public void Conformance_language() => RunBucket("language", LanguageFloor);

    [TestMethod]
    public void Conformance_built_ins() => RunBucket("built-ins", BuiltInsFloor);

    [TestMethod]
    public void Conformance_intl402() => RunBucket("intl402", Intl402Floor);

    [TestMethod]
    public void Conformance_annexB() => RunBucket("annexB", AnnexBFloor);

    [TestMethod]
    public void Conformance_staging() => RunBucket("staging", StagingFloor);

    /// <summary>Ad-hoc slice runner: set STARLING_TEST262_DIRS (comma list of
    /// subdirs under test/, e.g. "language/statements/for-of") to run just that
    /// scope. Inconclusive when the variable is unset, so the per-bucket tests
    /// stay the canonical measurement.</summary>
    [TestMethod]
    public void Conformance_custom_scope()
    {
        var dirsRaw = Environment.GetEnvironmentVariable("STARLING_TEST262_DIRS");
        if (string.IsNullOrWhiteSpace(dirsRaw))
        {
            Assert.Inconclusive("STARLING_TEST262_DIRS not set — the per-bucket tests are the canonical runs.");
            return;
        }

        var dirs = dirsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Run("custom", dirs, floorDefault: 0d);
    }

    private void RunBucket(string bucket, double floorDefault) =>
        Run(bucket, [bucket], floorDefault);

    private void Run(string label, string[] dirs, double floorDefault)
    {
        var root = Test262Corpus.LocateCorpus();
        if (root is null)
        {
            Assert.Inconclusive("test262 corpus not found — run tools/fetch-test262.sh (expects testdata/test262/test).");
            return;
        }

        var filter = Environment.GetEnvironmentVariable("STARLING_TEST262_FILTER");
        var max = int.TryParse(Environment.GetEnvironmentVariable("STARLING_TEST262_MAX"), out var m) ? m : 0;
        var timeout = int.TryParse(Environment.GetEnvironmentVariable("STARLING_TEST262_TIMEOUT_MS"), out var t) ? t : 5_000;
        var floor = double.TryParse(Environment.GetEnvironmentVariable("STARLING_TEST262_FLOOR"), out var f) ? f : floorDefault;

        var files = Test262Corpus.EnumerateTests(Path.Combine(root, "test"), dirs, filter, max);
        var runner = new Test262Runner(root, timeout);

        var resultsDir = Path.Combine(root, "results");
        Directory.CreateDirectory(resultsDir);
        var progressPath = Path.Combine(resultsDir, $"progress-{label}.txt");

        int pass = 0, fail = 0, timeoutN = 0, skip = 0;
        var byCat = new SortedDictionary<string, (int pass, int total)>(StringComparer.Ordinal);
        var failSamples = new List<string>();
        var allFails = new List<string>();
        var sw = Stopwatch.StartNew();
        var fileCount = 0;

        foreach (var file in files)
        {
            fileCount++;
            // Incremental progress so an uncatchable crash (e.g. native stack
            // overflow in the parser) leaves the offending file on disk.
            File.WriteAllText(progressPath, $"{fileCount}: {file}\n");
            foreach (var r in runner.RunFile(file))
            {
                var cat = Test262Corpus.Category(r.File);
                var cur = byCat.TryGetValue(cat, out var c) ? c : (0, 0);
                switch (r.Outcome)
                {
                    case Outcome.Pass: pass++; cur = (cur.Item1 + 1, cur.Item2 + 1); break;
                    case Outcome.Fail: fail++; cur = (cur.Item1, cur.Item2 + 1); RecordSample(failSamples, r); allFails.Add($"[{r.Mode}] {r.File} :: {r.Detail}"); break;
                    case Outcome.Timeout: timeoutN++; cur = (cur.Item1, cur.Item2 + 1); break;
                    case Outcome.Skip: skip++; break; // not counted in denominator
                }
                byCat[cat] = cur;
            }
        }
        sw.Stop();

        var ran = pass + fail + timeoutN;
        var rate = ran == 0 ? 0d : 100d * pass / ran;

        var report = new StringBuilder();
        report.AppendLine($"Test262 conformance [{label}] — {DateTime.UtcNow:u}");
        report.AppendLine($"dirs=[{string.Join(",", dirs)}] filter={filter ?? "(none)"} max={(max == 0 ? "∞" : max.ToString())}");
        report.AppendLine($"files={fileCount} scenarios_run={ran} pass={pass} fail={fail} timeout={timeoutN} skip(module/io)={skip}");
        report.AppendLine($"PASS RATE: {rate:F2}%  ({pass}/{ran})  in {sw.Elapsed.TotalSeconds:F1}s");
        report.AppendLine();
        report.AppendLine("By category (pass/total):");
        foreach (var (cat, c) in byCat)
        {
            report.AppendLine($"  {c.pass,7}/{c.total,-7} {100d * c.pass / Math.Max(1, c.total),6:F1}%  {cat}");
        }

        report.AppendLine();
        report.AppendLine($"Failure samples ({failSamples.Count} shown):");
        foreach (var s in failSamples)
        {
            report.AppendLine("  " + s);
        }

        var reportPath = Path.Combine(resultsDir, $"summary-{label}.txt");
        // Failure details can contain lone surrogates copied out of a test's
        // source text; a strict encoder throws on those. Use a UTF-8 encoder
        // that replaces unencodable chars so the dump never crashes.
        var enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
        File.WriteAllText(reportPath, report.ToString(), enc);
        // Full failure dump (every failing scenario) for offline triage.
        File.WriteAllLines(Path.Combine(resultsDir, $"failures-{label}.txt"), allFails, enc);

        TestContext.WriteLine(report.ToString());
        TestContext.WriteLine("report: " + reportPath);

        if (floor > 0)
        {
            Assert.IsTrue(rate >= floor, $"Test262 [{label}] pass rate {rate:F2}% < floor {floor:F2}% — see {reportPath}");
        }
    }

    private static void RecordSample(List<string> samples, ScenarioResult r)
    {
        if (samples.Count < 60)
        {
            samples.Add($"[{r.Mode}] {r.File} :: {r.Detail}");
        }
    }
}
