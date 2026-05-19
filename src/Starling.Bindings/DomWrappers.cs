using System.Runtime.CompilerServices;
using Tessera.Dom;
using Tessera.Dom.Events;
using Tessera.Js.Runtime;

namespace Tessera.Bindings;

/// <summary>
/// B5-1 — Lazy DOM-node ↔ JS-wrapper map. Each host node gets at most one JS
/// wrapper per realm, returned by every <see cref="Wrap"/> call. The wrapper's
/// prototype chain is wired so JS methods on Element/Document/Node/EventTarget
/// resolve via ordinary prototype walk.
/// </summary>
/// <remarks>
/// <para>Wrappers are stored in <see cref="ConditionalWeakTable{TKey, TValue}"/>
/// so they are released when the host node becomes unreachable. The cache is
/// keyed by the host node, not by the wrapper, so a single node observed
/// through two paths still resolves to the same JS object — preserving the
/// invariant <c>document.body === document.body</c>.</para>
/// </remarks>
public static class DomWrappers
{
    // Per-realm wrapper caches. Two realms (rare) get independent caches so a
    // wrapper from realm A isn't reused as the identity in realm B.
    private static readonly ConditionalWeakTable<JsRealm, ConditionalWeakTable<Node, JsObject>> NodeCachesPerRealm = new();

    /// <summary>Return the JS wrapper for <paramref name="target"/> on the
    /// supplied realm. Allocates a new wrapper the first time a node is
    /// observed; returns the cached wrapper on subsequent calls. Non-Node
    /// EventTargets (e.g. Window's <see cref="InMemoryEventTarget"/>) fall back
    /// to a transient plain JS object — currently unused outside synthetic
    /// scenarios.</summary>
    public static JsObject Wrap(JsRealm realm, EventTarget target)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(target);
        if (target is Node node) return WrapNode(realm, node);
        // Fallback: return a fresh wrapper bound to the host. Not cached.
        var proto = realm.EventTargetPrototype ?? realm.ObjectPrototype;
        var fresh = new JsObject(proto);
        EventTargetBinding.BindWrapper(fresh, target);
        return fresh;
    }

    private static JsObject WrapNode(JsRealm realm, Node node)
    {
        var cache = NodeCachesPerRealm.GetValue(realm, _ => new ConditionalWeakTable<Node, JsObject>());
        if (cache.TryGetValue(node, out var existing)) return existing;

        var proto = node switch
        {
            Document => realm.DocumentPrototype ?? realm.ObjectPrototype,
            Element => realm.ElementPrototype ?? realm.ObjectPrototype,
            _ => realm.NodePrototype ?? realm.ObjectPrototype,
        };
        var wrapper = new JsObject(proto);
        EventTargetBinding.BindWrapper(wrapper, node);
        cache.Add(node, wrapper);
        return wrapper;
    }

    /// <summary>Resolve the host <see cref="Node"/> backing a JS wrapper, or
    /// null when the object is not a wrapped DOM node.</summary>
    public static Node? UnwrapNode(JsValue v) =>
        EventTargetBinding.ResolveHost(v) as Node;

    public static Element? UnwrapElement(JsValue v) => UnwrapNode(v) as Element;
    public static Document? UnwrapDocument(JsValue v) => UnwrapNode(v) as Document;

    /// <summary>Convenience for proto methods: returns null when <c>this</c> is
    /// not a wrapped node of the expected type.</summary>
    internal static T? UnwrapAs<T>(JsValue v) where T : class => EventTargetBinding.ResolveHost(v) as T;
}
