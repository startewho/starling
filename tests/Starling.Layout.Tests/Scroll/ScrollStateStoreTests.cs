using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Html;
using Starling.Layout.Incremental;
using Starling.Layout.Scroll;
using Starling.Layout.Text;

namespace Starling.Layout.Tests.Scroll;

/// <summary>
/// WP1 of browser-plan/scroll-model.md: the per-document
/// <see cref="ScrollStateStore"/>, scrollable-overflow measurement during
/// layout, and the post-layout clamp.
/// </summary>
[TestClass]
public sealed class ScrollStateStoreTests
{
    private static readonly Size Viewport = new(800, 600);

    /// <summary>Lay <paramref name="html"/> out with <paramref name="store"/>
    /// attached, the way the engine session will run it.</summary>
    private static Document Layout(string html, ScrollStateStore store, Size? viewport = null)
    {
        var doc = HtmlParser.Parse(html);
        Relayout(doc, store, viewport);
        return doc;
    }

    private static void Relayout(Document doc, ScrollStateStore store, Size? viewport = null)
    {
        var engine = new LayoutEngine(new StyleEngine()) { ScrollState = store };
        engine.LayoutDocument(doc, viewport ?? Viewport);
    }

    private static ScrollState StateOf(ScrollStateStore store, Document doc, string id)
    {
        var el = doc.GetElementById(id);
        el.Should().NotBeNull();
        store.TryGet(el!, out var state).Should().BeTrue($"#{id} should have a scroll-store entry");
        return state;
    }

    // ---- Overflow measurement -----------------------------------------------

    [TestMethod]
    public void Deep_descendant_overflow_is_measured_not_just_direct_children()
    {
        var store = new ScrollStateStore();
        // The wide box is a *grandchild* of the scroller — the old
        // direct-children guess in WebviewPanel.ContentExtent misses it.
        var doc = Layout("""
            <body><div id=s style="overflow:auto;width:200px;height:100px;padding:10px">
              <div><div style="width:500px;height:30px"></div></div>
            </div></body>
            """, store);

        var s = StateOf(store, doc, "s");
        // Padding box: 200 content + 2*10 padding.
        s.ScrollportWidth.Should().Be(220);
        s.ScrollportHeight.Should().Be(120);
        // Overflow rect, padding-box space: 10 (left padding) + 500-wide grandchild.
        s.OverflowWidth.Should().Be(510);
        // Vertically the content fits: overflow floors at the scrollport.
        s.OverflowHeight.Should().Be(120);
        s.MaxOffsetX.Should().Be(290);
        s.MaxOffsetY.Should().Be(0);
    }

    [TestMethod]
    public void Positioned_descendants_extend_the_scrollable_overflow()
    {
        var store = new ScrollStateStore();
        var doc = Layout("""
            <body><div id=s style="position:relative;overflow:auto;width:200px;height:100px">
              <div style="position:absolute;left:0;top:250px;width:50px;height:40px"></div>
            </div></body>
            """, store);

        var s = StateOf(store, doc, "s");
        s.ScrollportHeight.Should().Be(100);
        // The absolutely positioned box bottoms out at 250 + 40.
        s.OverflowHeight.Should().Be(290);
        s.MaxOffsetY.Should().Be(190);
    }

    [TestMethod]
    public void Negative_margin_descendants_do_not_widen_the_scrolling_area()
    {
        var store = new ScrollStateStore();
        // Top/left overhang is unreachable in LTR: the scroll origin is the
        // padding-box corner, so a negative-margin child must not create
        // phantom scroll range (CSSOM View scrolling area).
        var doc = Layout("""
            <body><div id=s style="overflow:auto;width:200px;height:100px">
              <div style="margin-left:-50px;margin-top:-20px;width:100px;height:30px"></div>
            </div></body>
            """, store);

        var s = StateOf(store, doc, "s");
        s.OverflowWidth.Should().Be(s.ScrollportWidth);
        s.OverflowHeight.Should().Be(s.ScrollportHeight);
        s.MaxOffsetX.Should().Be(0);
        s.MaxOffsetY.Should().Be(0);
    }

