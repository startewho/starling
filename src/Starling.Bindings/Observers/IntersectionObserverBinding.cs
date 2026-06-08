using System.Runtime.CompilerServices;
using Starling.Dom;
using Starling.Js.Runtime;

namespace Starling.Bindings.Observers;

/*
 * IntersectionObserver — "run the update intersection observations steps"
 * (IO spec §3.2.2), driven off the host's layout + viewport.
 *
 * The host (interactive shell or headless renderer) calls
 * UpdateForDocument(doc, viewport) after layout and on scroll. For each
 * observed target we read its document-space border box from the ILayoutHost,
 * intersect it with the current viewport rect (its scroll offset + size),
 * compute the intersection ratio, and — when the target crosses the smallest
 * configured threshold (its intersecting/not state flips) — queue an
 * IntersectionObserverEntry and deliver it to the callback via a microtask.
 *
 * observe() also schedules one initial update using the realm's current
 * viewport globals, so a target already in view fires without waiting for the
 * first host-driven update (and a context with no layout host falls back to
 * "treat as on-screen", preserving one-shot renders that gate content on the
 * observer ever firing).
 */

/// <summary>
/// Installs the JS-visible <c>IntersectionObserver</c> constructor and prototype,
/// and drives intersection delivery from host layout via
/// <see cref="UpdateForDocument"/>.
/// </summary>
public static class IntersectionObserverBinding
{
    internal static readonly ConditionalWeakTable<JsObject, IntersectionObserverState> States = new();

    // Per-document registry of live observer states, so the host's
    // UpdateForDocument can iterate every observer rooted in a document without
    // holding the JS wrapper. Mirrors MutationObserverBinding.DocStates.
    internal static readonly ConditionalWeakTable<Document, List<WeakReference<IntersectionObserverState>>> DocStates = new();

    internal static void Register(Document doc, IntersectionObserverState state)
    {
        var list = DocStates.GetValue(doc, static _ => new List<WeakReference<IntersectionObserverState>>());
        foreach (var w in list) if (w.TryGetTarget(out var s) && ReferenceEquals(s, state)) return;
        list.Add(new WeakReference<IntersectionObserverState>(state));
    }

