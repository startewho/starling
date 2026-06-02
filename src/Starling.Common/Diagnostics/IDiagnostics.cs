namespace Starling.Common.Diagnostics;

public enum DiagLevel
{
    Trace,
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Single sink for engine logs, traces, counters, and snapshots. Concrete
/// implementations live in their consumers, including console, no-op,
/// OpenTelemetry, and in-memory DevTools sinks. See 01_ARCHITECTURE.md §H.
/// </summary>
public interface IDiagnostics
{
    void Log(DiagLevel level, string area, string message);
    IDisposable Span(string area, string operation);
    void Counter(string name, double value);

    /// <summary>
    /// Records the latest value of a gauge metric — a sampled level that rises
    /// and falls (FPS, queue depth, memory), unlike <see cref="Counter"/> which
    /// only ever accumulates. The OpenTelemetry sink maps this to a synchronous
    /// <c>Gauge</c> instrument so the dashboard graphs the value itself, not a
    /// running sum. Default is a no-op so a sink only implements it if it cares.
    /// </summary>
    void Gauge(string name, double value) { }

    void Snapshot(string label, ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Records an exception as a structured error. The OpenTelemetry sink attaches it to
    /// <c>Activity.Current</c> (so it shows up on the active span in the Aspire
    /// dashboard with full stack trace) and emits an error-level log through
    /// <c>ILogger</c>. Console sinks print stack to stderr. Call this for
    /// exceptions that you catch + handle, before rethrowing or recovering —
    /// otherwise the exception unwinds silently past every span without
    /// telemetry.
    /// </summary>
    /// <param name="area">Same area string used for <c>Log</c>/<c>Span</c>.</param>
    /// <param name="exception">The exception to record.</param>
    /// <param name="message">Optional context message (e.g. "WebGPU init failed"). Defaults to <c>exception.Message</c>.</param>
    void LogException(string area, Exception exception, string? message = null);
}

public sealed class NoopDiagnostics : IDiagnostics
{
    public static readonly NoopDiagnostics Instance = new();

    public void Log(DiagLevel level, string area, string message) { }
    public IDisposable Span(string area, string operation) => NoopSpan.Instance;
    public void Counter(string name, double value) { }
    public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
    public void LogException(string area, Exception exception, string? message = null) { }

    private sealed class NoopSpan : IDisposable
    {
        public static readonly NoopSpan Instance = new();
        public void Dispose() { }
    }
}

public sealed class ConsoleDiagnostics : IDiagnostics
{
    public DiagLevel MinLevel { get; init; } = DiagLevel.Info;

    public void Log(DiagLevel level, string area, string message)
    {
        if (level < MinLevel) return;
        Console.Error.WriteLine($"[{level,-5}] {area}: {message}");
    }

    public IDisposable Span(string area, string operation)
    {
        Log(DiagLevel.Trace, area, $"+ {operation}");
        return new TraceSpan(this, area, operation);
    }

    public void Counter(string name, double value)
        => Log(DiagLevel.Debug, "counter", $"{name}={value}");

    public void Gauge(string name, double value)
        => Log(DiagLevel.Debug, "gauge", $"{name}={value}");

    public void Snapshot(string label, ReadOnlySpan<byte> bytes)
        => Log(DiagLevel.Debug, "snapshot", $"{label} ({bytes.Length} bytes)");

    public void LogException(string area, Exception exception, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var header = message ?? exception.Message;
        // Errors always print regardless of MinLevel — silently swallowing
        // exceptions is the bug we're trying to fix.
        Console.Error.WriteLine($"[Error] {area}: {header}");
        Console.Error.WriteLine(exception);
    }

    private sealed class TraceSpan(ConsoleDiagnostics owner, string area, string operation) : IDisposable
    {
        private readonly long _started = Environment.TickCount64;
        public void Dispose()
        {
            var elapsed = Environment.TickCount64 - _started;
            owner.Log(DiagLevel.Trace, area, $"- {operation} ({elapsed}ms)");
        }
    }
}
