using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Starling.Common.Diagnostics;

namespace Starling.Telemetry;

/// <summary>
/// Bridges <see cref="IDiagnostics"/> calls (Span/Log/Counter/Snapshot) to the
/// OpenTelemetry primitives that <see cref="OtelBootstrap"/> wires up:
/// <see cref="ActivitySource"/>, <see cref="Meter"/>, and
/// <see cref="ILogger"/>. Aspire (or any OTLP collector) sees the result as
/// proper traces, metrics, and structured logs without engine code referencing
/// OpenTelemetry types directly.
/// </summary>
public sealed class OtelDiagnostics : IDiagnostics
{
    /// <summary>Single source name for engine/layout/paint/css spans. The
    /// <see cref="OtelBootstrap"/> tracer registers this via AddSource so the
    /// SDK actually exports them.</summary>
    public const string SourceName = "Starling.Engine";

    internal static readonly ActivitySource Source = new(SourceName);
    internal static readonly Meter Meter = new(SourceName);

    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Counter<double>> _counters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Gauge<double>> _gauges = new(StringComparer.Ordinal);

    public OtelDiagnostics(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
    }

    public IDisposable Span(string area, string operation)
    {
        // Activity is itself IDisposable; when no listener is attached
        // StartActivity returns null so we hand back a no-op disposable to
        // keep the IDiagnostics contract non-null.
        var activity = Source.StartActivity($"{area}.{operation}", ActivityKind.Internal);
        return activity ?? (IDisposable)NoopSpan.Instance;
    }

    public void Log(DiagLevel level, string area, string message)
    {
        var logger = _loggers.GetOrAdd(area, a => _loggerFactory.CreateLogger($"Starling.{a}"));
        logger.Log(MapLevel(level), message);

        var current = Activity.Current;
        if (current is not null)
        {
            // Span events show inline in the Aspire trace timeline; useful
            // for warnings/info that happen mid-phase.
            var tags = new ActivityTagsCollection
            {
                { "level", level.ToString() },
                { "area", area },
            };
            current.AddEvent(new ActivityEvent(message, tags: tags));

            if (level == DiagLevel.Error)
                current.SetStatus(ActivityStatusCode.Error, message);
        }
    }

    public void Counter(string name, double value)
    {
        var counter = _counters.GetOrAdd(name, n => Meter.CreateCounter<double>(n));
        counter.Add(value);
    }

    public void Gauge(string name, double value)
    {
        // Synchronous Gauge (.NET 9+): records the last-set value, so the
        // dashboard plots the level itself (e.g. live FPS) rather than a sum.
        var gauge = _gauges.GetOrAdd(name, n => Meter.CreateGauge<double>(n));
        gauge.Record(value);
    }

    public void Snapshot(string label, ReadOnlySpan<byte> bytes)
    {
        var current = Activity.Current;
        if (current is null) return;
        var tags = new ActivityTagsCollection { { "bytes", bytes.Length } };
        current.AddEvent(new ActivityEvent($"snapshot:{label}", tags: tags));
    }

    public void LogException(string area, Exception exception, string? message = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var header = message ?? exception.Message;

        // ILogger.LogError takes the exception object directly so OTLP/Aspire
        // gets the full structured shape (type, message, stack) rather than a
        // ToString'd string.
        var logger = _loggers.GetOrAdd(area, a => _loggerFactory.CreateLogger($"Starling.{a}"));
        logger.LogError(exception, "{Message}", header);

        // Pin the exception on the active span so Aspire's trace view shows the
        // failure inline. AddException is the OTel-spec'd shape (exception.type,
        // exception.message, exception.stacktrace as event attributes); plain
        // AddEvent loses the structured fields. Setting Status=Error also flips
        // the span's UI badge from green to red.
        var current = Activity.Current;
        if (current is not null)
        {
            var tags = new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.ToString() },
                { "area", area },
            };
            current.AddEvent(new ActivityEvent("exception", tags: tags));
            current.SetStatus(ActivityStatusCode.Error, header);
        }
    }

    private static LogLevel MapLevel(DiagLevel level) => level switch
    {
        DiagLevel.Trace => LogLevel.Trace,
        DiagLevel.Debug => LogLevel.Debug,
        DiagLevel.Info => LogLevel.Information,
        DiagLevel.Warn => LogLevel.Warning,
        DiagLevel.Error => LogLevel.Error,
        _ => LogLevel.Information,
    };

    private sealed class NoopSpan : IDisposable
    {
        public static readonly NoopSpan Instance = new();
        public void Dispose() { }
    }
}
