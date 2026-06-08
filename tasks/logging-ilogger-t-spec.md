# Phase 3 — convert non-static logging classes to `ILogger<T>`

Goal: stop threading `ILoggerFactory` into leaf classes that only log for
themselves. Inject `ILogger<T>` in the constructor instead, with a convenience
default so the class is still `new`-able without DI.

Do NOT edit `.csproj`. Build only the project(s) you touch.

## The pattern

For a **non-static** class `Foo` that currently takes `ILoggerFactory? loggerFactory`
and uses it only to create its own `_log` (i.e. it does NOT construct child
objects that need their own typed loggers):

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

private readonly ILogger<Foo> _log;

// was: public Foo(/*…*/ ILoggerFactory? loggerFactory = null)
public Foo(/*…*/ ILogger<Foo>? log = null)
{
    _log = log ?? NullLogger<Foo>.Instance;
}
```

- Drop the `_loggerFactory` field entirely.
- The source-gen `[LoggerMessage]` methods already take an `ILogger` — pass `_log`. No change to the log-method definitions.
- Keep the param **optional defaulting to `NullLogger<Foo>.Instance`** so existing `new Foo(...)` call sites (tests, bench, other libraries) keep compiling. This is the "stay new-able" requirement.
- If the ctor had no other params, `public Foo(ILogger<Foo>? log = null)` is fine.
- Update in-project callers that passed a factory. For cross-project callers (they passed `loggerFactory` / `NullLoggerFactory.Instance`), they'll now pass `ILogger<Foo>` or nothing — list those old→new signatures in your report; the integrator fixes external callers.

## Do NOT convert these (they must stay as-is)

- **Static classes** — `T` can't be a static type, so `ILogger<T>` is impossible.
  They keep `CreateLogger(typeof(X))` / `NullLogger.Instance`. (Don't touch
  `SystemRootCertificates`, the Mac static helpers, `EventDispatcher`,
  `PageSurfaceHost`, the bindings.)
- Classes that construct child objects needing their own typed loggers (the
  composition roots) — leave their `ILoggerFactory` alone; the integrator handles them.
- Anything that no longer references a logger at all (dead plumbing already removed).

## Report

Files changed, every constructor/method signature old→new, and external callers
that need updating.
