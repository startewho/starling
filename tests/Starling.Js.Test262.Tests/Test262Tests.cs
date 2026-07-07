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
///   STARLING_TEST262_WORKERS parallel worker processes (default: CPU count, max 8; 0 = serial)
///   STARLING_TEST262_ZERO    fail when any scenario fails or times out (1/true)
/// </summary>
[TestClass]
public class Test262Tests
{
    private static readonly string[] DefaultScope = ["language/expressions", "language/statements"];
    private const int DefaultWorkerChunkSize = 40;
    private const int DefaultMaxWorkers = 8;

    public TestContext TestContext { get; set; } = null!;

    // Ratchet floors, percent. Raise deliberately after a measured improvement;
    // never lower. 0 = report-only (bucket not yet baselined).
    private const double LanguageFloor = 98.2d; // 98.33% — param TDZ + private-name identity + namespace TDZ (2026-07-06)
    private const double BuiltInsFloor = 93.5d; // 93.64% — Unicode 17 property-escape tables drove built-ins/RegExp 61.8%->98.8% (2026-07-06)
    private const double Intl402Floor = 69d;   // 69.77% after the NumberFormat option/decimal engine (2026-07-06)
    private const double AnnexBFloor = 85d;   // 85.38% — + catch-pattern bindings forced catch-local in eval/script tops (2026-07-06)
    private const double StagingFloor = 61d;   // 61.51% after the day-2 sweep + agent merges (2026-07-06)

