// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Common.Image;
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
/// Pixel-probe tests for CSS Masking 1 §7 <c>clip-path</c> painting via
/// basic shapes: <c>circle()</c>, <c>inset()</c>, and <c>polygon()</c>.
/// Each test renders a colored box, applies a clip-path that excludes the
/// corners or specific regions, and verifies painted vs transparent pixels.
/// </summary>
[TestClass]
public sealed class ClipPathPaintTests
{
    // Shared colors
    private static readonly CssColor Red = new(255, 0, 0, 255);
    private static readonly CssColor Blue = new(0, 0, 255, 255);

    // -----------------------------------------------------------------------
    // Display-list builder integration tests — verify PushClipPath/PopClipPath
    // are emitted for clip-path CSS properties.
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
    public void ClipPath_circle_emits_push_and_pop_clip_path()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:100px;height:100px;background:red;clip-path:circle(40px at 50px 50px)'></div>" +
            "</body>");

        dl.Items.OfType<PushClipPath>().Should().NotBeEmpty("clip-path:circle() must emit PushClipPath");
        dl.Items.OfType<PopClipPath>().Should().NotBeEmpty("clip-path:circle() must emit PopClipPath");
        dl.Items.OfType<PushClipPath>().Count()
            .Should().Be(dl.Items.OfType<PopClipPath>().Count(), "PushClipPath/PopClipPath must be balanced");
    }

    [TestMethod]
    public void ClipPath_none_does_not_emit_clip_path_items()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:100px;height:100px;background:red;clip-path:none'></div>" +
            "</body>");

        dl.Items.OfType<PushClipPath>().Should().BeEmpty("clip-path:none must not emit any clip-path items");
    }

    [TestMethod]
    public void ClipPath_push_clip_path_carries_reference_box()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:80px;height:60px;background:blue;clip-path:inset(10px)'></div>" +
            "</body>");

        var push = dl.Items.OfType<PushClipPath>().FirstOrDefault();
        push.Should().NotBeNull("inset() clip-path must emit a PushClipPath");
        push!.ReferenceBox.Width.Should().BeGreaterThan(0, "the reference box must have a positive width");
        push.ReferenceBox.Height.Should().BeGreaterThan(0, "the reference box must have a positive height");
    }

    // -----------------------------------------------------------------------
    // Pixel-probe tests — build a display list by hand and verify paint output
    // -----------------------------------------------------------------------

    private static RenderedBitmap RenderList(PaintList list, int width, int height)
    {
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        return backend.Render(list, new LayoutSize(width, height));
    }

    // ---- circle() -----------------------------------------------------------

    /// <summary>
    /// circle(40px at 50px 50px) on a 100x100 red box: center must be red,
    /// the four extreme corners (which lie outside the 40px circle) must be white.
    /// </summary>
    [TestMethod]
    public void Circle_clip_leaves_center_red_and_corners_transparent()
    {
        var refBox = new LayoutRect(0, 0, 100, 100);
        // circle(40px at 50% 50%)
        var shape = new CssCircleShape(
            Radius: CssLengthPercentage.FromLength(new CssLength(40, CssLengthUnit.Px)),
            RadiusKeyword: null,
            Position: CssShapePosition.Center);
        var clip = CssClipPath.FromShape(shape);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(refBox, Red, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 100, 100);

        // Center of the circle — must be red.
        bmp.GetPixel(50, 50).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "the center of the circle clip must be painted red");

        // Extreme corner at (2, 2) — outside the 40px circle, must stay white.
        var (r, g, b, _) = bmp.GetPixel(2, 2);
        (r == 255 && g == 255 && b == 255).Should().BeTrue(
            "the extreme top-left corner is outside the 40px circle and must remain white");

        // Extreme corner at (97, 2).
        var (r2, g2, b2, _) = bmp.GetPixel(97, 2);
        (r2 == 255 && g2 == 255 && b2 == 255).Should().BeTrue(
            "the extreme top-right corner is outside the 40px circle and must remain white");

        // Extreme corner at (2, 97).
        var (r3, g3, b3, _) = bmp.GetPixel(2, 97);
        (r3 == 255 && g3 == 255 && b3 == 255).Should().BeTrue(
            "the extreme bottom-left corner is outside the 40px circle and must remain white");
    }

    /// <summary>
    /// circle() with default radius (closest-side): 50px radius on a 100x100 box.
    /// Center must be painted; extreme corners must be outside and white.
    /// </summary>
    [TestMethod]
    public void Circle_default_radius_closest_side_clips_corners()
    {
        var refBox = new LayoutRect(0, 0, 100, 100);
        var shape = new CssCircleShape(
            Radius: null,
            RadiusKeyword: null, // default = closest-side
            Position: CssShapePosition.Center);
        var clip = CssClipPath.FromShape(shape);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(refBox, Blue, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 100, 100);

        // Center must be blue.
        var (rc, gc, bc, _) = bmp.GetPixel(50, 50);
        bc.Should().BeGreaterThan(200, "the center of the closest-side circle must be painted blue");

        // Extreme corner is at distance sqrt(50²+50²) ≈ 70.7 from center, well
        // outside the 50px closest-side radius — must stay white.
        var (r, g, b, _) = bmp.GetPixel(2, 2);
        (r == 255 && g == 255 && b == 255).Should().BeTrue(
            "the extreme top-left corner must remain white (outside closest-side circle)");
    }

    // ---- inset() ------------------------------------------------------------

    /// <summary>
    /// inset(20px): a 20px inset on all sides of a 100x100 box. The inset
    /// region (x=20..80, y=20..80) must be red; pixels outside (e.g. x=5)
    /// must stay white.
    /// </summary>
    [TestMethod]
    public void Inset_clips_to_inner_rect_and_leaves_margin_white()
    {
        var refBox = new LayoutRect(0, 0, 100, 100);
        var inset20 = CssLengthPercentage.FromLength(new CssLength(20, CssLengthUnit.Px));
        var shape = new CssInsetShape(inset20, inset20, inset20, inset20, Radii: null);
        var clip = CssClipPath.FromShape(shape);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(refBox, Red, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 100, 100);

        // Center of the inset rect (50, 50) — must be red.
        bmp.GetPixel(50, 50).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "the center of the inset region must be painted red");

        // Just outside the left inset (x=5, y=50) — must stay white.
        bmp.GetPixel(5, 50).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "the pixel in the left margin (x=5) must stay white");

        // Just outside the top inset (x=50, y=5) — must stay white.
        bmp.GetPixel(50, 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "the pixel in the top margin (y=5) must stay white");

        // Just inside the inset on all sides (30, 30) — must be red.
        bmp.GetPixel(30, 30).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "pixel (30,30) is inside the 20px inset and must be red");
    }

    /// <summary>
    /// inset(0 50px 0 0): only the left half of the box is visible.
    /// Pixels in the right half (x=70) must stay white; left half (x=20) must be red.
    /// </summary>
    [TestMethod]
    public void Inset_asymmetric_right_offset_clips_right_half()
    {
        var refBox = new LayoutRect(0, 0, 100, 100);
        var zero = CssLengthPercentage.FromLength(CssLength.Zero);
        var half = CssLengthPercentage.FromLength(new CssLength(50, CssLengthUnit.Px));
        // inset(0 50px 0 0): top=0, right=50, bottom=0, left=0
        var shape = new CssInsetShape(zero, half, zero, zero, Radii: null);
        var clip = CssClipPath.FromShape(shape);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(refBox, Blue, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 100, 100);

        // Left half (x=20) — blue.
        var (r, g, b, _) = bmp.GetPixel(20, 50);
        b.Should().BeGreaterThan(200, "the left half of the inset box must be painted blue");

        // Right half (x=70) — white.
        bmp.GetPixel(70, 50).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "the right half (inside the 50px right inset) must stay white");
    }

    /// <summary>
    /// inset(0 round 20px): zero insets with 20px corner radii. The extreme
    /// corner pixel (2, 2) must be outside the rounded rectangle; the center (50, 50)
    /// must be painted.
    /// </summary>
    [TestMethod]
    public void Inset_with_round_clause_clips_corners()
    {
        var refBox = new LayoutRect(0, 0, 100, 100);
        var zero = CssLengthPercentage.FromLength(CssLength.Zero);
        var radius20 = CssLengthPercentage.FromLength(new CssLength(20, CssLengthUnit.Px));
        var radii = new List<CssRadiusPair>
        {
            new(radius20, radius20),
            new(radius20, radius20),
            new(radius20, radius20),
            new(radius20, radius20),
        };
        var shape = new CssInsetShape(zero, zero, zero, zero, radii);
        var clip = CssClipPath.FromShape(shape);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(refBox, Red, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 100, 100);

        // Center — must be red.
        bmp.GetPixel(50, 50).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "center of the rounded-inset shape must be painted red");

        // Extreme top-left corner — cut by 20px radius.
        var (r, g, b, _) = bmp.GetPixel(2, 2);
        (r == 255 && g == 255 && b == 255).Should().BeTrue(
            "extreme top-left corner must be clipped away by the 20px inset radius");
    }

    // ---- polygon() ----------------------------------------------------------

    /// <summary>
    /// polygon(50% 0%, 100% 100%, 0% 100%): an upward-pointing triangle.
    /// The top center (50, 5) must be inside; a pixel outside the triangle
    /// near the bottom-left corner region of the upper half (e.g. 10, 20)
    /// must be white (the triangle doesn't cover the upper-left area).
    /// The bottom center (50, 90) must be inside (wide base of the triangle).
    /// </summary>
    [TestMethod]
    public void Polygon_triangle_clips_outside_vertices()
    {
        var refBox = new LayoutRect(0, 0, 100, 100);
        var shape = new CssPolygonShape(
            FillRule: CssFillRule.Nonzero,
            Vertices:
            [
                new(CssLengthPercentage.FromPercentage(50), CssLengthPercentage.FromPercentage(0)),   // top center
                new(CssLengthPercentage.FromPercentage(100), CssLengthPercentage.FromPercentage(100)), // bottom right
                new(CssLengthPercentage.FromPercentage(0), CssLengthPercentage.FromPercentage(100)),   // bottom left
            ]);
        var clip = CssClipPath.FromShape(shape);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(refBox, Red, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 100, 100);

        // Bottom center (50, 90) — well inside the triangle base.
        bmp.GetPixel(50, 90).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "bottom center (50, 90) is inside the triangle and must be red");

        // Top center close to apex (50, 5) — inside the triangle near the apex.
        bmp.GetPixel(50, 5).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "apex vicinity (50, 5) is inside the triangle and must be red");

        // Upper-left corner (5, 5) — outside the triangle (above the left edge).
        bmp.GetPixel(5, 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "upper-left corner (5, 5) is outside the triangle and must stay white");

        // Upper-right corner (95, 5) — outside the triangle.
        bmp.GetPixel(95, 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "upper-right corner (95, 5) is outside the triangle and must stay white");
    }

    /// <summary>
    /// polygon with absolute pixel coordinates: a 60x60 square in the
    /// center of a 100x100 box (vertices at 20,20 / 80,20 / 80,80 / 20,80).
    /// Pixels inside the square must be painted; pixels in the margin must stay white.
    /// </summary>
    [TestMethod]
    public void Polygon_square_center_clips_margins()
    {
        var refBox = new LayoutRect(0, 0, 100, 100);
        var shape = new CssPolygonShape(
            FillRule: CssFillRule.Nonzero,
            Vertices:
            [
                new(CssLengthPercentage.FromLength(new CssLength(20, CssLengthUnit.Px)), CssLengthPercentage.FromLength(new CssLength(20, CssLengthUnit.Px))),
                new(CssLengthPercentage.FromLength(new CssLength(80, CssLengthUnit.Px)), CssLengthPercentage.FromLength(new CssLength(20, CssLengthUnit.Px))),
                new(CssLengthPercentage.FromLength(new CssLength(80, CssLengthUnit.Px)), CssLengthPercentage.FromLength(new CssLength(80, CssLengthUnit.Px))),
                new(CssLengthPercentage.FromLength(new CssLength(20, CssLengthUnit.Px)), CssLengthPercentage.FromLength(new CssLength(80, CssLengthUnit.Px))),
            ]);
        var clip = CssClipPath.FromShape(shape);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(refBox, Blue, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 100, 100);

        // Center of the polygon — blue.
        var (r, g, b, _) = bmp.GetPixel(50, 50);
        b.Should().BeGreaterThan(200, "center of the polygon square must be blue");

        // Top-left margin (5, 5) — white.
        bmp.GetPixel(5, 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixel (5, 5) is outside the polygon square and must stay white");

        // Bottom-right margin (95, 95) — white.
        bmp.GetPixel(95, 95).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixel (95, 95) is outside the polygon square and must stay white");
    }

    // ---- geometry-box -------------------------------------------------------

    /// <summary>
    /// Geometry-box only (no shape): a border-box keyword with no explicit shape
    /// clips to the box bounds. The whole box should be visible.
    /// </summary>
    [TestMethod]
    public void Geometry_box_only_clips_to_box_rect()
    {
        var refBox = new LayoutRect(10, 10, 80, 80);
        // border-box only, no shape
        var clip = CssClipPath.FromBox(CssGeometryBox.BorderBox);

        var list = new PaintList();
        list.Add(new PushClipPath(refBox, clip));
        list.Add(new FillRect(new LayoutRect(0, 0, 200, 200), Red, FillRectPixelAlignment.Preserve));
        list.Add(PopClipPath.Instance);

        using var bmp = RenderList(list, 200, 200);

        // Inside the reference box — red.
        bmp.GetPixel(50, 50).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "pixel inside the border-box geometry clip must be red");

        // Outside the reference box (at x=5, y=5 — before the box starts at 10,10) — white.
        bmp.GetPixel(5, 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "pixel outside the border-box geometry clip must stay white");
    }

    // ---- clip-path: none / url — no clip applied ----------------------------

    /// <summary>
    /// When clip-path is none, no clip is applied; the entire fill is visible.
    /// </summary>
    [TestMethod]
    public void ClipPath_none_does_not_clip_paint()
    {
        // Build a list with no PushClipPath — simulate clip-path:none.
        var refBox = new LayoutRect(0, 0, 100, 100);

        var list = new PaintList();
        // No PushClipPath — just a full fill.
        list.Add(new FillRect(refBox, Red, FillRectPixelAlignment.Preserve));

        using var bmp = RenderList(list, 100, 100);

        // All four corners must be red (no clipping).
        bmp.GetPixel(2, 2).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "without clip-path, the top-left corner must be painted red");
        bmp.GetPixel(97, 97).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "without clip-path, the bottom-right corner must be painted red");
    }

    // ---- HTML round-trip — end-to-end via CSS parsing -----------------------

    /// <summary>
    /// End-to-end: render an HTML element with clip-path:circle(40px at 50% 50%)
    /// via the full CSS→layout→paint pipeline and verify the corners are not painted.
    /// </summary>
    [TestMethod]
    public void EndToEnd_circle_clip_path_via_html()
    {
        const int boxSize = 100;
        var html =
            "<body style='margin:0'>" +
            $"<div style='width:{boxSize}px;height:{boxSize}px;background:#ff0000;" +
            "clip-path:circle(40px at 50% 50%)'></div>" +
            "</body>";

        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        var dl = new DisplayListBuilder().Build(root);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 300));

        // Center — red (inside the 40px circle).
        bmp.GetPixel(50, 50).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "center of the circle-clipped element must be red");

        // Extreme corner (2, 2) — white (outside the 40px circle).
        var (r, g, b, _) = bmp.GetPixel(2, 2);
        (r == 255 && g == 255 && b == 255).Should().BeTrue(
            "extreme top-left corner must be white (outside the 40px circle)");
    }

    /// <summary>
    /// End-to-end: render an HTML element with clip-path:inset(20px) and verify
    /// the 20px margins are transparent (white canvas) and the center is painted.
    /// </summary>
    [TestMethod]
    public void EndToEnd_inset_clip_path_via_html()
    {
        const int boxSize = 100;
        var html =
            "<body style='margin:0'>" +
            $"<div style='width:{boxSize}px;height:{boxSize}px;background:#0000ff;" +
            "clip-path:inset(20px)'></div>" +
            "</body>";

        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        var dl = new DisplayListBuilder().Build(root);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 300));

        // Center (50, 50) — blue.
        var (r, g, b, _) = bmp.GetPixel(50, 50);
        b.Should().BeGreaterThan(200, "center of the inset-clipped element must be blue");

        // Left margin (5, 50) — white.
        bmp.GetPixel(5, 50).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "the 20px left margin must be white");

        // Top margin (50, 5) — white.
        bmp.GetPixel(50, 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "the 20px top margin must be white");
    }

    /// <summary>
    /// End-to-end: polygon triangle via HTML clip-path.
    /// </summary>
    [TestMethod]
    public void EndToEnd_polygon_triangle_via_html()
    {
        const int boxSize = 100;
        var html =
            "<body style='margin:0'>" +
            $"<div style='width:{boxSize}px;height:{boxSize}px;background:#ff0000;" +
            "clip-path:polygon(50% 0%,100% 100%,0% 100%)'></div>" +
            "</body>";

        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 300));
        var dl = new DisplayListBuilder().Build(root);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 300));

        // Bottom center (50, 90) — inside the triangle.
        bmp.GetPixel(50, 90).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "bottom center of the triangle clip must be red");

        // Upper-left (5, 5) — outside the triangle.
        bmp.GetPixel(5, 5).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "upper-left corner outside the triangle must be white");
    }

    // ---- url() clip — deferred (clean no-op) --------------------------------

    /// <summary>
    /// clip-path:url(#foo) must paint unclipped (the SVG clipPath reference is
    /// deferred). Verify by checking that no PushClipPath is emitted.
    /// </summary>
    [TestMethod]
    public void ClipPath_url_is_deferred_and_paints_unclipped()
    {
        var dl = BuildList(
            "<body style='margin:0'>" +
            "<div style='width:100px;height:100px;background:red;clip-path:url(#myClip)'></div>" +
            "</body>");

        // url() references are deferred — no PushClipPath should be emitted.
        dl.Items.OfType<PushClipPath>().Should().BeEmpty(
            "clip-path:url() is deferred and must not emit a PushClipPath");
    }
}
