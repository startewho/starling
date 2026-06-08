// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Values;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Pixel-probe tests for the real clipping substrate (css-overflow-3 §2):
/// <c>overflow: hidden/clip/scroll/auto</c> crops descendants at the box edge,
/// and <c>border-radius</c> makes that crop follow the rounded inner edge.
/// </summary>
[TestClass]
public sealed class ClipPaintTests
{
    // -----------------------------------------------------------------------
    // Display-list builder tests — verify PushClip/PopClip are emitted
    // -----------------------------------------------------------------------

    private static DisplayList.DisplayList BuildList(string html)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        return new DisplayListBuilder().Build(root);
    }

    [TestMethod]
    public void Overflow_hidden_parent_emits_push_and_pop_clip()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:100px;height:100px;overflow:hidden'>" +
            "<div style='width:200px;height:200px;background:red'></div>" +
            "</div></body>");

        dl.Items.OfType<PushClip>().Should().NotBeEmpty("overflow:hidden must emit a PushClip");
        dl.Items.OfType<PopClip>().Should().NotBeEmpty("overflow:hidden must emit a PopClip");
        dl.Items.OfType<PushClip>().Count()
            .Should().Be(dl.Items.OfType<PopClip>().Count(), "clips must be balanced");
    }

    [TestMethod]
    public void Non_clipping_overflow_visible_does_not_emit_push_clip()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:100px;height:100px;overflow:visible'>" +
            "<div style='width:200px;height:200px;background:red'></div>" +
            "</div></body>");

        dl.Items.OfType<PushClip>().Should().BeEmpty("overflow:visible must NOT emit a clip");
    }

    [TestMethod]
    public void Overflow_hidden_with_border_radius_emits_rounded_push_clip()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:100px;height:100px;overflow:hidden;border-radius:20px'>" +
            "<div style='width:200px;height:200px;background:blue'></div>" +
            "</div></body>");

        var pushClips = dl.Items.OfType<PushClip>().ToList();
        pushClips.Should().NotBeEmpty("overflow:hidden with border-radius must emit a PushClip");
        pushClips.Should().Contain(pc => !pc.Radii.IsZero,
            "the PushClip must carry the border-radius so the backend clips to the rounded shape");
    }

    [TestMethod]
    public void Overflow_clip_keyword_emits_push_clip()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:100px;height:100px;overflow:clip'>" +
            "<div style='background:green;width:300px;height:50px'></div>" +
            "</div></body>");

        dl.Items.OfType<PushClip>().Should().NotBeEmpty("overflow:clip must emit a PushClip");
    }

    // -----------------------------------------------------------------------
    // Pixel-probe tests — drive through the backend, verify paint is cropped
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Child_overflowing_overflow_hidden_parent_is_cropped_at_edge()
    {
        // A 100×100 parent with overflow:hidden containing a 200×200 red child.
        // The red child overflows the right and bottom edges. Pixels inside the
        // parent box should be red; pixels just outside the right edge (at x=110)
        // and at a y inside the parent height should be white (cropped).
        const int parentW = 100, parentH = 100;
        var html =
            "<body style='margin:0'>" +
            $"<div style='width:{parentW}px;height:{parentH}px;overflow:hidden'>" +
            "<div style='width:200px;height:200px;background:#ff0000'></div>" +
            "</div></body>";

        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        var dl = new DisplayListBuilder().Build(root);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 300));

        // Well inside the parent box: should be red.
        bmp.GetPixel(parentW / 2, parentH / 2).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "pixels inside the overflow:hidden parent must paint red");

        // Just outside the right edge (x = parentW + 5). The child overflows here
        // but the clip must prevent it from painting.
        bmp.GetPixel(parentW + 5, parentH / 2).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixels past the right edge of the overflow:hidden box must stay white");

        // Just outside the bottom edge (y = parentH + 5).
        bmp.GetPixel(parentW / 2, parentH + 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixels past the bottom edge of the overflow:hidden box must stay white");
    }

    [TestMethod]
    public void Child_overflowing_overflow_clip_parent_is_cropped_at_edge()
    {
        // Same as above but with overflow:clip keyword.
        const int parentW = 80, parentH = 80;
        var html =
            "<body style='margin:0'>" +
            $"<div style='width:{parentW}px;height:{parentH}px;overflow:clip'>" +
            "<div style='width:160px;height:160px;background:#00cc00'></div>" +
            "</div></body>";

        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        var dl = new DisplayListBuilder().Build(root);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 300));

        // Inside the parent — green.
        bmp.GetPixel(parentW / 2, parentH / 2).Should().Be(
            ((byte)0, (byte)204, (byte)0, (byte)255),
            "pixels inside the overflow:clip parent must paint green");

        // Past the right edge — white.
        bmp.GetPixel(parentW + 5, parentH / 2).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixels past the right edge of the overflow:clip box must stay white");
    }

    [TestMethod]
    public void Rounded_overflow_hidden_clips_to_corner()
    {
        // A 100×100 parent with overflow:hidden and border-radius:20px; a 200×200
        // blue child fills it and beyond. The extreme corner pixel of the parent
        // box (top-left, at ~(2,2)) is inside the border-box but outside the
        // rounded shape and must stay white. The center must be blue. A pixel
        // well outside the box (at x=110) must also stay white.
        const int parentW = 100, parentH = 100;
        const int radius = 20;
        var html =
            "<body style='margin:0'>" +
            $"<div style='width:{parentW}px;height:{parentH}px;overflow:hidden;border-radius:{radius}px'>" +
            "<div style='width:200px;height:200px;background:#0000ff'></div>" +
            "</div></body>";

        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        var dl = new DisplayListBuilder().Build(root);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 300));

        // Center of the box — blue.
        bmp.GetPixel(parentW / 2, parentH / 2).Should().Be(
            ((byte)0, (byte)0, (byte)255, (byte)255),
            "center of a rounded overflow:hidden box must be painted");

        // Extreme top-left corner pixel (inside the border box but inside the
        // rounded corner zone). With a 20px radius this pixel is cut by the
        // rounding.
        var (r, g, b, _) = bmp.GetPixel(2, 2);
        (r == 255 && g == 255 && b == 255).Should().BeTrue(
            "the extreme top-left corner of a rounded clip must stay white (cut by the radius)");

        // Past the right edge — white.
        bmp.GetPixel(parentW + 5, parentH / 2).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixels past the right edge of a rounded overflow:hidden box must stay white");
    }

    [TestMethod]
    public void Nested_clips_intersect_correctly()
    {
        // An outer 100×100 overflow:hidden, inner 60×100 overflow:hidden at
        // left:0. A 200×200 red child fills both. Only pixels inside the inner
        // box (0..59 in x) should be red; x=70 (inside outer, outside inner)
        // should be white.
        var html =
            "<body style='margin:0'>" +
            "<div style='position:relative;width:100px;height:100px;overflow:hidden'>" +
            "<div style='width:60px;height:100px;overflow:hidden'>" +
            "<div style='width:200px;height:200px;background:#ff0000'></div>" +
            "</div></div></body>";

        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        var dl = new DisplayListBuilder().Build(root);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 300));

        // Inside the inner clip — red.
        bmp.GetPixel(30, 30).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "x=30 is inside both clips, should be red");

        // Inside outer but outside inner — white.
        bmp.GetPixel(70, 30).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "x=70 is inside the outer clip but outside the inner 60px clip; must stay white");
    }

    // -----------------------------------------------------------------------
    // Direct display-list tests — hand-built lists check the backend switch
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Backend_PushClip_clips_subsequent_fill_to_bounds()
    {
        // Hand-build a list: PushClip(50×50 at origin), FillRect(200×200 red),
        // PopClip. Only the 50×50 region must be red.
        var list = new PaintList();
        list.Add(new PushClip(new LayoutRect(0, 0, 50, 50)));
        list.Add(new FillRect(new LayoutRect(0, 0, 200, 200), new CssColor(255, 0, 0, 255), FillRectPixelAlignment.Preserve));
        list.Add(PopClip.Instance);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(200, 200));

        // Inside the 50×50 clip — red.
        bmp.GetPixel(25, 25).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "pixels inside the PushClip region must be painted");

        // Outside the clip — white.
        bmp.GetPixel(75, 25).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixels outside the PushClip region must stay white");
        bmp.GetPixel(25, 75).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixels below the PushClip region must stay white");
    }

    [TestMethod]
    public void Backend_rounded_PushClip_cuts_extreme_corner()
    {
        // Hand-build: PushClip(100×100 at 10,10 with 25px radii), FillRect, PopClip.
        // The extreme corner pixel of the clip region (10,10) must remain white
        // (cut by the radius); the center must be red.
        const int bx = 10, by = 10, bw = 100, bh = 100;
        var radii = CornerRadii.Uniform(25, 25, 25, 25);

        var list = new PaintList();
        list.Add(new PushClip(new LayoutRect(bx, by, bw, bh), radii));
        list.Add(new FillRect(new LayoutRect(0, 0, 300, 300), new CssColor(255, 0, 0, 255), FillRectPixelAlignment.Preserve));
        list.Add(PopClip.Instance);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(200, 200));

        // Center — red.
        bmp.GetPixel(bx + bw / 2, by + bh / 2).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "center of the rounded clip must be painted");

        // Extreme top-left corner of the clip box — cut by the 25px radius.
        var (r, g, b, _) = bmp.GetPixel(bx + 2, by + 2);
        (r == 255 && g == 255 && b == 255).Should().BeTrue(
            "the extreme top-left corner of a rounded PushClip must stay white");

        // Outside the clip to the right — white.
        bmp.GetPixel(bx + bw + 5, by + bh / 2).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixels past the right edge of the PushClip must stay white");
    }

    [TestMethod]
    public void Backend_after_PopClip_items_paint_normally()
    {
        // PushClip(50×50), FillRect(red), PopClip, then FillRect(blue, 200×200).
        // After PopClip the blue fill must cover the full 200×200 — the clip is gone.
        var list = new PaintList();
        list.Add(new PushClip(new LayoutRect(0, 0, 50, 50)));
        list.Add(new FillRect(new LayoutRect(0, 0, 200, 200), new CssColor(255, 0, 0, 255), FillRectPixelAlignment.Preserve));
        list.Add(PopClip.Instance);
        list.Add(new FillRect(new LayoutRect(0, 0, 200, 200), new CssColor(0, 0, 255, 255), FillRectPixelAlignment.Preserve));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(200, 200));

        // After PopClip the blue rect covers everything, including the area that was
        // outside the old clip.
        bmp.GetPixel(100, 100).Should().Be(
            ((byte)0, (byte)0, (byte)255, (byte)255),
            "after PopClip, draws outside the old clip rect must paint normally");
    }

    [TestMethod]
    public void Backend_nested_clips_intersect()
    {
        // Outer clip: 0..100 in x. Inner clip: 0..60 in x. Red fill 0..200.
        // Result: only 0..60 in x should be red.
        var list = new PaintList();
        list.Add(new PushClip(new LayoutRect(0, 0, 100, 100)));
        list.Add(new PushClip(new LayoutRect(0, 0, 60, 100)));
        list.Add(new FillRect(new LayoutRect(0, 0, 200, 100), new CssColor(255, 0, 0, 255), FillRectPixelAlignment.Preserve));
        list.Add(PopClip.Instance);
        list.Add(PopClip.Instance);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(200, 200));

        bmp.GetPixel(30, 50).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "x=30 is inside both clips, must be red");

        bmp.GetPixel(70, 50).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "x=70 is inside outer clip but outside inner clip, must be white");

        bmp.GetPixel(110, 50).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "x=110 is outside both clips, must be white");
    }
}
