using Tessera.Js.Runtime;

namespace Tessera.Js.Intrinsics;

/// <summary>
/// Shared helpers used by every intrinsic install path. The B2-2 follow-up
/// migrated <see cref="JsNativeFunction"/> construction to the realm-aware
/// overload so every built-in method inherits from
/// <c>realm.FunctionPrototype</c> and therefore exposes
/// <c>call</c>/<c>apply</c>/<c>bind</c> out of the box.
/// </summary>
internal static class IntrinsicHelpers
{
    /// <summary>
    /// Install a writable + non-enumerable + configurable method on a
    /// prototype/namespace object (the §17 default attributes for built-in
    /// functions). The function inherits from <c>realm.FunctionPrototype</c>
    /// and has its <c>name</c> + <c>length</c> own properties stamped by the
    /// realm-aware <see cref="JsNativeFunction"/> ctor.
    /// </summary>
    public static JsNativeFunction DefineMethod(
        JsRealm realm,
        JsObject target,
        string name,
        int length,
        Func<JsValue, JsValue[], JsValue> body,
        bool isConstructor = false)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(target);
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor);
        target.DefineOwnProperty(name, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
        return fn;
    }

    /// <summary>Install a constant data property (non-writable, non-enumerable, non-configurable).</summary>
    public static void DefineConstant(JsObject target, string name, JsValue value)
    {
        target.DefineOwnProperty(name,
            PropertyDescriptor.Data(value, writable: false, enumerable: false, configurable: false));
    }
}