    [TestMethod]
    public void Scrollport_is_the_padding_box()
    {
        var store = new ScrollStateStore();
        var doc = Layout("""
            <body><div id=s style="overflow:scroll;width:200px;height:100px;padding:10px;border:5px solid black">
              <div style="width:50px;height:20px"></div>
            </div></body>
            """, store);

        var s = StateOf(store, doc, "s");
        // Border box 230x130 minus 5px borders each side: the padding box.
        // Overlay scrollbars by decision, so no scrollbar inset either.
        s.ScrollportWidth.Should().Be(220);
        s.ScrollportHeight.Should().Be(120);
    }

    [TestMethod]
    public void Nested_scroller_overflow_does_not_leak_into_the_outer_container()
    {
        var store = new ScrollStateStore();
        var doc = Layout("""
            <body><div id=outer style="overflow:auto;width:200px;height:100px">
              <div id=inner style="overflow:auto;width:150px;height:80px">
                <div style="width:600px;height:300px"></div>
              </div>
            </div></body>
            """, store);

        var outer = StateOf(store, doc, "outer");
        var inner = StateOf(store, doc, "inner");

        // The inner scroller owns its 600x300 content; only its 150x80
        // border box contributes to the outer measurement.
        outer.OverflowWidth.Should().Be(outer.ScrollportWidth);
        outer.OverflowHeight.Should().Be(outer.ScrollportHeight);
        outer.MaxOffsetX.Should().Be(0);
        outer.MaxOffsetY.Should().Be(0);

        inner.OverflowWidth.Should().Be(600);
        inner.OverflowHeight.Should().Be(300);
    }