    /// <summary>
    /// Runs the "update intersection observations" step for every observer in
    /// <paramref name="doc"/> against <paramref name="viewport"/> (the visible
    /// region in document CSS px — scroll offset as origin, viewport size as
    /// extent). Targets that crossed their threshold get a record delivered to
    /// the callback via a microtask. Returns true when any record was queued.
    /// </summary>
    public static bool UpdateForDocument(Document doc, LayoutRect viewport)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (!DocStates.TryGetValue(doc, out var list)) return false;
        var any = false;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (!list[i].TryGetTarget(out var state)) { list.RemoveAt(i); continue; }
            any |= state.Update(viewport);
        }
        return any;
    }

    public static void Install(JsRuntime runtime, Document document)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(document);
        var realm = runtime.Realm;
        if (realm.IntersectionObserverConstructor is not null) return;

        var proto = new JsObject(realm.ObjectPrototype);
        realm.IntersectionObserverPrototype = proto;
        realm.IntersectionObserverEntryPrototype = new JsObject(realm.ObjectPrototype);

        EventTargetBinding.DefineAccessor(realm, proto, "root", (thisV, _) =>
        {
            var s = ResolveState(thisV);
            return s?.Root is { } r ? JsValue.Object(DomWrappers.Wrap(realm, r)) : JsValue.Null;
        });
        EventTargetBinding.DefineAccessor(realm, proto, "rootMargin",
            (thisV, _) => JsValue.String(ResolveState(thisV)?.RootMargin ?? "0px 0px 0px 0px"));
        EventTargetBinding.DefineAccessor(realm, proto, "thresholds", (thisV, _) =>
        {
            var s = ResolveState(thisV);
            var items = new List<JsValue>();
            if (s is not null) foreach (var t in s.Thresholds) items.Add(JsValue.Number(t));
            return JsValue.Object(new JsArray(realm, items));
        });

        EventTargetBinding.DefineMethod(realm, proto, "observe", (thisV, args) =>
        {
            var state = ResolveState(thisV)
                ?? throw new JsThrow(realm.NewTypeError("Illegal invocation: observe called on non-IntersectionObserver"));
            if (args.Length == 0 || DomWrappers.UnwrapElement(args[0]) is not { } el)
                throw new JsThrow(realm.NewTypeError("IntersectionObserver.observe: target must be an Element"));
            state.AddTarget(el);
            if (el.OwnerDocument is { } ownerDoc) Register(ownerDoc, state);
            // Schedule one initial "update intersection observations" against the
            // realm's current viewport so a target already in view fires without
            // waiting for the first host-driven update.
            state.QueueInitialUpdate();
            return JsValue.Undefined;
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, proto, "unobserve", (thisV, args) =>
        {
            var state = ResolveState(thisV);
            if (state is null || args.Length == 0) return JsValue.Undefined;
            if (DomWrappers.UnwrapElement(args[0]) is { } el) state.RemoveTarget(el);
            return JsValue.Undefined;
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, proto, "disconnect", (thisV, _) =>
        {
            ResolveState(thisV)?.Disconnect();
            return JsValue.Undefined;
        }, length: 0);

        EventTargetBinding.DefineMethod(realm, proto, "takeRecords", (thisV, _) =>
        {
            var s = ResolveState(thisV);
            return s is null ? JsValue.Object(new JsArray(realm)) : JsValue.Object(s.DrainRecords(realm));
        }, length: 0);

        var ctor = new JsNativeFunction(realm, "IntersectionObserver", 1, (thisV, args) =>
        {
            if (args.Length == 0 || !AbstractOperations.IsCallable(args[0]))
                throw new JsThrow(realm.NewTypeError("IntersectionObserver requires a callback function"));
            var (root, rootMargin, thresholds) = ParseOptions(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var inst = new JsObject(proto);
            States.Add(inst, new IntersectionObserverState(runtime, inst, args[0], root, rootMargin, thresholds));
            return JsValue.Object(inst);
        }, isConstructor: true);

        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));

        realm.IntersectionObserverConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("IntersectionObserver",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    private static IntersectionObserverState? ResolveState(JsValue thisV)
        => thisV.IsObject && States.TryGetValue(thisV.AsObject, out var s) ? s : null;

    private static (Element? root, string rootMargin, IReadOnlyList<double> thresholds) ParseOptions(JsRealm realm, JsValue raw)
    {
        Element? root = null;
        var rootMargin = "0px 0px 0px 0px";
        IReadOnlyList<double> thresholds = new[] { 0.0 };

        if (raw.IsUndefined || raw.IsNull) return (root, rootMargin, thresholds);
        if (!raw.IsObject)
            throw new JsThrow(realm.NewTypeError("IntersectionObserver: options must be an object"));
        var o = raw.AsObject;

        var rootV = o.Get("root");
        if (!rootV.IsUndefined && !rootV.IsNull)
        {
            if (DomWrappers.UnwrapElement(rootV) is { } el) root = el;
            // Document roots are spec-allowed too; we silently ignore non-Element roots for now.
        }

        var rm = o.Get("rootMargin");
        if (!rm.IsUndefined && !rm.IsNull)
            rootMargin = JsValue.ToStringValue(rm);

        var th = o.Get("threshold");
        if (!th.IsUndefined && !th.IsNull)
        {
            if (th.IsObject && th.AsObject is JsArray arr)
            {
                var list = new List<double>(arr.Length);
                for (var i = 0; i < arr.Length; i++)
                {
                    var n = JsValue.ToNumber(arr[i]);
                    if (double.IsNaN(n) || n < 0 || n > 1)
                        throw new JsThrow(realm.NewRangeError("IntersectionObserver: threshold values must be in [0, 1]"));
                    list.Add(n);
                }
                thresholds = list;
            }
            else
            {
                var n = JsValue.ToNumber(th);
                if (double.IsNaN(n) || n < 0 || n > 1)
                    throw new JsThrow(realm.NewRangeError("IntersectionObserver: threshold values must be in [0, 1]"));
                thresholds = new[] { n };
            }
        }

        return (root, rootMargin, thresholds);
    }
}

internal sealed class IntersectionObserverState
{
    private readonly JsRuntime _runtime;
    private readonly JsObject _observerWrapper;
    private readonly JsValue _callback;
    private readonly List<Element> _targets = new();
    private readonly List<JsObject> _pending = new();
    // Last delivered intersecting state per target (absent = never evaluated), so a
    // record is queued only when the target crosses its threshold, not every update.
    private readonly Dictionary<Element, bool> _intersecting = new();
    private bool _deliveryQueued;

    public Element? Root { get; }
    public string RootMargin { get; }
    public IReadOnlyList<double> Thresholds { get; }

    public IntersectionObserverState(JsRuntime runtime, JsObject observerWrapper, JsValue callback,
        Element? root, string rootMargin, IReadOnlyList<double> thresholds)
    {
        _runtime = runtime;
        _observerWrapper = observerWrapper;
        _callback = callback;
        Root = root;
        RootMargin = rootMargin;
        Thresholds = thresholds;
    }

    public void AddTarget(Element target)
    {
        foreach (var t in _targets) if (ReferenceEquals(t, target)) return;
        _targets.Add(target);
    }

    public void RemoveTarget(Element target)
    {
        for (var i = 0; i < _targets.Count; i++)
            if (ReferenceEquals(_targets[i], target))
            {
                _targets.RemoveAt(i);
                _intersecting.Remove(target);
                return;
            }
    }

    public void Disconnect()
    {
        _targets.Clear();
        _pending.Clear();
        _intersecting.Clear();
    }

    /// <summary>Schedules an initial update against the realm's current viewport
    /// globals, so a target already in view fires without waiting for the host.</summary>
    public void QueueInitialUpdate()
        => _runtime.Realm.Microtasks.Enqueue(() => Update(ReadViewport()));

    /// <summary>
    /// Recomputes intersection for every target against <paramref name="viewport"/>
    /// (document CSS px). Queues a record for each target whose intersecting state
    /// flipped across the smallest threshold and schedules one delivery microtask.
    /// Returns true when any record was queued.
    /// </summary>
    public bool Update(LayoutRect viewport)
    {
        if (_targets.Count == 0) return false;
        var host = WindowBinding.LayoutHostForRealm(_runtime.Realm);
        var realm = _runtime.Realm;
        var threshold = Thresholds.Count == 0 ? 0.0 : MinOf(Thresholds);
        var rootRect = ResolveRootRect(host, viewport);
        var queued = false;

        foreach (var target in _targets)
        {
            double ratio;
            bool isIntersecting;
            if (host is null || !host.TryGetBoundingClientRect(target, out var tr))
            {
                // No layout available (e.g. a non-laid-out test realm): treat the
                // target as on-screen so content gated on the observer firing still
                // appears, matching a one-shot "render the whole page" context.
                ratio = 1.0;
                isIntersecting = true;
            }
            else
            {
                ratio = IntersectionRatio(tr, rootRect);
                isIntersecting = threshold <= 0 ? ratio > 0 : ratio >= threshold;
            }

            if (_intersecting.TryGetValue(target, out var last) && last == isIntersecting)
                continue; // no threshold crossing since last update
            _intersecting[target] = isIntersecting;
            _pending.Add(ObserverRecords.BuildIntersectionEntry(realm, target, ratio, isIntersecting));
            queued = true;
        }

        if (queued) ScheduleDelivery();
        return queued;
    }

    private LayoutRect ResolveRootRect(ILayoutHost? host, LayoutRect viewport)
        => Root is { } r && host is not null && host.TryGetBoundingClientRect(r, out var rr)
            ? rr
            : viewport;

    private static double IntersectionRatio(LayoutRect target, LayoutRect root)
    {
        var area = target.Width * target.Height;
        if (area <= 0) return 0;
        var ix = Math.Max(target.Left, root.Left);
        var iy = Math.Max(target.Top, root.Top);
        var ir = Math.Min(target.Right, root.Right);
        var ib = Math.Min(target.Bottom, root.Bottom);
        var w = ir - ix;
        var h = ib - iy;
        return w <= 0 || h <= 0 ? 0 : w * h / area;
    }

    private static double MinOf(IReadOnlyList<double> xs)
    {
        var m = xs[0];
        for (var i = 1; i < xs.Count; i++) if (xs[i] < m) m = xs[i];
        return m;
    }

    private LayoutRect ReadViewport()
    {
        var g = _runtime.Realm.GlobalObject;
        double Num(string n)
        {
            var d = JsValue.ToNumber(g.Get(n));
            return double.IsNaN(d) ? 0 : d;
        }
        return new LayoutRect(Num("scrollX"), Num("scrollY"), Num("innerWidth"), Num("innerHeight"));
    }

    private void ScheduleDelivery()
    {
        if (_deliveryQueued) return;
        _deliveryQueued = true;
        _runtime.Realm.Microtasks.Enqueue(() =>
        {
            _deliveryQueued = false;
            Deliver();
        });
    }

    private void Deliver()
    {
        if (_pending.Count == 0) return;
        var realm = _runtime.Realm;
        var records = DrainRecords(realm);
        _runtime.WithActiveVm(() =>
        {
            try
            {
                AbstractOperations.Call(realm.ActiveVm, _callback,
                    JsValue.Object(_observerWrapper),
                    new[] { JsValue.Object(records), JsValue.Object(_observerWrapper) });
            }
            catch (JsThrow ex)
            {
                realm.ConsoleSink(ConsoleLevel.Error,
                    $"Uncaught (in IntersectionObserver) {JsValue.ToStringValue(ex.Value)}");
            }
            catch (Exception ex)
            {
                realm.ConsoleSink(ConsoleLevel.Error,
                    $"Uncaught (in IntersectionObserver) {ex.Message}");
            }
        });
    }

    public JsArray DrainRecords(JsRealm realm)
    {
        if (_pending.Count == 0) return new JsArray(realm);
        var items = new List<JsValue>(_pending.Count);
        foreach (var r in _pending) items.Add(JsValue.Object(r));
        _pending.Clear();
        return new JsArray(realm, items);
    }
}
