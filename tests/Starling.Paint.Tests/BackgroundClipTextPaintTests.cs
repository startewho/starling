// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Paint.DisplayList;
using Starling.Spec;
using LayoutSize = Starling.Layout.Size;

namespace Starling.Paint.Tests;

/// <summary>
/// CSS Backgrounds 3 §3.8 — pixel and display-list coverage for
/// <c>background-clip: text</c>. The background (gradient or solid color) must
/// be clipped to the element's glyph shapes: the glyphs show the background,
/// the rest of the box stays transparent. This is the angular.dev gradient-text
/// regression (it used to paint a solid bar over the whole box).
/// </summary>
[TestClass]
public sealed class BackgroundClipTextPaintTests
{
    private static DisplayList.DisplayList BuildList(string html, LayoutSize viewport)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, viewport);
        return new DisplayListBuilder().Build(root);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "3.8")]
    public void Text_clip_gradient_emits_clip_item_not_full_box_gradient()
    {
        var dl = BuildList("""
            <body style="margin:0">
              <h1 style="font-size:40px; background-image: linear-gradient(90deg, red, blue);
                         background-clip: text; color: transparent">HELLO</h1>
            </body>
            """, new LayoutSize(800, 200));

        // The glyph-clipped fill replaces the normal full-box gradient.
        var clips = dl.Items.OfType<FillBackgroundTextClip>().ToList();
        clips.Should().ContainSingle("background-clip: text must emit a glyph-clipped fill");
        clips[0].Gradient.Should().NotBeNull("the background is a gradient");
        clips[0].Glyphs.Should().NotBeEmpty("the clip must carry the element's glyph runs");

        dl.Items.OfType<FillGradient>()
            .Should().BeEmpty("the gradient must not also fill the whole box (the bar bug)");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "3.8")]
    public void Solid_color_text_clip_emits_clip_item()
    {
        var dl = BuildList("""
            <body style="margin:0">
              <h1 style="font-size:40px; background-color: #ff00ff;
                         background-clip: text; color: transparent">PINK</h1>
            </body>
            """, new LayoutSize(800, 200));

        var clips = dl.Items.OfType<FillBackgroundTextClip>().ToList();
        clips.Should().ContainSingle("a solid color background-clip: text must emit a glyph-clipped fill");
        clips[0].Gradient.Should().BeNull();
        clips[0].Color.R.Should().Be(255);
        clips[0].Color.B.Should().Be(255);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "3.8")]
    public void Gradient_clipped_text_paints_glyphs_and_leaves_corners_transparent()
    {
        // End-to-end through the real ImageSharp painter: the gradient must show
        // up where the glyphs are, and the box corners must stay white (the
        // page background), proving the gradient did not fill the whole box.
        var painter = new Painter();
        var document = HtmlParser.Parse("""
            <body style="margin:0; background:#ffffff">
              <h1 style="font-size:80px; font-weight:700; margin:0;
                         background-image: linear-gradient(90deg, #ff0000, #0000ff);
                         background-clip: text; color: transparent">WWWWW</h1>
            </body>
            """);
        using var image = painter.RenderDocument(document, new LayoutSize(600, 200));

        // Glyphs cover the upper-left band; require coloured (non-white) pixels.
        BitmapPixels.CountNonWhite(image)
            .Should().BeGreaterThan(200, "the gradient must paint inside the glyph shapes");

        // Red pixels prove the gradient (left = red) shows through the glyphs,
        // not a uniform bar or solid text colour.
        BitmapPixels.Count(image, (r, g, b, a) => a > 200 && r > 150 && g < 100 && b < 100)
            .Should().BeGreaterThan(20, "the left of the red→blue gradient must show inside the glyphs");

        // The bottom-right corner sits well below the single line of text, so it
        // must remain the white page background — the gradient did not fill the
        // box.
        var (cr, cg, cb, _) = image.GetPixel(image.Width - 3, image.Height - 3);
        ((int)cr).Should().BeGreaterThan(245);
        ((int)cg).Should().BeGreaterThan(245);
        ((int)cb).Should().BeGreaterThan(245);
    }
}
