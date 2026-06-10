// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Scroll;
using Starling.Layout.Text;

namespace Starling.Gui.Tests;

/// <summary>
/// WP5 (browser-plan/scroll-model.md): the hit-tester mirrors the painter's
/// sticky shift — a click on the STUCK position lands on the sticky element,
/// and the vacated natural slot belongs to the content that scrolled in.
/// </summary>
[TestClass]
public sealed class StickyHitTestTests
{
    private const string Page = """
        <body style='margin:0'>
          <div id=sc style='overflow:auto;width:200px;height:100px'>
            <div id=wrap>
              <div id=lead style='height:40px'></div>
              <div id=st style='position:sticky;top:0;height:20px'></div>
              <div id=content style='height:340px'></div>
            </div>
          </div>
        </body>
        """;

    private static (BlockBox Root, Starling.Dom.Document Doc, ScrollStateStore Store) Layout()
    {
        var doc = HtmlParser.Parse(Page);
        var store = new ScrollStateStore();
        var root = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance) { ScrollState = store }
            .LayoutDocument(doc, new Size(800, 600));
        return (root, doc, store);
    }

    [TestMethod]
    public void Click_on_the_stuck_position_hits_the_sticky_element()
    {
        var (root, doc, store) = Layout();
        store.Write(doc.GetElementById("sc")!, 0, 100);

        var hit = BoxHitTester.HitTest(root, 100, 10, viewportX: 0, viewportY: 0,
            store.GetOffset, store.GetStickyShift);

        hit.IsHit.Should().BeTrue();
        hit.Box!.Element.Should().BeSameAs(doc.GetElementById("st"),
            "the painter pins the header at the scrollport top, so the click must land on it");
    }

    [TestMethod]
    public void Click_below_the_stuck_band_hits_the_scrolled_in_content()
    {
        var (root, doc, store) = Layout();
        store.Write(doc.GetElementById("sc")!, 0, 100);

        var hit = BoxHitTester.HitTest(root, 100, 30, viewportX: 0, viewportY: 0,
            store.GetOffset, store.GetStickyShift);

        hit.Box!.Element.Should().BeSameAs(doc.GetElementById("content"),
            "the header vacated its natural slot; the content that scrolled in owns it");
    }

    [TestMethod]
    public void Unscrolled_click_hits_the_header_at_its_natural_position()
    {
        var (root, doc, store) = Layout();

        var hit = BoxHitTester.HitTest(root, 100, 50, viewportX: 0, viewportY: 0,
            store.GetOffset, store.GetStickyShift);

        hit.Box!.Element.Should().BeSameAs(doc.GetElementById("st"));
    }
}
