using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Starling.Common.Diagnostics;

namespace Starling.Bindings.Jint;

/// <summary>
/// DOM Standard §4.3 MutationObserver, IntersectionObserver v2, and
/// ResizeObserver on the Jint backend.
/// </summary>
/// <remarks>
/// <para>
/// <b>MutationObserver / ResizeObserver</b> are surface-only implementations: constructors
/// accept and store a callback; <c>observe()</c>/<c>unobserve()</c>/
/// <c>disconnect()</c> are wired but produce no records, and
/// <c>takeRecords()</c> returns an empty array. This is enough for
/// feature-detection (<c>typeof MutationObserver === "function"</c>) and keeps
/// pages from TypeError-ing when constructing them.
/// </para>
/// <para>
/// <b>IntersectionObserver</b> is functional in the one direction a one-shot
/// headless render needs: <c>observe(target)</c> asynchronously delivers a
/// single record reporting the target as fully intersecting
/// (<c>isIntersecting:true</c>, <c>intersectionRatio:1</c>). Real pages gate
/// lazy-rendered/lazy-loaded content (e.g. McMaster's React product tiles in
/// <c>#ClientRenderedContentWebPart</c>) on intersection callbacks; a
/// never-firing observer leaves that content permanently empty. Treating every
/// observed element as on-screen matches "render the whole page once" semantics
/// (we have no scroll viewport to occlude anything). The delivery is posted to a
/// later pump turn via <see cref="JintBackendContext.Post"/>, matching the spec's
/// asynchronous notification and giving the framework time to finish setup.
/// </para>
/// </remarks>
internal static class ObserversBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        InstallObserver(ctx, "MutationObserver", takeRecords: true, fireIntersecting: false);
        InstallObserver(ctx, "IntersectionObserver", takeRecords: true, fireIntersecting: true);
        InstallObserver(ctx, "ResizeObserver", takeRecords: false, fireIntersecting: false);
    }

    private static void InstallObserver(JintBackendContext ctx, string name, bool takeRecords, bool fireIntersecting)
    {
        var engine = ctx.Engine;
        var proto = new JsObject(engine);

        JintInterop.DefineMethod(engine, proto, "observe", (thisV, args) =>
        {
            if (fireIntersecting && thisV is ObjectInstance obs && args.Length > 0 && !args[0].IsUndefined())
            {
                var target = args[0];
                var cb = obs.Get("_callback");
                if (cb is global::Jint.Native.Function.Function)
                    ctx.Post(() => DeliverIntersecting(ctx, cb, obs, target));
            }
            return JsValue.Undefined;
        }, length: 1);
        JintInterop.DefineMethod(engine, proto, "unobserve",
            (_, _) => JsValue.Undefined, length: 1);
        JintInterop.DefineMethod(engine, proto, "disconnect",
            (_, _) => JsValue.Undefined, length: 0);
        if (takeRecords)
        {
            JintInterop.DefineMethod(engine, proto, "takeRecords",
                (_, _) => new JsArray(engine, []), length: 0);
        }

        var ctor = new NativeConstructor(engine, name, 1, (args, _) =>
        {
            if (args.Length == 0 || !args[0].IsCallable())
                throw new JavaScriptException(engine.Intrinsics.TypeError,
                    $"{name}: callback is not a function");
            var inst = new JsObject(engine) { Prototype = proto };
            JintInterop.DefineDataProp(inst, "_callback", args[0],
                writable: false, enumerable: false, configurable: false);
            return inst;
        });

        ctor.DefineOwnProperty("prototype",
            new PropertyDescriptor(proto, writable: false, enumerable: false, configurable: false));
        ctor.DefineOwnProperty("length",
            new PropertyDescriptor(JintInterop.Num(1), writable: false, enumerable: false, configurable: true));
        proto.FastSetProperty("constructor",
            new PropertyDescriptor(ctor, writable: true, enumerable: false, configurable: true));

        JintInterop.DefineDataProp(engine.Global, name, ctor,
            writable: true, enumerable: false, configurable: true);
    }

    /// <summary>Invoke an IntersectionObserver callback with one
    /// fully-intersecting record for <paramref name="target"/>.</summary>
    private static void DeliverIntersecting(JintBackendContext ctx, JsValue callback, ObjectInstance observer, JsValue target)
    {
        var engine = ctx.Engine;

        // boundingClientRect / intersectionRect from the layout host when present.
        double x = 0, y = 0, w = 0, h = 0;
        if (ctx.Wrappers.UnwrapElement(target) is { } el && ctx.LayoutHost is { } host &&
            host.TryGetBoundingClientRect(el, out var r))
        {
            x = r.X; y = r.Y; w = r.Width; h = r.Height;
        }
        var rect = Rect(engine, x, y, w, h);

        var entry = new JsObject(engine);
        JintInterop.DefineDataProp(entry, "target", target);
        JintInterop.DefineDataProp(entry, "isIntersecting", JsBoolean.True);
        JintInterop.DefineDataProp(entry, "intersectionRatio", JintInterop.Num(1));
        JintInterop.DefineDataProp(entry, "boundingClientRect", rect);
        JintInterop.DefineDataProp(entry, "intersectionRect", rect);
        JintInterop.DefineDataProp(entry, "rootBounds",
            Rect(engine, 0, 0, ctx.ViewportWidth, ctx.ViewportHeight));
        JintInterop.DefineDataProp(entry, "time", JintInterop.Num(0));

        var entries = new JsArray(engine, new JsValue[] { entry });
        try
        {
            engine.Invoke(callback, observer, new JsValue[] { entries, observer });
            engine.Advanced.ProcessTasks();
        }
        catch (JavaScriptException ex)
        {
            ctx.Diag.Log(DiagLevel.Warn, "engine.js",
                $"Uncaught (in IntersectionObserver) {JintInterop.DescribeError(ex.Error, ex.Message)}");
        }
        catch (Exception ex)
        {
            ctx.Diag.Log(DiagLevel.Warn, "engine.js", $"Uncaught (in IntersectionObserver) {ex.Message}");
        }
    }

    /// <summary>Build a DOMRectReadOnly-shaped object.</summary>
    private static JsObject Rect(global::Jint.Engine engine, double x, double y, double w, double h)
    {
        var o = new JsObject(engine);
        JintInterop.DefineDataProp(o, "x", JintInterop.Num(x));
        JintInterop.DefineDataProp(o, "y", JintInterop.Num(y));
        JintInterop.DefineDataProp(o, "width", JintInterop.Num(w));
        JintInterop.DefineDataProp(o, "height", JintInterop.Num(h));
        JintInterop.DefineDataProp(o, "top", JintInterop.Num(y));
        JintInterop.DefineDataProp(o, "right", JintInterop.Num(x + w));
        JintInterop.DefineDataProp(o, "bottom", JintInterop.Num(y + h));
        JintInterop.DefineDataProp(o, "left", JintInterop.Num(x));
        return o;
    }
}
