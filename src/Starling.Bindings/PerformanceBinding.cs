using System.Diagnostics;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// B5-5 — High Resolution Time Level 3 (W3C). Installs <c>performance</c> on
/// the window with <c>now()</c>, <c>timeOrigin</c>, no-op Performance Timeline
/// stubs (<c>mark</c>/<c>measure</c>/<c>getEntries*</c>/<c>clear*</c>), and the
/// legacy Navigation Timing Level 1 objects (<c>timing</c>/<c>navigation</c>).
/// </summary>
/// <remarks>
/// <para><b>Monotonicity:</b> <c>now()</c> is backed by
/// <see cref="Stopwatch.GetTimestamp"/>, which is guaranteed monotonic on
/// every supported platform (osx/linux/win). <c>timeOrigin</c> captures the
/// wall-clock time at install (UTC ms since epoch), so
/// <c>timeOrigin + now()</c> approximates wall-clock without ever going
/// backwards across an NTP adjustment.</para>
/// <para><b>Timeline stubs:</b> real sites (e.g. github.com) call
/// <c>performance.mark(...)</c> at module entry. With <c>mark</c> absent the
/// call threw "not a function" and aborted the whole bundle, so the page's
/// JavaScript never ran. We install no-op stubs that keep no entries, so the
/// <c>getEntries*</c> family returns an empty array. This mirrors the Jint
/// backend's <c>PerformanceBinding</c>.</para>
/// <para><b>Out-of-scope (v1):</b> real PerformanceEntry/PerformanceObserver,
/// Resource Timing, and Paint Timing. The stubs above satisfy the
/// overwhelming majority of in-the-wild <c>performance</c> usage (timing
/// shims, RAF deltas, RUM probes that feature-detect or fire-and-forget marks).</para>
/// </remarks>
public static class PerformanceBinding
{
    // Legacy Navigation Timing Level 1 fields (performance.timing). Analytics
    // scripts read domInteractive/domComplete/loadEventEnd to compute page-load
    // metrics. Build-once table — avoids re-allocating per install.
    private static readonly string[] TimingFields =
    [
        "navigationStart", "unloadEventStart", "unloadEventEnd", "redirectStart", "redirectEnd",
        "fetchStart", "domainLookupStart", "domainLookupEnd", "connectStart", "connectEnd",
        "secureConnectionStart", "requestStart", "responseStart", "responseEnd",
        "domLoading", "domInteractive", "domContentLoadedEventStart", "domContentLoadedEventEnd",
        "domComplete", "loadEventStart", "loadEventEnd",
    ];

    public static void Install(JsRuntime runtime) => Install(runtime, () => (DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).TotalMilliseconds);

    /// <summary>Test seam: lets a suite pin <c>timeOrigin</c> deterministically.</summary>
    public static void Install(JsRuntime runtime, Func<double> wallClockMs)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(wallClockMs);

        var realm = runtime.Realm;
        if (realm.GlobalObject.HasOwn("performance")) return;

        var origin = wallClockMs();
        var stopwatch = Stopwatch.StartNew();

        var performance = new JsObject(realm.ObjectPrototype);
        EventTargetBinding.DefineAccessor(realm, performance, "timeOrigin",
            (_, _) => JsValue.Number(origin));
        EventTargetBinding.DefineMethod(realm, performance, "now",
            (_, _) => JsValue.Number(stopwatch.Elapsed.TotalMilliseconds),
            length: 0);
        EventTargetBinding.DefineMethod(realm, performance, "toJSON",
            (_, _) =>
            {
                var json = new JsObject(realm.ObjectPrototype);
                json.DefineOwnProperty("timeOrigin",
                    PropertyDescriptor.Data(JsValue.Number(origin), writable: true, enumerable: true, configurable: true));
                return JsValue.Object(json);
            }, length: 0);

        // Performance Timeline — no-op stubs so mark/measure-based instrumentation
        // doesn't throw. We keep no entries, so the getEntries* family returns [].
        EventTargetBinding.DefineMethod(realm, performance, "mark", static (_, _) => JsValue.Undefined, length: 1);
        EventTargetBinding.DefineMethod(realm, performance, "measure", static (_, _) => JsValue.Undefined, length: 1);
        EventTargetBinding.DefineMethod(realm, performance, "clearMarks", static (_, _) => JsValue.Undefined, length: 0);
        EventTargetBinding.DefineMethod(realm, performance, "clearMeasures", static (_, _) => JsValue.Undefined, length: 0);
        EventTargetBinding.DefineMethod(realm, performance, "clearResourceTimings", static (_, _) => JsValue.Undefined, length: 0);
        EventTargetBinding.DefineMethod(realm, performance, "setResourceTimingBufferSize", static (_, _) => JsValue.Undefined, length: 1);
        EventTargetBinding.DefineMethod(realm, performance, "getEntries", (_, _) => JsValue.Object(new JsArray(realm)), length: 0);
        EventTargetBinding.DefineMethod(realm, performance, "getEntriesByType", (_, _) => JsValue.Object(new JsArray(realm)), length: 1);
        EventTargetBinding.DefineMethod(realm, performance, "getEntriesByName", (_, _) => JsValue.Object(new JsArray(realm)), length: 1);

        // Legacy Navigation Timing Level 1 — performance.timing. We have no real
        // navigation milestones, so every field reads the install time (origin,
        // integral ms): non-zero and self-consistent, which is all these
        // analytics scripts need to avoid dividing by zero.
        var startMs = JsValue.Number((long)origin);
        var timing = new JsObject(realm.ObjectPrototype);
        foreach (var field in TimingFields)
        {
            timing.DefineOwnProperty(field,
                PropertyDescriptor.Data(startMs, writable: false, enumerable: true, configurable: true));
        }
        performance.DefineOwnProperty("timing",
            PropertyDescriptor.Data(JsValue.Object(timing), writable: true, enumerable: true, configurable: true));

        // performance.navigation (legacy) — type 0 = navigate, 0 redirects.
        var navigation = new JsObject(realm.ObjectPrototype);
        navigation.DefineOwnProperty("type",
            PropertyDescriptor.Data(JsValue.Number(0), writable: false, enumerable: true, configurable: true));
        navigation.DefineOwnProperty("redirectCount",
            PropertyDescriptor.Data(JsValue.Number(0), writable: false, enumerable: true, configurable: true));
        performance.DefineOwnProperty("navigation",
            PropertyDescriptor.Data(JsValue.Object(navigation), writable: true, enumerable: true, configurable: true));

        realm.GlobalObject.DefineOwnProperty("performance",
            PropertyDescriptor.Data(JsValue.Object(performance), writable: true, enumerable: true, configurable: true));
    }
}