    [TestMethod]
    public void Overflow_hidden_boxes_get_no_v1_store_entry()
    {
        // CSS Overflow 3: hidden boxes are programmatically scrollable, but
        // v1 scopes the store to auto|scroll (scroll-model.md, Goals). They
        // still clip — see the nested-boundary test above.
        var store = new ScrollStateStore();
        var doc = Layout("""
            <body><div id=h style="overflow:hidden;width:200px;height:100px">
              <div style="width:600px;height:300px"></div>
            </div></body>
            """, store);

        store.TryGet(doc.GetElementById("h")!, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Root_entry_tracks_viewport_and_page_extent()
    {
        var store = new ScrollStateStore();
        Layout("""
            <body style="margin:0"><div style="height:2000px"></div></body>
            """, store);

        var root = store.Root;
        root.ScrollportWidth.Should().Be(Viewport.Width);
        root.ScrollportHeight.Should().Be(Viewport.Height);
        root.OverflowWidth.Should().Be(Viewport.Width);
        root.OverflowHeight.Should().BeGreaterThanOrEqualTo(2000);
    }

    // ---- Writes + post-layout clamp -------------------------------------------

    [TestMethod]
    public void Write_clamps_to_the_measured_range_and_flags_the_event()
    {
        var store = new ScrollStateStore();
        var doc = Layout(TallScroller(innerHeight: 300), store);
        var el = doc.GetElementById("s")!;

        store.Write(el, 0, 1000);

        store.TryGet(el, out var s).Should().BeTrue();
        s.OffsetY.Should().Be(200); // 300 content - 100 scrollport
        s.OffsetX.Should().Be(0);
        s.PendingEvent.Should().BeTrue();

        // Draining clears the flag and reports the element once.
        var targets = new List<Element>();
        store.DrainPendingEventTargets(targets, out var rootPending);
        targets.Should().ContainSingle().Which.Should().BeSameAs(el);
        rootPending.Should().BeFalse();
        store.TryGet(el, out s);
        s.PendingEvent.Should().BeFalse();

        // A write that does not move the offset does not re-flag.
        store.Write(el, 0, 200);
        store.TryGet(el, out s);
        s.PendingEvent.Should().BeFalse();
    }

    [TestMethod]
    public void Clamp_after_content_shrink_pulls_the_offset_in_and_flags()
    {
        var store = new ScrollStateStore();
        var doc = Layout(TallScroller(innerHeight: 300), store);
        var el = doc.GetElementById("s")!;

        store.Write(el, 0, 200); // pinned at max
        Drain(store);

        // Content shrinks: max offset drops from 200 to 50.
        doc.GetElementById("inner")!.SetAttribute("style", "height:150px");
        Relayout(doc, store);

        store.TryGet(el, out var s).Should().BeTrue();
        s.OverflowHeight.Should().Be(150);
        s.OffsetY.Should().Be(50);
        s.PendingEvent.Should().BeTrue("a clamp that moves the offset is a scroll");
    }

    [TestMethod]
    public void Clamp_after_scrollport_grow_pulls_the_offset_in_and_flags()
    {
        var store = new ScrollStateStore();
        var doc = Layout(TallScroller(innerHeight: 300), store);
        var el = doc.GetElementById("s")!;

        store.Write(el, 0, 200);
        Drain(store);

        // The scrollport grows to wrap the content: no scroll range remains.
        el.SetAttribute("style", "overflow:auto;width:200px;height:300px");
        Relayout(doc, store);

        store.TryGet(el, out var s).Should().BeTrue();
        s.ScrollportHeight.Should().Be(300);
        s.OffsetY.Should().Be(0);
        s.PendingEvent.Should().BeTrue();
    }

    [TestMethod]
    public void Unclamped_relayout_does_not_flag_a_phantom_scroll()
    {
        var store = new ScrollStateStore();
        var doc = Layout(TallScroller(innerHeight: 300), store);
        var el = doc.GetElementById("s")!;

        store.Write(el, 0, 50);
        Drain(store);

        Relayout(doc, store); // geometry unchanged — offset legal

        store.TryGet(el, out var s).Should().BeTrue();
        s.OffsetY.Should().Be(50);
        s.PendingEvent.Should().BeFalse();
    }

    [TestMethod]
    public void Entry_is_dropped_when_the_element_stops_being_a_scroll_container()
    {
        var store = new ScrollStateStore();
        var doc = Layout(TallScroller(innerHeight: 300), store);
        var el = doc.GetElementById("s")!;
        store.Write(el, 0, 100);

        el.SetAttribute("style", "width:200px;height:100px"); // overflow back to visible
        Relayout(doc, store);

        store.TryGet(el, out _).Should().BeFalse();
        store.GetOffset(el).Should().Be((0d, 0d));
    }

#if DEBUG
    [TestMethod]
    public void Writes_during_a_layout_pass_throw()
    {
        var store = new ScrollStateStore();
        var doc = Layout(TallScroller(innerHeight: 300), store);
        var el = doc.GetElementById("s")!;

        // Simulate the gate the layout engine holds for the duration of a
        // pass. A Write here means layout code is feeding offsets back into
        // itself — the exact bug the scroll-model doc forbids.
        store.BeginLayoutPass();
        try
        {
            var write = () => store.Write(el, 0, 10);
            write.Should().Throw<InvalidOperationException>();
            var writeRoot = () => store.WriteRoot(0, 10);
            writeRoot.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            store.EndLayoutPass();
        }

        // The gate reopens after the pass.
        store.Write(el, 0, 10);
        store.TryGet(el, out var s).Should().BeTrue();
        s.OffsetY.Should().Be(10);
    }
#endif

    [TestMethod]
    public void Incremental_layout_session_measures_and_clamps_too()
    {
        var doc = HtmlParser.Parse(TallScroller(innerHeight: 300));
        doc.RecordLayoutMutations = true;
        var store = new ScrollStateStore();
        var style = new StyleEngine();
        var session = new LayoutSession(style) { ScrollState = store };
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        var el = doc.GetElementById("s")!;
        store.TryGet(el, out var s).Should().BeTrue();
        s.OverflowHeight.Should().Be(300);

        store.Write(el, 0, 200);
        Drain(store);

        doc.GetElementById("inner")!.SetAttribute("style", "height:150px");
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

        store.TryGet(el, out s).Should().BeTrue();
        s.OffsetY.Should().Be(50);
        s.PendingEvent.Should().BeTrue();
    }

    private static string TallScroller(int innerHeight) => $"""
        <body><div id=s style="overflow:auto;width:200px;height:100px">
          <div id=inner style="height:{innerHeight}px"></div>
        </div></body>
        """;

    private static void Drain(ScrollStateStore store)
        => store.DrainPendingEventTargets([], out _);
}
