using System.Diagnostics;
using Starling.Dom;

namespace Starling.Layout.Scroll;

/// <summary>
/// Read-side snapshot of one scroll container's state. Offsets are CSS px,
/// already clamped to the legal range. The scrollport is the padding-box size
/// from the last layout (the <c>clientWidth</c>/<c>clientHeight</c> source);
/// the overflow size is the scrollable overflow from the last layout (the
/// <c>scrollWidth</c>/<c>scrollHeight</c> source and the clamp bound).
/// </summary>
public readonly record struct ScrollState(
    double OffsetX,
    double OffsetY,
    double ScrollportWidth,
    double ScrollportHeight,
    double OverflowWidth,
    double OverflowHeight,
    bool PendingEvent)
{
    /// <summary>Largest legal <see cref="OffsetX"/> for the recorded geometry.</summary>
    public double MaxOffsetX => Math.Max(0, OverflowWidth - ScrollportWidth);

    /// <summary>Largest legal <see cref="OffsetY"/> for the recorded geometry.</summary>
    public double MaxOffsetY => Math.Max(0, OverflowHeight - ScrollportHeight);
}

/// <summary>
/// The one shared scroll store for a document (see
/// browser-plan/scroll-model.md). Keyed by <see cref="Element"/> because boxes
/// are rebuilt on every relayout while elements are stable — the same pattern
/// the script-animation store uses. Holds, per scroll container: the clamped
/// offset, the scrollport (padding box) and scrollable overflow from the last
/// layout, and a pending-event flag set on any offset change (drained once per
/// frame by the frame pump, WP4). One extra root entry models the document
/// scroller.
/// </summary>
/// <remarks>
/// <para><b>Scrolling never dirties layout.</b> The offset is paint-time and
/// hit-test-time state only. <see cref="Write"/> clamps, stores, and flags —
/// it flushes nothing. Geometry flows the other way: the layout engine
/// measures scrollport + overflow into the store after each pass, then
/// <see cref="ReconcileAfterLayout"/> re-clamps every entry against the fresh
/// geometry (a clamp that moves an offset flags the event like any other
/// scroll).</para>
/// <para><b>Layout gate.</b> The layout engine brackets each pass with
/// <see cref="BeginLayoutPass"/>/<see cref="EndLayoutPass"/>; offset writes
/// during a pass throw in DEBUG builds. The offset must never feed a layout
/// input (e.g. <c>ConstraintSpace</c>) — a write mid-pass is the loudest
/// symptom of that bug.</para>
/// <para>Not thread-safe; same single-threaded use as the layout tree it
/// mirrors.</para>
/// </remarks>
public sealed class ScrollStateStore
{
    private struct Entry
    {
        public double X, Y;             // clamped offset
        public double PortW, PortH;     // scrollport = padding box
        public double OverW, OverH;     // scrollable overflow
        public bool Pending;            // scroll event owed to this target
        public int Generation;          // last layout pass that measured this entry
    }

    private readonly Dictionary<Element, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<Element> _scratch = [];
    private Entry _root;
    private int _generation;
    private int _layoutPassDepth;
    private long _geometryRecords;

    // ---- Reads (bindings, paint, hit testing) -------------------------------

    /// <summary>Snapshot for <paramref name="element"/>'s scroll container, or
    /// false when the element is not a scroll container in the current layout
    /// (and holds no offset written since).</summary>
    public bool TryGet(Element element, out ScrollState state)
    {
        if (_entries.TryGetValue(element, out var e))
        {
            state = Snapshot(in e);
            return true;
        }
        state = default;
        return false;
    }

    /// <summary>The document scroller's entry. Scrollport = viewport,
    /// overflow = page extent, offset = root scroll position (synced from the
    /// shell's root scroller in v1).</summary>
    public ScrollState Root => Snapshot(in _root);

    /// <summary>Offset lookup shaped for the paint/hit-test scroll-offset
    /// funcs (<c>DisplayListBuilder.Build</c> et al.). (0,0) for non-scrollers.</summary>
    public (double X, double Y) GetOffset(Element element)
        => _entries.TryGetValue(element, out var e) ? (e.X, e.Y) : (0, 0);

    /// <summary>True when any entry (or the root) owes a scroll event.</summary>
    public bool HasPendingEvents
    {
        get
        {
            if (_root.Pending) return true;
            foreach (var kv in _entries)
                if (kv.Value.Pending) return true;
            return false;
        }
    }

