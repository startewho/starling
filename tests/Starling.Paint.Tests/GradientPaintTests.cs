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
/// CSS Images 3 §3 — pixel-probe coverage for the <see cref="FillGradient"/>
/// display item rasterized through <see cref="ImageSharpBackend"/>'s gradient
/// brushes. The headline check: a left→right red→blue linear gradient must be
/// red-dominant near the left edge and blue-dominant near the right edge.
/// </summary>
[TestClass]
public sealed class GradientPaintTests
{
    private static readonly CssColor Red = new(255, 0, 0);
    private static readonly CssColor Blue = new(0, 0, 255);

    private static CssGradient Linear(CssGradientLine? line, params CssColorStop[] stops)
        => new(CssGradientKind.Linear, Repeating: false, stops, Line: line);

    [TestMethod]
    public void Linear_90deg_red_to_blue_is_red_left_blue_right()
    {
        var gradient = Linear(
            CssGradientLine.FromAngle(90), // to right
            new CssColorStop(Red),
            new CssColorStop(Blue));

        var list = new PaintList();
        list.Add(new FillGradient(new LayoutRect(0, 0, 100, 40), gradient));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(100, 40));

        // Probe at 10% width (x=10) and 90% width (x=90), mid-height.
        var (lr, _, lb, _) = bmp.GetPixel(10, 20);
        var (rr, _, rb, _) = bmp.GetPixel(90, 20);

        lr.Should().BeGreaterThan(lb, "left side of a red→blue gradient should be red-dominant");
        rb.Should().BeGreaterThan(rr, "right side of a red→blue gradient should be blue-dominant");
    }

    [TestMethod]
    public void Linear_to_right_keyword_matches_90deg()
    {
        var gradient = Linear(
            CssGradientLine.FromSide(CssGradientSideX.Right, CssGradientSideY.None),
            new CssColorStop(Red),
            new CssColorStop(Blue));

        var list = new PaintList();
        list.Add(new FillGradient(new LayoutRect(0, 0, 100, 20), gradient));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(100, 20));

        bmp.GetPixel(5, 10).Item1.Should().BeGreaterThan(bmp.GetPixel(5, 10).Item3);
        bmp.GetPixel(95, 10).Item3.Should().BeGreaterThan(bmp.GetPixel(95, 10).Item1);
    }

    [TestMethod]
    public void Radial_gradient_paints_center_color_at_center()
    {
        var gradient = new CssGradient(
            CssGradientKind.Radial,
            Repeating: false,
            new[] { new CssColorStop(Red), new CssColorStop(Blue) },
            Shape: CssRadialShape.Circle,
            Size: CssRadialSize.FarthestCorner,
            Position: CssGradientPosition.Center);

        var list = new PaintList();
        list.Add(new FillGradient(new LayoutRect(0, 0, 60, 60), gradient));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(60, 60));

        // Center is the first stop (red); a corner trends toward the last (blue).
        var (cr, _, cb, _) = bmp.GetPixel(30, 30);
        cr.Should().BeGreaterThan(cb, "the radial gradient center should show the first (red) stop");
    }

    [TestMethod]
    public void Three_stop_gradient_shows_middle_color()
    {
        var gradient = Linear(
            CssGradientLine.FromAngle(90),
            new CssColorStop(Red, new CssGradientStopPosition(0, IsPercent: true)),
            new CssColorStop(new CssColor(0, 255, 0), new CssGradientStopPosition(50, IsPercent: true)),
            new CssColorStop(Blue, new CssGradientStopPosition(100, IsPercent: true)));

        var list = new PaintList();
        list.Add(new FillGradient(new LayoutRect(0, 0, 100, 20), gradient));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(100, 20));

        var (mr, mg, mb, _) = bmp.GetPixel(50, 10);
        ((int)mg).Should().BeGreaterThan(mr, "the middle stop is green");
        ((int)mg).Should().BeGreaterThan(mb, "the middle stop is green");
    }

    [TestMethod]
    public void Css_background_gradient_emits_fill_gradient_through_layout()
    {
        // End-to-end: CSS `background-image: linear-gradient(...)` must flow
        // through the parser, layout, and EmitBackgroundImage to a FillGradient
        // — no image resolver required.
        var document = HtmlParser.Parse("""
            <body style="margin:0">
              <div style="width:100px; height:40px;
                          background-image: linear-gradient(90deg, red, blue)"></div>
            </body>
            """);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance, images: null);
        var root = engine.LayoutDocument(document, new Size(800, 600));
        var list = new DisplayListBuilder().Build(root, styleOverride: null, images: null);

        var fills = list.Items.OfType<FillGradient>().ToList();
        fills.Should().NotBeEmpty("a CSS linear-gradient background must emit a FillGradient");
        var fill = fills[0];
        fill.Gradient.Kind.Should().Be(CssGradientKind.Linear);
        fill.Gradient.Stops.Should().HaveCount(2);
        fill.Bounds.Width.Should().BeApproximately(100, 0.5);
        fill.Bounds.Height.Should().BeApproximately(40, 0.5);
    }

    [TestMethod]
    public void Css_conic_gradient_emits_nothing()
    {
        // conic-gradient has no ImageSharp brush; it must fail soft (no FillGradient,
        // no throw) per the deferred-conic note.
        var document = HtmlParser.Parse("""
            <body style="margin:0">
              <div style="width:50px; height:50px;
                          background-image: conic-gradient(red, blue)"></div>
            </body>
            """);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance, images: null);
        var root = engine.LayoutDocument(document, new Size(800, 600));
        var list = new DisplayListBuilder().Build(root, styleOverride: null, images: null);

        list.Items.OfType<FillGradient>().Should().BeEmpty("conic gradients are not paintable yet");
    }

    [TestMethod]
    public void Gradient_fills_whole_box_no_white_remains()
    {
        var gradient = Linear(
            CssGradientLine.FromAngle(90),
            new CssColorStop(Red),
            new CssColorStop(Blue));

        var list = new PaintList();
        list.Add(new FillGradient(new LayoutRect(0, 0, 50, 50), gradient));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(50, 50));

        // The 50x50 box covers the entire 50x50 canvas; effectively no white left.
        BitmapPixels.CountNonWhite(bmp).Should().BeGreaterThan(50 * 50 - 50,
            "a full-box gradient should leave essentially no white background");
    }
}
