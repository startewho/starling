using System.Diagnostics;
using System.Collections.Concurrent;
using Starling.Common.Diagnostics;

namespace Starling.Bench;

internal static class GitHubStyleSmoke
{
    public static int Run()
    {
        var bench = new GitHubStyleBench();
        bench.Setup();

        Measure("ParseCss_GitHubHome", bench.ParseCss_GitHubHome);
        Measure("BuildStyleEngine_GitHubHome", bench.BuildStyleEngine_GitHubHome);
        var cascadeDiagnostics = new CounterDiagnostics();
        Measure("PrecomputeTree_GitHubHome", () => bench.PrecomputeTree_GitHubHome(cascadeDiagnostics));
        cascadeDiagnostics.Print("css.");
        var layoutDiagnostics = new TimingDiagnostics();
        Measure("LayoutDocument_GitHubHome", () => bench.LayoutDocument_GitHubHome(layoutDiagnostics));
        layoutDiagnostics.Print();
        Measure("Render_GitHubHome_HtmlToDisplayList", bench.Render_GitHubHome_HtmlToDisplayList);
        return 0;
    }

    private static void Measure<T>(string name, Func<T> run)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        var result = run();
        sw.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Console.WriteLine($"{name}: {sw.ElapsedMilliseconds} ms, {allocated:N0} B, result={result}");
    }

    private sealed class CounterDiagnostics : IDiagnostics
    {
        private readonly ConcurrentDictionary<string, double> _counters = new(StringComparer.Ordinal);

        public void Log(DiagLevel level, string area, string message) { }
        public IDisposable Span(string area, string operation) => Noop.Instance;
        public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        public void LogException(string area, Exception exception, string? message = null) { }

        public void Counter(string name, double value)
            => _counters.AddOrUpdate(name, _ => value, (_, current) => current + value);

        public void Print(string prefix)
        {
            foreach (var pair in _counters
                         .Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                         .OrderBy(pair => pair.Key))
                Console.WriteLine($"  counter {pair.Key}: {pair.Value:N0}");
        }

        private sealed class Noop : IDisposable
        {
            public static readonly Noop Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class TimingDiagnostics : IDiagnostics
    {
        private readonly Dictionary<string, long> _spans = new(StringComparer.Ordinal);

        public void Log(DiagLevel level, string area, string message) { }
        public void Counter(string name, double value) { }
        public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        public void LogException(string area, Exception exception, string? message = null) { }

        public IDisposable Span(string area, string operation)
            => new TimingSpan(this, $"{area}.{operation}");

        public void Print()
        {
            foreach (var pair in _spans.OrderByDescending(pair => pair.Value))
                Console.WriteLine($"  span {pair.Key}: {pair.Value} ms");
        }

        private void Add(string key, long elapsedMs)
        {
            _spans.TryGetValue(key, out var current);
            _spans[key] = current + elapsedMs;
        }

        private sealed class TimingSpan(TimingDiagnostics owner, string key) : IDisposable
        {
            private readonly Stopwatch _sw = Stopwatch.StartNew();

            public void Dispose()
            {
                _sw.Stop();
                owner.Add(key, _sw.ElapsedMilliseconds);
            }
        }
    }
}