    [TestMethod]
    public void Conformance_default_scope() =>
        Run("default", DefaultScope, floorDefault: 0d, requireZeroFailuresDefault: false);

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
        Run("custom", dirs, floorDefault: 0d, requireZeroFailuresDefault: false);
    }

    private void RunBucket(string bucket, double floorDefault) =>
        Run(bucket, [bucket], floorDefault, requireZeroFailuresDefault: false);

    private void Run(string label, string[] dirs, double floorDefault, bool requireZeroFailuresDefault)
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
        var workers = WorkerCount();
        var requireZeroFailures = ParseBool(Environment.GetEnvironmentVariable("STARLING_TEST262_ZERO")) ?? requireZeroFailuresDefault;

        var files = Test262Corpus.EnumerateTests(Path.Combine(root, "test"), dirs, filter, max).ToArray();

        var resultsDir = Path.Combine(root, "results");
        Directory.CreateDirectory(resultsDir);
        var progressPath = Path.Combine(resultsDir, $"progress-{label}.txt");

        int pass = 0, fail = 0, timeoutN = 0, skip = 0;
        var byCat = new SortedDictionary<string, BucketTally>(StringComparer.Ordinal);
        var failSamples = new List<string>();
        var allFails = new List<string>();
        var sw = Stopwatch.StartNew();
        var results = workers == 0
            ? RunSerial(root, files, timeout, progressPath)
            : RunWorkerProcesses(root, files, timeout, workers, progressPath);

        foreach (var r in results)
        {
            var cat = Test262Corpus.Category(r.File);
            if (!byCat.TryGetValue(cat, out var cur))
            {
                cur = new BucketTally();
                byCat[cat] = cur;
            }

            switch (r.Outcome)
            {
                case Outcome.Pass:
                    pass++;
                    cur.Pass++;
                    break;
                case Outcome.Fail:
                    fail++;
                    cur.Fail++;
                    RecordSample(failSamples, r);
                    allFails.Add($"[{r.Mode}] {r.File} :: {r.Detail}");
                    break;
                case Outcome.Timeout:
                    timeoutN++;
                    cur.Timeout++;
                    RecordSample(failSamples, r);
                    allFails.Add($"[{r.Mode}] {r.File} :: {r.Detail}");
                    break;
                case Outcome.Skip:
                    skip++;
                    cur.Skip++;
                    break; // not counted in denominator
            }
        }
        sw.Stop();

        var ran = pass + fail + timeoutN;
        var rate = ran == 0 ? 0d : 100d * pass / ran;

        var report = new StringBuilder();
        report.AppendLine($"Test262 conformance [{label}] — {DateTime.UtcNow:u}");
        report.AppendLine($"dirs=[{string.Join(",", dirs)}] filter={filter ?? "(none)"} max={(max == 0 ? "∞" : max.ToString())} workers={(workers == 0 ? "serial" : workers.ToString())}");
        report.AppendLine($"files={files.Length} scenarios_run={ran} pass={pass} fail={fail} timeout={timeoutN} skip={skip}");
        report.AppendLine($"PASS RATE: {rate:F2}%  ({pass}/{ran})  in {sw.Elapsed.TotalSeconds:F1}s");
        report.AppendLine($"ZERO FAILURES: {(fail == 0 && timeoutN == 0 ? "yes" : "no")}  fail={fail} timeout={timeoutN}");
        report.AppendLine();
        report.AppendLine("By category:");
        report.AppendLine("      pass    fail timeout    skip     rate  category");
        foreach (var (cat, c) in byCat)
        {
            var total = c.Pass + c.Fail + c.Timeout;
            report.AppendLine($"  {c.Pass,8}{c.Fail,8}{c.Timeout,8}{c.Skip,8}{100d * c.Pass / Math.Max(1, total),8:F1}%  {cat}");
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

        if (requireZeroFailures)
        {
            Assert.AreEqual(0, fail + timeoutN, $"Test262 [{label}] has failures/timeouts — see {reportPath}");
        }
        else if (floor > 0)
        {
            Assert.IsTrue(rate >= floor, $"Test262 [{label}] pass rate {rate:F2}% < floor {floor:F2}% — see {reportPath}");
        }
    }

    private static List<ScenarioResult> RunSerial(string root, string[] files, int timeoutMs, string progressPath)
    {
        var runner = new Test262Runner(root, timeoutMs);
        var results = new List<ScenarioResult>();
        for (var i = 0; i < files.Length; i++)
        {
            File.WriteAllText(progressPath, $"{i + 1}/{files.Length}: {files[i]}\n");
            results.AddRange(runner.RunFile(files[i]));
        }

        return results;
    }

    private static List<ScenarioResult> RunWorkerProcesses(string root, string[] files, int timeoutMs, int workerCount, string progressPath)
    {
        if (files.Length == 0)
        {
            return [];
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "starling-test262-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var pending = new Queue<WorkerJob>();
            var nextJobId = 0;
            foreach (var chunk in files.Chunk(DefaultWorkerChunkSize))
            {
                pending.Enqueue(new WorkerJob(nextJobId++, chunk));
            }

            var running = new List<RunningWorker>();
            var results = new List<ScenarioResult>();
            var completedFiles = 0;

            while (pending.Count > 0 || running.Count > 0)
            {
                while (pending.Count > 0 && running.Count < workerCount)
                {
                    running.Add(StartWorker(root, timeoutMs, tempRoot, pending.Dequeue()));
                }

                for (var i = running.Count - 1; i >= 0; i--)
                {
                    var worker = running[i];
                    if (!worker.Process.HasExited)
                    {
                        continue;
                    }

                    worker.Process.WaitForExit();
                    var workerResults = ReadWorkerResults(worker.Job, worker.OutputPath, root, worker.Process.ExitCode, worker.StandardError.Result);
                    results.AddRange(workerResults);
                    completedFiles += worker.Job.Files.Length;
                    File.WriteAllText(progressPath, $"{Math.Min(completedFiles, files.Length)}/{files.Length}: worker {worker.Job.Id} complete\n");
                    worker.Process.Dispose();
                    running.RemoveAt(i);
                }

                if (running.Count > 0)
                {
                    Thread.Sleep(50);
                }
            }

            return results;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }

    private static RunningWorker StartWorker(string root, int timeoutMs, string tempRoot, WorkerJob job)
    {
        var inputPath = Path.Combine(tempRoot, $"worker-{job.Id}.in");
        var outputPath = Path.Combine(tempRoot, $"worker-{job.Id}.out");
        File.WriteAllLines(inputPath, job.Files);

        var process = new Process
        {
            StartInfo =
            {
                FileName = DotNetHost(),
                UseShellExecute = false,
                RedirectStandardError = true,
            },
        };
        process.StartInfo.ArgumentList.Add(typeof(Test262Tests).Assembly.Location);
        process.StartInfo.ArgumentList.Add("--starling-test262-worker");
        process.StartInfo.ArgumentList.Add(root);
        process.StartInfo.ArgumentList.Add(inputPath);
        process.StartInfo.ArgumentList.Add(outputPath);
        process.StartInfo.ArgumentList.Add(timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Process.Start returned false.");
            }
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException($"Could not start Test262 worker {job.Id}: {ex.Message}", ex);
        }

        return new RunningWorker(job, outputPath, process, process.StandardError.ReadToEndAsync());
    }

    private static List<ScenarioResult> ReadWorkerResults(WorkerJob job, string outputPath, string root, int exitCode, string standardError)
    {
        var results = new List<ScenarioResult>();
        if (File.Exists(outputPath))
        {
            foreach (var line in File.ReadLines(outputPath))
            {
                if (Test262WorkerProtocol.TryDecode(line, out var result))
                {
                    results.Add(result);
                }
            }
        }

        if (exitCode == 0)
        {
            return results;
        }

        var filesWithResults = results.Select(r => r.File).ToHashSet(StringComparer.Ordinal);
        foreach (var file in job.Files)
        {
            var rel = Path.GetRelativePath(root, file);
            if (filesWithResults.Contains(rel))
            {
                continue;
            }

            var detail = "worker exited " + exitCode;
            if (!string.IsNullOrWhiteSpace(standardError))
            {
                detail += ": " + Truncate(standardError.ReplaceLineEndings(" "));
            }

            results.Add(new ScenarioResult(rel, ScenarioMode.NonStrict, Outcome.Fail, detail));
        }

        return results;
    }

    private static int WorkerCount()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("STARLING_TEST262_WORKERS"), out var configured))
        {
            return Math.Clamp(configured, 0, 64);
        }

        return Math.Clamp(Environment.ProcessorCount, 1, DefaultMaxWorkers);
    }

    private static bool? ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value is "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? true
            : value is "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
    }

    private static string DotNetHost() =>
        Environment.ProcessPath is { Length: > 0 } path && Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? path
            : "dotnet";

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120];

    private static void RecordSample(List<string> samples, ScenarioResult r)
    {
        if (samples.Count < 60)
        {
            samples.Add($"[{r.Mode}] {r.File} :: {r.Detail}");
        }
    }

    private sealed class BucketTally
    {
        public int Pass;
        public int Fail;
        public int Timeout;
        public int Skip;
    }

    private sealed record WorkerJob(int Id, string[] Files);

    private sealed record RunningWorker(WorkerJob Job, string OutputPath, Process Process, Task<string> StandardError);
}

internal static class Test262WorkerProgram
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "--starling-test262-worker")
        {
            Console.Error.WriteLine("This assembly is a test project. Use dotnet test, or pass --starling-test262-worker.");
            return 2;
        }

        if (args.Length != 5)
        {
            Console.Error.WriteLine("usage: --starling-test262-worker <root> <input-file-list> <output> <timeout-ms>");
            return 2;
        }

        var root = args[1];
        var input = args[2];
        var output = args[3];
        if (!int.TryParse(args[4], out var timeoutMs))
        {
            Console.Error.WriteLine("invalid timeout-ms");
            return 2;
        }

        var runner = new Test262Runner(root, timeoutMs);
        using var writer = new StreamWriter(output, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false));
        foreach (var file in File.ReadLines(input))
        {
            foreach (var result in runner.RunFile(file))
            {
                writer.WriteLine(Test262WorkerProtocol.Encode(result));
            }

            writer.Flush();
        }

        return 0;
    }
}