    /// <summary>
    /// Collect every element owing a scroll event into
    /// <paramref name="targets"/> (cleared first) and clear the flags;
    /// <paramref name="rootPending"/> reports (and clears) the document
    /// scroller's flag. The frame pump (WP4) drains this once per frame,
    /// before <c>requestAnimationFrame</c> callbacks.
    /// </summary>
    public void DrainPendingEventTargets(List<Element> targets, out bool rootPending)
    {
        ArgumentNullException.ThrowIfNull(targets);
        targets.Clear();
        rootPending = _root.Pending;
        _root.Pending = false;
        foreach (var kv in _entries)
            if (kv.Value.Pending) targets.Add(kv.Key);
        foreach (var el in targets)
        {
            var e = _entries[el];
            e.Pending = false;
            _entries[el] = e;
        }
    }

    // ---- Writes (input routing, bindings) -----------------------------------

    /// <summary>
    /// Set <paramref name="element"/>'s scroll offset. Clamps to the legal
    /// range for the last-measured geometry, stores, and sets the
    /// pending-event flag when the offset actually moved. Flushes nothing —
    /// callers that need fresh geometry first (the <c>scrollTop</c> setter)
    /// flush layout before writing. Writes to a box that is not a scroll
    /// container clamp to (0,0), matching CSSOM's no-op semantics.
    /// </summary>
    public void Write(Element element, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(element);
        AssertNotInLayoutPass();
        var existed = _entries.TryGetValue(element, out var e); // default entry: zero geometry
        // A box with no measured geometry clamps to (0,0); skip materializing
        // an all-zero entry for it (reconcile would drop it anyway).
        if (WriteEntry(ref e, x, y) || existed)
            _entries[element] = e;
    }

    /// <summary>Root-scroller variant of <see cref="Write"/>. The shells sync
    /// their root scroll position through here in v1.</summary>
    public void WriteRoot(double x, double y)
    {
        AssertNotInLayoutPass();
        WriteEntry(ref _root, x, y);
    }

    // ---- Layout-side surface (internal: the layout engine only) -------------

    /// <summary>Marks a layout pass in progress: external offset writes are
    /// illegal until the matching <see cref="EndLayoutPass"/> (DEBUG throw).
    /// Also opens a new measurement generation so <see cref="ReconcileAfterLayout"/>
    /// can drop entries the new layout no longer measures. The gate is a
    /// counter, so a layout pass nested inside another (e.g. a flush forced
    /// mid-pass) cannot reopen the outer pass's gate early.</summary>
    internal void BeginLayoutPass()
    {
        _layoutPassDepth++;
        _generation++;
    }

    /// <summary>Closes one nesting level of the layout gate; offset writes are
    /// legal again when the outermost pass ends. Always runs (finally) so an
    /// aborted layout cannot wedge the gate shut; an unbalanced call is
    /// clamped rather than letting the gate go negative.</summary>
    internal void EndLayoutPass()
    {
        if (_layoutPassDepth > 0) _layoutPassDepth--;
    }

    /// <summary>Record a scroll container's fresh geometry. Offsets and the
    /// pending flag are preserved; clamping waits for
    /// <see cref="ReconcileAfterLayout"/> so one pass clamps everything once.</summary>
    internal void RecordGeometry(Element element, double scrollportWidth, double scrollportHeight, double overflowWidth, double overflowHeight)
    {
        _geometryRecords++;
        _entries.TryGetValue(element, out var e);
        e.PortW = scrollportWidth;
        e.PortH = scrollportHeight;
        e.OverW = overflowWidth;
        e.OverH = overflowHeight;
        e.Generation = _generation;
        _entries[element] = e;
    }

    /// <summary>Record the document scroller's geometry (scrollport = viewport,
    /// overflow = page extent).</summary>
    internal void RecordRootGeometry(double scrollportWidth, double scrollportHeight, double overflowWidth, double overflowHeight)
    {
        _root.PortW = scrollportWidth;
        _root.PortH = scrollportHeight;
        _root.OverW = overflowWidth;
        _root.OverH = overflowHeight;
        _root.Generation = _generation;
    }

