using Jint;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Descriptors;

namespace Starling.Bindings.Jint;

/// <summary>
/// J3d — DOM Standard §4.3 MutationObserver, IntersectionObserver v2,
/// ResizeObserver — JS surface only on the Jint backend.
/// </summary>
/// <remarks>
/// <para>
/// Constructors accept and store a callback; <c>observe()</c>,
/// <c>unobserve()</c>, and <c>disconnect()</c> are wired but no records are
/// ever produced — same v1 behavior as the Starling backend until the layout
/// and mutation pipelines plumb observer notifications through.
/// <c>takeRecords()</c> always returns an empty array.
/// </para>
/// <para>
/// Implemented this way so feature-detection (<c>typeof MutationObserver ===
/// "function"</c>) passes and pages don't TypeError when constructing
/// observers; behavioral wiring is tracked in a future WP.
/// </para>
/// </remarks>
internal static class ObserversBinding
{
    public static void Install(JintBackendContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var engine = ctx.Engine;

        InstallObserver(engine, "MutationObserver", takeRecords: true);
        InstallObserver(engine, "IntersectionObserver", takeRecords: true);
        InstallObserver(engine, "ResizeObserver", takeRecords: false);
    }

    private static void InstallObserver(global::Jint.Engine engine, string name, bool takeRecords)
    {
        var proto = new JsObject(engine);

        JintInterop.DefineMethod(engine, proto, "observe",
            (_, _) => JsValue.Undefined, length: 1);
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
}
