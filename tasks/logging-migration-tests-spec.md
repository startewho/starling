# Test/bench migration addendum — IDiagnostics removal

`IDiagnostics`, `NoopDiagnostics`, `ConsoleDiagnostics`, and `DiagLevel` are
**deleted**. Logging is now `ILogger` (threaded as `ILoggerFactory`); tracing +
metrics are the static `Starling.Common.Diagnostics.StarlingTelemetry`
(`Span`/`Counter`/`Gauge`/`Snapshot`). Get the build green for your assigned test
files. Do NOT edit `.csproj`. Do NOT build the whole solution; you MAY build your
own test project at the end.

## Mechanical swaps

- `NoopDiagnostics.Instance` → `NullLoggerFactory.Instance`
  (add `using Microsoft.Extensions.Logging.Abstractions;`).
- Constructor/factory args that took a diagnostics value:
  - positional `IDiagnostics` arg → pass `NullLoggerFactory.Instance` (or your
    recording factory — see below).
  - named `diagnostics:` / `diag:` → `loggerFactory:`
  - record property `Diag:` → `LoggerFactory:`
- Some signatures CHANGED or DROPPED the param — match the migrated signatures:
  - `new StarlingEngine(loggerFactory: …)` (was `diagnostics:`)
  - `new BrowserSession(loggerFactory: …)`
  - `CssParser.ParseStyleSheet(source, StyleOrigin.Author)` — diagnostics param **dropped**.
  - `StyleEngine(includeUserAgentStyleSheet, loggerFactory)`,
    `LayoutEngine(style, measurer, images, loggerFactory, abort)`,
    `LayoutSession(style, images, loggerFactory)`.
  - `Compositor(backend, tileGrid)`, `TileGrid(maxBytes)`,
    `LayerTreeBuilder(styleOverride, images, isAnimatingLayerRoot, layerIdFor, scrollOffsets)`,
    `CachedPageRenderer(backend, cache)`, `PictureCache()`,
    `GpuSurfacePresenter.CreateFor*(…)` — all **dropped** their diagnostics param.
  - `JintBackendContext(engine, document, baseUrl, http, loggerFactory, loop, layoutHost, fetch)` (was `diag`).
  - `ScriptSessionOptions(Document, BaseUrl, Fetcher, Http, LayoutHost, LoggerFactory)` (was `Diag`).
  - `HtmlParser.Parse(html, loggerFactory, scriptingEnabled)`.
- Remove now-unused `using` lines (IDE0005 is an error here).

## Capture doubles — replace the `: IDiagnostics` test class

### A. Log capture (asserting messages/levels, e.g. `area == "engine.js"`)

Replace the double with a recording `ILoggerProvider` wrapped in a factory you
pass where the logger factory is needed:

```csharp
using Microsoft.Extensions.Logging;

private sealed class RecordingLoggerProvider : ILoggerProvider
{
    public readonly List<(string Category, LogLevel Level, string Message)> Entries = new();
    public ILogger CreateLogger(string categoryName) => new Rec(this, categoryName);
    public void Dispose() { }
    private sealed class Rec(RecordingLoggerProvider o, string cat) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState s) where TState : notnull => null;
        public bool IsEnabled(LogLevel l) => true;
        public void Log<TState>(LogLevel l, EventId id, TState s, Exception? ex,
            Func<TState, Exception?, string> fmt)
        { lock (o.Entries) o.Entries.Add((cat, l, fmt(s, ex))); }
    }
}

// in the test:
var rec = new RecordingLoggerProvider();
using var factory = LoggerFactory.Create(b => { b.SetMinimumLevel(LogLevel.Trace); b.AddProvider(rec); });
// pass `factory` as the loggerFactory argument.
// JS console output lands under category "Starling.engine.js":
var jsLogs = rec.Entries.Where(e => e.Category == "Starling.engine.js").ToList();
```

Old `DiagLevel` → `LogLevel`: Trace→Trace, Debug→Debug, Info→**Information**,
Warn→**Warning**, Error→Error. Old `area` strings map to logger categories:
`"engine.js"` → `"Starling.engine.js"`; other areas → the full type name of the
class that logged (assert on substring or just on level/message if the exact
category isn't important).

### B. Counter capture (asserting `Counter`/`Gauge` totals)

Counters now flow through the static `StarlingTelemetry.Meter`. Listen with a
`MeterListener`. Counters are **process-global** now, so record a baseline before
the action and assert on the **delta** to avoid cross-test bleed:

```csharp
using System.Diagnostics.Metrics;
using Starling.Common.Diagnostics;

private sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _l = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, double> _v = new();
    public MetricRecorder()
    {
        _l.InstrumentPublished = (inst, lst) =>
        { if (inst.Meter.Name == StarlingTelemetry.SourceName) lst.EnableMeasurementEvents(inst); };
        _l.SetMeasurementEventCallback<double>((inst, m, t, s) => Add(inst.Name, m));
        _l.SetMeasurementEventCallback<long>((inst, m, t, s) => Add(inst.Name, m));
        _l.Start();
    }
    private void Add(string n, double m) => _v.AddOrUpdate(n, m, (_, p) => p + m);
    public double CountOf(string name) => _v.TryGetValue(name, out var x) ? x : 0d;
    public void Dispose() => _l.Dispose();
}
// usage: construct it before the action, CountOf(name) after. Prefer "> 0" / delta
// assertions over exact equality where the metric is process-global.
```

### C. Span capture (asserting spans were opened)

Use an `ActivityListener` on `StarlingTelemetry.Source` (Meter→Source, Activity):
```csharp
using System.Diagnostics;
var spans = new List<string>();
using var al = new ActivityListener
{
    ShouldListenTo = src => src.Name == StarlingTelemetry.SourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    ActivityStarted = a => { lock (spans) spans.Add(a.OperationName); },
};
ActivitySource.AddActivityListener(al);
```

## Report back

Files changed, any test you converted from exact-count to delta/“>0” assertions
(and why), and anything you couldn't cleanly migrate.