    /// <summary>
    /// Post-layout reconcile: re-clamp every entry against the geometry the
    /// pass just measured. When content shrank or a scrollport grew, the
    /// stored offset can exceed the new range — the clamp pulls it in and
    /// sets the pending-event flag like any other scroll. Entries the pass
    /// did not measure (the element stopped being a scroll container, or left
    /// the tree) are dropped; their next appearance starts at offset (0,0),
    /// matching engine behavior for display:none round-trips.
    /// </summary>
    internal void ReconcileAfterLayout()
    {
        ClampEntry(ref _root);

        if (_entries.Count == 0) return;
        _scratch.Clear();
        foreach (var kv in _entries)
            _scratch.Add(kv.Key);
        foreach (var el in _scratch)
        {
            var e = _entries[el];
            if (e.Generation != _generation)
            {
                _entries.Remove(el);
                continue;
            }
            if (ClampEntry(ref e))
                _entries[el] = e;
        }
        _scratch.Clear();
    }

    /// <summary>
    /// Scoped-pass reconcile (incremental relayout): re-clamp every entry and
    /// the root against their current geometry, with no generation-based
    /// dropping — a scoped pass deliberately re-measures only relaid scroll
    /// containers, so an unmeasured entry is fresh, not stale. The layout
    /// session drops dead entries itself (element detached or no longer a
    /// scroll container) before calling this.
    /// </summary>
    internal void ClampAllEntries()
    {
        ClampEntry(ref _root);

        if (_entries.Count == 0) return;
        _scratch.Clear();
        foreach (var kv in _entries)
            _scratch.Add(kv.Key);
        foreach (var el in _scratch)
        {
            var e = _entries[el];
            if (ClampEntry(ref e))
                _entries[el] = e;
        }
        _scratch.Clear();
    }

    /// <summary>Copy every entry's element into <paramref name="into"/>
    /// (cleared first) so the layout session can audit entries against the
    /// live box tree without iterating the dictionary it mutates.</summary>
    internal void CollectEntryElements(List<Element> into)
    {
        into.Clear();
        foreach (var kv in _entries)
            into.Add(kv.Key);
    }

    /// <summary>Drop one entry — the scoped-pass equivalent of the
    /// generation-mismatch drop in <see cref="ReconcileAfterLayout"/>.</summary>
    internal void RemoveEntry(Element element) => _entries.Remove(element);

    /// <summary>Total <see cref="RecordGeometry"/> calls over this store's
    /// lifetime — the observable that pins re-measurement scoping in tests
    /// (an untouched scroll container's entry is never re-recorded by an
    /// incremental relayout of a sibling).</summary>
    internal long GeometryRecords => _geometryRecords;

    // ---- Internals -----------------------------------------------------------

    private static ScrollState Snapshot(ref readonly Entry e)
        => new(e.X, e.Y, e.PortW, e.PortH, e.OverW, e.OverH, e.Pending);

    /// <summary>Clamp-and-store an offset write into <paramref name="e"/>.
    /// Returns true when the offset moved (flag set).</summary>
    private static bool WriteEntry(ref Entry e, double x, double y)
    {
        if (double.IsNaN(x)) x = 0;
        if (double.IsNaN(y)) y = 0;
        var nx = Math.Clamp(x, 0, Math.Max(0, e.OverW - e.PortW));
        var ny = Math.Clamp(y, 0, Math.Max(0, e.OverH - e.PortH));
        if (nx == e.X && ny == e.Y) return false;
        e.X = nx;
        e.Y = ny;
        e.Pending = true;
        return true;
    }

    /// <summary>Re-clamp <paramref name="e"/>'s offset against its own
    /// geometry. Returns true when the offset moved (flag set).</summary>
    private static bool ClampEntry(ref Entry e)
    {
        var nx = Math.Clamp(e.X, 0, Math.Max(0, e.OverW - e.PortW));
        var ny = Math.Clamp(e.Y, 0, Math.Max(0, e.OverH - e.PortH));
        if (nx == e.X && ny == e.Y) return false;
        e.X = nx;
        e.Y = ny;
        e.Pending = true;
        return true;
    }

    /// <summary>The scroll-model invariant, loud: nothing may write a scroll
    /// offset while layout is running — the offset is not a layout input, and
    /// a write mid-pass means some layout code is reading it back in (see the
    /// Risks section of browser-plan/scroll-model.md). Compiled out of release
    /// builds.</summary>
    [Conditional("DEBUG")]
    private void AssertNotInLayoutPass()
    {
        if (_layoutPassDepth > 0)
            throw new InvalidOperationException(
                "ScrollStateStore offset write during a layout pass. Scroll offsets are paint/hit-test state; layout must never read or write them (browser-plan/scroll-model.md).");
    }
}
