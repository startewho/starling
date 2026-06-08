# IDiagnostics → ILogger migration spec (shared contract)

We are removing the home-rolled `IDiagnostics` sink entirely. Logging moves to
`ILogger` with **source-generated `[LoggerMessage]`** methods. Tracing/metrics
move to the new static `StarlingTelemetry` facade. Every agent MUST follow these
rules exactly so the parallel edits line up across project boundaries.

Foundation is already in place (do NOT re-add it):
- `Microsoft.Extensions.Logging.Abstractions` is referenced by every project
  (root `Directory.Build.props`). You get `ILogger`, `ILoggerFactory`,
  `NullLoggerFactory`, `LogLevel`, and the `[LoggerMessage]` generator for free.
- `Starling.Common.Diagnostics.StarlingTelemetry` (static) exposes
  `Span(area, op) : IDisposable`, `Counter(name, value)`, `Gauge(name, value)`,
  `Snapshot(label, bytes)`, and `RecordException(area, ex, msg?)`.

## Hard rules

1. **Do NOT edit any `.csproj` file.** Package refs are handled centrally.
2. **Do NOT build the whole solution** — the tree is mid-refactor and other
   projects won't compile yet. Just make your files internally correct.
3. Touch only the files assigned to you.

## Transform A — the threaded sink becomes `ILoggerFactory`

Every place that threaded `IDiagnostics` now threads `ILoggerFactory` (the 1:1
replacement type — keep it uniform so cross-project call sites match).

Fields:
```csharp
// before
private readonly IDiagnostics _diag;
// after
private readonly ILoggerFactory _loggerFactory;
private readonly ILogger _log;
```

Constructor params + body:
```csharp
// before
public Foo(/*...*/ IDiagnostics? diagnostics = null)
{
    _diag = diagnostics ?? NoopDiagnostics.Instance;
}
// after
public Foo(/*...*/ ILoggerFactory? loggerFactory = null)
{
    _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    _log = _loggerFactory.CreateLogger<Foo>();
}
```
- Non-optional `IDiagnostics diagnostics` → non-optional `ILoggerFactory loggerFactory`.
- When the class constructs child objects that previously got `_diag`, pass
  `_loggerFactory` to them instead.
- If a class only ever LOGS (never builds children), you may store just `_log`
  and skip the `_loggerFactory` field — but keep the **constructor param typed
  `ILoggerFactory`** so callers don't diverge.

Static/helper method params:
```csharp
// before
static SheetIndex BuildSheetIndex(StyleSheet sheet, IDiagnostics diag, ...)
// after
static SheetIndex BuildSheetIndex(StyleSheet sheet, ILoggerFactory loggerFactory, ...)
//   create a local logger inside: var log = loggerFactory.CreateLogger(typeof(StyleEngine));
```

`NoopDiagnostics.Instance` → `NullLoggerFactory.Instance` everywhere.

## Transform B — logging calls become source-gen `[LoggerMessage]`

Per file, add ONE top-level (file-scoped namespace) partial log class. It does
NOT require the consuming type to be `partial`:

```csharp
internal static partial class <TypeName>Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "dropping rule with invalid selector: {Reason}")]
    public static partial void InvalidSelector(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "subframe fetch failed: {Url}")]
    public static partial void SubframeFetchFailed(ILogger logger, Exception ex, string url);
}
```

Call-site mapping:
| old | new |
|---|---|
| `x.Log(DiagLevel.Trace, area, msg)` | `<Type>Log.Name(_log, ...)` with `Level = LogLevel.Trace` |
| `x.Log(DiagLevel.Debug, …)` | `Level = LogLevel.Debug` |
| `x.Log(DiagLevel.Info, …)` | `Level = LogLevel.Information` |
| `x.Log(DiagLevel.Warn, …)` | `Level = LogLevel.Warning` |
| `x.Log(DiagLevel.Error, …)` | `Level = LogLevel.Error` |
| `x.LogException(area, ex, msg)` | `Level = LogLevel.Error`, method takes `Exception ex` first ILogger-arg slot |

- Convert string interpolation in the old message into **named template
  placeholders** + typed params (e.g. `{Url}`, `{Reason}`, `{Count}`). Never
  pass a pre-interpolated string as a single `{Message}` unless the value is
  genuinely opaque.
- The old `area` string is dropped from the message text — the logger category
  (the type) now encodes location. **EXCEPTION:** see engine.js below.
- Pick a short PascalCase method name describing the event.

## Transform C — tracing/metrics become `StarlingTelemetry`

| old | new |
|---|---|
| `using (x.Span(area, op)) { … }` | `using (StarlingTelemetry.Span(area, op)) { … }` |
| `x.Counter(name, value)` | `StarlingTelemetry.Counter(name, value)` |
| `x.Gauge(name, value)` | `StarlingTelemetry.Gauge(name, value)` |
| `x.Snapshot(label, bytes)` | `StarlingTelemetry.Snapshot(label, bytes)` |

`StarlingTelemetry` is in `Starling.Common.Diagnostics`, so keep that `using`.
If after migration a class no longer uses any `ILoggerFactory`/`ILogger` member
(it only did Span/Counter/Gauge), you can DROP the `ILoggerFactory` constructor
param entirely and just call the static `StarlingTelemetry`. Use judgement; when
in doubt keep the param.

## Special case — JS console output keeps category `Starling.engine.js`

Any call `x.Log(level, "engine.js", msg)` routes page `console.*` output. The
in-memory DevTools sink and the MCP query tool filter on the exact category
`Starling.engine.js`. Preserve it: hold a dedicated logger
```csharp
private readonly ILogger _jsConsoleLog = loggerFactory.CreateLogger("Starling.engine.js");
```
and route those source-gen calls through `_jsConsoleLog` (the `[LoggerMessage]`
method still takes an `ILogger logger` param — pass `_jsConsoleLog`).

## Transform D — fix empty catch blocks (slopwatch SW003) in your files

For every empty/comment-only `catch` in your assigned files, log the exception
via a source-gen method instead of swallowing:
- Genuinely unimportant/expected (cleanup, best-effort, optional) → `LogLevel.Debug`
  or `LogLevel.Trace`.
- Otherwise → `LogLevel.Warning`/`Error`.
- Keep the existing explanatory comment.
- If the class has no logger and threading one in is disproportionate for a tiny
  static leaf, you MAY add an `ILoggerFactory` param (consistent with Transform A)
  — prefer threading over leaving it empty.

## Usings

Add as needed:
```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions; // for NullLoggerFactory
```
Keep `using Starling.Common.Diagnostics;` if you reference `StarlingTelemetry`.
Remove it only if nothing from that namespace is used anymore (IDE0005 is an
error in this repo, so don't leave unused usings).

## Report back

When done, return: the files you changed, every public/internal **constructor or
method signature** whose parameter changed from `IDiagnostics` to
`ILoggerFactory` (callers in other projects need these), and anything you
couldn't cleanly migrate.
