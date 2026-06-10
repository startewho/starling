using Starling.Dom;
using Starling.Dom.Events;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// Turns the scroll store's drained pending-event facts into real DOM
/// <c>scroll</c> events and dispatches them on their targets — the scroll
/// counterpart of <c>AnimationEventDispatcher</c> (browser-plan/scroll-model.md,
/// "Input and events"). The embedder drains the store once per frame, before
/// the <c>requestAnimationFrame</c> callbacks run (the HTML event loop's
/// "run the scroll steps"), and hands the drained facts here — never from the
/// wheel handler and never inside a paint. JS listeners run too: the bindings
/// bridge every JS listener onto the host <see cref="EventTarget"/>, so a
/// host-side dispatch is the same path <c>dispatchEvent</c> takes.
/// </summary>
public static class ScrollEventDispatcher
{
    /// <summary>
    /// Dispatch one coalesced <c>scroll</c> event per drained target.
    /// Per CSSOM View §"Scrolling events": an element scroller fires on that
    /// element and does not bubble; the document scroller fires on the
    /// <see cref="Document"/> instead, and that one bubbles to the window.
    /// None are cancelable — listeners observe a scroll that already happened
    /// (the spec's passive default). Targets re-flagged by a listener writing
    /// an offset during dispatch are picked up by the NEXT frame's drain; this
    /// method never re-reads the store. Returns the number of scroll events
    /// dispatched.
    /// </summary>
    /// <remarks>
    /// The host event path stops at the Document (the window is a separate
    /// host EventTarget outside the node tree), so the document's bubble to
    /// window is modelled the same way <c>WindowBinding.FireDomContentLoaded</c>
    /// models it: a sibling event on the window host target.
    /// </remarks>
    public static int Dispatch(
        JsRealm realm, Document document, IReadOnlyList<Element> scrolledElements, bool documentScrolled)
    {
        ArgumentNullException.ThrowIfNull(realm);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scrolledElements);

        var count = 0;
        for (var i = 0; i < scrolledElements.Count; i++)
        {
            var ev = new Event("scroll", new EventInit(Bubbles: false, Cancelable: false));
            ev.IsTrusted = true; // engine-generated, not script-synthesized
            scrolledElements[i].DispatchEvent(ev);
            count++;
        }

        if (documentScrolled)
        {
            var ev = new Event("scroll", new EventInit(Bubbles: true, Cancelable: false));
            ev.IsTrusted = true;
            document.DispatchEvent(ev);

            var windowTarget = EventTargetBinding.ResolveHost(JsValue.Object(realm.GlobalObject));
            if (windowTarget is not null)
            {
                var wev = new Event("scroll", new EventInit(Bubbles: false, Cancelable: false));
                wev.IsTrusted = true;
                windowTarget.DispatchEvent(wev);
            }
            count++;
        }

        return count;
    }
}
