using System.Diagnostics;
using Jint.Native;

namespace Starling.Bindings.Jint;

/// <summary>
/// High Resolution Time Level 3 on the Jint backend.
/// Mirrors <c>Starling.Bindings/PerformanceBinding.cs</c>: installs
/// <c>performance.now()</c> (monotonic ms via <see cref="Stopwatch"/>),
/// <c>performance.timeOrigin</c> (UTC ms at install), and a minimal
/// <c>performance.toJSON()</c>.
/// </summary>
/// <remarks>
/// Out of scope (v1): Performance Timeline (mark / measure / PerformanceEntry /
/// PerformanceObserver), Resource Timing, Navigation Timing, Paint Timing.
/// </remarks>
internal static class PerformanceBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var engine = ctx.Engine;

        var origin = (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalMilliseconds;
        var sw = Stopwatch.StartNew();

        var perf = new JsObject(engine);
        JintInterop.DefineAccessor(engine, perf, "timeOrigin",
            (_, _) => JintInterop.Num(origin));
        JintInterop.DefineMethod(engine, perf, "now",
            (_, _) => JintInterop.Num(sw.Elapsed.TotalMilliseconds), length: 0);
        JintInterop.DefineMethod(engine, perf, "toJSON", (_, _) =>
        {
            var json = new JsObject(engine);
            JintInterop.DefineDataProp(json, "timeOrigin", JintInterop.Num(origin));
            return json;
        }, length: 0);

        // Legacy Navigation Timing Level 1 — performance.timing. Analytics
        // scripts (e.g. McMaster's tracker) read domInteractive/domComplete/
        // loadEventEnd to compute page-load metrics. We have no real navigation
        // milestones, so every field reads the install time (origin, integral
        // ms) — non-zero and self-consistent, which is all these scripts need.
        var startMs = (long)origin;
        var timing = new JsObject(engine);
        foreach (var field in new[]
        {
            "navigationStart", "unloadEventStart", "unloadEventEnd", "redirectStart", "redirectEnd",
            "fetchStart", "domainLookupStart", "domainLookupEnd", "connectStart", "connectEnd",
            "secureConnectionStart", "requestStart", "responseStart", "responseEnd",
            "domLoading", "domInteractive", "domContentLoadedEventStart", "domContentLoadedEventEnd",
            "domComplete", "loadEventStart", "loadEventEnd",
        })
        {
            JintInterop.DefineDataProp(timing, field, JintInterop.Num(startMs),
                writable: false, enumerable: true, configurable: true);
        }
        JintInterop.DefineDataProp(perf, "timing", timing, writable: true, enumerable: true, configurable: true);

        // performance.navigation (legacy) — type 0 = navigate, 0 redirects.
        var navigation = new JsObject(engine);
        JintInterop.DefineDataProp(navigation, "type", JintInterop.Num(0), writable: false, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(navigation, "redirectCount", JintInterop.Num(0), writable: false, enumerable: true, configurable: true);
        JintInterop.DefineDataProp(perf, "navigation", navigation, writable: true, enumerable: true, configurable: true);

        // Performance Timeline — no-op stubs so mark/measure-based instrumentation
        // doesn't throw. We keep no entries, so the getEntries* family returns [].
        JintInterop.DefineMethod(engine, perf, "mark", (_, _) => JsValue.Undefined, length: 1);
        JintInterop.DefineMethod(engine, perf, "measure", (_, _) => JsValue.Undefined, length: 1);
        JintInterop.DefineMethod(engine, perf, "clearMarks", (_, _) => JsValue.Undefined, length: 0);
        JintInterop.DefineMethod(engine, perf, "clearMeasures", (_, _) => JsValue.Undefined, length: 0);
        JintInterop.DefineMethod(engine, perf, "clearResourceTimings", (_, _) => JsValue.Undefined, length: 0);
        JintInterop.DefineMethod(engine, perf, "setResourceTimingBufferSize", (_, _) => JsValue.Undefined, length: 1);
        JintInterop.DefineMethod(engine, perf, "getEntries", (_, _) => new global::Jint.Native.JsArray(engine), length: 0);
        JintInterop.DefineMethod(engine, perf, "getEntriesByType", (_, _) => new global::Jint.Native.JsArray(engine), length: 1);
        JintInterop.DefineMethod(engine, perf, "getEntriesByName", (_, _) => new global::Jint.Native.JsArray(engine), length: 1);

        JintInterop.DefineDataProp(engine.Global, "performance", perf,
            writable: true, enumerable: true, configurable: true);
    }
}
