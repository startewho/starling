using System.Diagnostics;
using Jint.Native;

namespace Starling.Bindings.Jint;

/// <summary>
/// J2d — High Resolution Time Level 3 on the Jint backend.
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

        JintInterop.DefineDataProp(engine.Global, "performance", perf,
            writable: true, enumerable: true, configurable: true);
    }
}
