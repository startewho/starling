using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Html;
using Starling.Layout.Incremental;
using Starling.Layout.Scroll;
using Starling.Layout.Text;

namespace Starling.Layout.Tests.Scroll;

/// <summary>
/// WP1 follow-up (browser-plan/scroll-model.md): scroll measurement must not
/// re-walk the whole box tree on every incremental relayout. An incremental
/// pass re-records only the scroll containers it actually re-laid (observed
/// through the store's <see cref="ScrollStateStore.GeometryRecords"/> counter)
/// and reconciles the document extent from the per-box extent caches. Also
/// covers the two WP1 review minors: block-end padding joining scrollHeight,
/// and the nesting-safe layout-pass gate.
/// </summary>
[TestClass]
public sealed class ScrollMeasureScopeTests
{
    private static readonly Size Viewport = new(800, 600);

    private static (Document Doc, LayoutSession Session, ScrollStateStore Store) StartSession(string html)
    {
        var doc = HtmlParser.Parse(html);
        doc.RecordLayoutMutations = true;
        var store = new ScrollStateStore();
        var session = new LayoutSession(new StyleEngine()) { ScrollState = store };
        session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);
        return (doc, session, store);
    }

    private static void Relayout(LayoutSession session, Document doc)
        => session.Layout(doc, Viewport, DefaultTextMeasurer.Instance, nowMs: null);

    private static ScrollState StateOf(ScrollStateStore store, Document doc, string id)
    {
        var el = doc.GetElementById(id);
        el.Should().NotBeNull();
        store.TryGet(el!, out var state).Should().BeTrue($"#{id} should have a scroll-store entry");
        return state;
    }

    // ---- Scoped incremental re-measurement ------------------------------------

    [TestMethod]
    public void Incremental_relayout_remeasures_only_the_dirty_scroller()
    {
        var (doc, session, store) = StartSession("""
            <body style="margin:0">
              <div id=a style="overflow:auto;width:200px;height:100px">
                <div id=ia style="height:300px"></div>
              </div>
              <div id=b style="overflow:auto;width:200px;height:100px">
                <div style="height:400px"></div>
              </div>
            </body>
            """);

        StateOf(store, doc, "a").OverflowHeight.Should().Be(300);
        var siblingBefore = StateOf(store, doc, "b");
        var recordsBefore = store.GeometryRecords;

        // Dirty #a's subtree only; #b's clean subtree is reused in place.
        doc.GetElementById("ia")!.SetAttribute("style", "height:500px");
        Relayout(session, doc);

        StateOf(store, doc, "a").OverflowHeight.Should().Be(500,
            "the dirty scroller's overflow must reflect the relaid subtree");
        store.GeometryRecords.Should().Be(recordsBefore + 1,
            "only the relaid scroller may be re-measured — an untouched sibling " +
            "must not pay the measure walk on an incremental relayout");
        StateOf(store, doc, "b").Should().Be(siblingBefore,
            "an untouched sibling scroller's entry must be byte-identical");
    }

    [TestMethod]
    public void Scoped_pass_reconciles_the_root_extent_without_remeasuring_scrollers()
    {
        var (doc, session, store) = StartSession("""
            <body style="margin:0">
              <div id=grow style="height:100px"></div>
              <div id=s style="overflow:auto;width:200px;height:100px">
                <div style="height:300px"></div>
              </div>
            </body>
            """);

        var entryBefore = StateOf(store, doc, "s");
        var recordsBefore = store.GeometryRecords;

        // Grows the page, touches no scroll container.
        doc.GetElementById("grow")!.SetAttribute("style", "height:2000px");
        Relayout(session, doc);

        store.Root.OverflowHeight.Should().BeGreaterThanOrEqualTo(2100,
            "the document extent must reconcile on a scoped pass");
        store.GeometryRecords.Should().Be(recordsBefore,
            "no scroll container was relaid, so none may be re-measured");
        StateOf(store, doc, "s").Should().Be(entryBefore);
    }

    [TestMethod]
    public void Entry_is_dropped_on_an_incremental_pass_when_overflow_is_removed()
    {
        var (doc, session, store) = StartSession("""
            <body style="margin:0">
              <div id=s style="overflow:auto;width:200px;height:100px">
                <div style="height:300px"></div>
              </div>
            </body>
            """);

        var el = doc.GetElementById("s")!;
        store.Write(el, 0, 50);

        // Same drop semantics as a full layout: the element stopped being a
        // scroll container, so its entry (and offset) must go, even though the
        // scoped pass never ran the full measure walk.
        el.SetAttribute("style", "width:200px;height:100px");
        Relayout(session, doc);

        store.TryGet(el, out _).Should().BeFalse();
        store.GetOffset(el).Should().Be((0d, 0d));
    }

    [TestMethod]
    public void Incremental_relayout_inside_a_scroller_updates_it_through_the_scoped_path()
    {
        // The deep-mutation variant: the dirty box is a grandchild, the
        // scroller is found because the dirty path relays it, not because the
        // mutation targeted it.
        var (doc, session, store) = StartSession("""
            <body style="margin:0">
              <div id=s style="overflow:auto;width:200px;height:100px">
                <div><div id=deep style="width:100px;height:30px"></div></div>
              </div>
            </body>
            """);

        StateOf(store, doc, "s").OverflowWidth.Should().Be(200);

        doc.GetElementById("deep")!.SetAttribute("style", "width:700px;height:30px");
        Relayout(session, doc);

        StateOf(store, doc, "s").OverflowWidth.Should().Be(700);
        StateOf(store, doc, "s").MaxOffsetX.Should().Be(500);
    }

    [TestMethod]
    public void Consecutive_scoped_passes_stay_coherent()
    {
        // The steady-state animation-tick pattern: full layout once, then
        // scoped relayouts every frame. The caches a scoped pass revalidates
        // must serve the next scoped pass.
        var (doc, session, store) = StartSession("""
            <body style="margin:0">
              <div id=a style="overflow:auto;width:200px;height:100px">
                <div id=ia style="height:300px"></div>
              </div>
              <div id=b style="overflow:auto;width:200px;height:100px">
                <div style="height:400px"></div>
              </div>
            </body>
            """);

        var records = store.GeometryRecords;
        for (var height = 500; height <= 700; height += 100)
        {
            doc.GetElementById("ia")!.SetAttribute("style", $"height:{height}px");
            Relayout(session, doc);

            StateOf(store, doc, "a").OverflowHeight.Should().Be(height);
            store.GeometryRecords.Should().Be(records + 1,
                "each tick re-measures exactly the relaid scroller");
            records = store.GeometryRecords;
        }
        StateOf(store, doc, "b").OverflowHeight.Should().Be(400);
    }

    // ---- Review minor (a): block-end padding ----------------------------------

    [TestMethod]
    public void Block_end_padding_joins_scrollheight_when_content_overflows_the_block_axis()
    {
        var store = new ScrollStateStore();
        var doc = HtmlParser.Parse("""
            <body>
              <div id=s style="overflow:auto;width:200px;height:100px;padding:10px">
                <div style="height:300px"></div>
              </div>
              <div id=t style="overflow:auto;width:200px;height:100px;padding:10px">
                <div style="height:80px"></div>
              </div>
              <div id=u style="overflow:auto;width:200px;height:100px;padding:10px">
                <div style="width:500px;height:30px"></div>
              </div>
            </body>
            """);
        new LayoutEngine(new StyleEngine()) { ScrollState = store }.LayoutDocument(doc, Viewport);

        // Chromium: 10px top padding + 300px child + 10px bottom padding.
        var s = StateOf(store, doc, "s");
        s.ScrollportHeight.Should().Be(120);
        s.OverflowHeight.Should().Be(320);
        s.MaxOffsetY.Should().Be(200);

        // Content that fits the content box keeps scrollHeight == clientHeight.
        var t = StateOf(store, doc, "t");
        t.OverflowHeight.Should().Be(t.ScrollportHeight);

        // The inline axis stays content-only (Chromium block-container
        // behavior): no inline-end padding joins scrollWidth.
        var u = StateOf(store, doc, "u");
        u.OverflowWidth.Should().Be(510);
    }

    // ---- Review minor (b): nesting-safe layout gate ----------------------------

#if DEBUG
    [TestMethod]
    public void Layout_pass_gate_is_nesting_safe()
    {
        var store = new ScrollStateStore();
        var doc = HtmlParser.Parse("""
            <body><div id=s style="overflow:auto;width:200px;height:100px">
              <div style="height:300px"></div>
            </div></body>
            """);
        new LayoutEngine(new StyleEngine()) { ScrollState = store }.LayoutDocument(doc, Viewport);
        var el = doc.GetElementById("s")!;

        // A nested pass (e.g. a forced flush mid-pass) must not reopen the
        // outer pass's write gate when it ends.
        store.BeginLayoutPass();
        store.BeginLayoutPass();
        store.EndLayoutPass();
        try
        {
            var write = () => store.Write(el, 0, 10);
            write.Should().Throw<InvalidOperationException>(
                "one nested EndLayoutPass must not reopen the outer gate");
        }
        finally
        {
            store.EndLayoutPass();
        }

        store.Write(el, 0, 10);
        store.TryGet(el, out var s).Should().BeTrue();
        s.OffsetY.Should().Be(10);
    }
#endif
}
