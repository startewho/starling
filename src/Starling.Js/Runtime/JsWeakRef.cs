namespace Tessera.Js.Runtime;

/// <summary>
/// §26.1 WeakRef Objects — a weakly-held reference to a single object target.
/// Backed by .NET's <see cref="WeakReference{T}"/>, which gives us
/// GC-correctness for free (the target may be reclaimed when no other strong
/// references exist).
/// </summary>
/// <remarks>
/// The "kept alive" pin required by §26.1.3.2 (every successful
/// <c>deref()</c> must keep the target alive for the rest of the current
/// job) is approximated at the realm level: <see cref="JsRealm.KeptAlive"/>
/// is a strong set that <c>deref</c> adds the target to, and the runtime
/// clears at the bottom of every <see cref="JsRuntime.DrainMicrotasks"/>
/// call. Per-job clearing is a small spec deviation but unobservable in
/// practice — JS can't directly trigger a GC between two <c>deref</c> calls
/// inside the same job.
/// </remarks>
public sealed class JsWeakRef : JsObject
{
    private readonly WeakReference<JsObject> _target;

    public JsWeakRef(JsRealm realm, JsObject target) : base(realm.WeakRefPrototype)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = new WeakReference<JsObject>(target);
    }

    /// <summary>Try to retrieve the target. Returns <c>true</c> + the target
    /// when still alive; <c>false</c> otherwise.</summary>
    public bool TryGetTarget(out JsObject? target) => _target.TryGetTarget(out target!);
}
