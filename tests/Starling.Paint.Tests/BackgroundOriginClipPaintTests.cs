using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Layout.Tree;
using Starling.Paint.DisplayList;
using Starling.Spec;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// CSS Backgrounds 3 §2.6 (<c>background-origin</c>) and §2.4
/// (<c>background-clip</c>) — the per-layer positioning area and painting
/// area. Origin moves where position/size resolve; clip crops the paint to
/// the border/padding/content box with corrected inner corner radii, and
/// <c>background-color</c> is clipped by the LAST layer's clip box.
/// </summary>
[TestClass]
public sealed class BackgroundOriginClipPaintTests
{
    private sealed class StubResolver : IImageResolver
    {
        public string? Url { get; init; }
        public DecodedImage? Image { get; init; }

        public bool TryResolve(Starling.Dom.Element element, out ResolvedImage image)
        {
            image = default;
            return false;
        }

        public bool TryResolveUrl(string url, out DecodedImage image)
        {
            if (url == Url && Image is not null)
            {
                image = Image;
                return true;
            }
            image = null!;
            return false;
        }
    }

    private static DecodedImage MakeImage(int width = 20, int height = 20)
        => DecodedImage.CreatePooled(width, height, span =>
        {
            for (var i = 0; i < span.Length; i += 4)
            {
                span[i] = 255;
                span[i + 1] = 0;
                span[i + 2] = 0;
                span[i + 3] = 255;
            }
        });

    private static PaintList Build(string html, LayoutSize viewport, IImageResolver? resolver = null)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = resolver is null
            ? new LayoutEngine(style, DefaultTextMeasurer.Instance)
            : new LayoutEngine(style, DefaultTextMeasurer.Instance, resolver);
        var root = engine.LayoutDocument(document, viewport);
        return new DisplayListBuilder().Build(root, styleOverride: null, images: resolver);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-origin", section: "2.6")]
    public void Origin_content_box_shifts_positioned_image_by_the_padding()
    {
        using var image = MakeImage();
        var resolver = new StubResolver { Url = "dot.png", Image = image };

        // Control: default origin (padding-box; no border → the box corner).
        var control = Build("""
            <body style="margin:0">
              <div style="width:60px; height:60px; padding:10px;
                          background-image:url(dot.png);
                          background-position:0 0; background-repeat:no-repeat"></div>
            </body>
            """, new LayoutSize(400, 300), resolver);
        var controlDraw = control.Items.OfType<DrawImage>().Single();
        controlDraw.Bounds.X.Should().BeApproximately(0, 0.5);
        controlDraw.Bounds.Y.Should().BeApproximately(0, 0.5);

        // origin: content-box → position 0 0 resolves from the content corner,
        // i.e. shifted by the 10px padding.
        var shifted = Build("""
            <body style="margin:0">
              <div style="width:60px; height:60px; padding:10px;
                          background-image:url(dot.png); background-origin:content-box;
                          background-position:0 0; background-repeat:no-repeat"></div>
            </body>
            """, new LayoutSize(400, 300), resolver);
        var shiftedDraw = shifted.Items.OfType<DrawImage>().Single();
        shiftedDraw.Bounds.X.Should().BeApproximately(10, 0.5, "content-box origin starts after the left padding");
        shiftedDraw.Bounds.Y.Should().BeApproximately(10, 0.5, "content-box origin starts after the top padding");
        shiftedDraw.Bounds.Width.Should().BeApproximately(20, 0.5, "origin must not rescale the image");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-origin", section: "2.6")]
    public void Origin_border_box_resolves_percent_position_against_the_border_box()
    {
        using var image = MakeImage();
        var resolver = new StubResolver { Url = "dot.png", Image = image };

        // border 10px; origin border-box; position 100% 100% → the image's
        // bottom-right corner lands on the border box's bottom-right
        // (120,70), so its top-left is (100,50). The default border-box clip
        // keeps the whole image visible.
        var list = Build("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:10px solid transparent;
                          background-image:url(dot.png); background-origin:border-box;
                          background-position:100% 100%; background-repeat:no-repeat"></div>
            </body>
            """, new LayoutSize(400, 300), resolver);
        var draw = list.Items.OfType<DrawImage>().Single();
        draw.Bounds.X.Should().BeApproximately(100, 0.5);
        draw.Bounds.Y.Should().BeApproximately(50, 0.5);
        draw.Bounds.Width.Should().BeApproximately(20, 0.5);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "2.4")]
    public void Clip_padding_box_shrinks_the_color_fill_to_the_padding_rect()
    {
        // Border box (0,0,120,70); 10px border → padding box (10,10,100,50).
        var list = Build("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:10px solid transparent;
                          background-color:#ff0000; background-clip:padding-box"></div>
            </body>
            """, new LayoutSize(400, 300));

        var fill = list.Items.OfType<FillRect>().Single(f => f.Color is { R: 255, G: 0, B: 0 });
        fill.Bounds.X.Should().BeApproximately(10, 0.5);
        fill.Bounds.Y.Should().BeApproximately(10, 0.5);
        fill.Bounds.Width.Should().BeApproximately(100, 0.5);
        fill.Bounds.Height.Should().BeApproximately(50, 0.5);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "2.4")]
    public void Clip_padding_box_leaves_the_border_area_transparent_in_pixels()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:10px solid transparent;
                          background-color:#ff0000; background-clip:padding-box"></div>
            </body>
            """);
        using var bmp = painter.RenderDocument(document, new LayoutSize(300, 200));

        bmp.GetPixel(4, 4).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255),
            "the transparent border ring shows the page background when the color clips to the padding box");
        bmp.GetPixel(20, 20).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255),
            "inside the padding box the color paints normally");

        // Control: default clip (border-box) paints under the transparent border.
        var controlDoc = HtmlParser.Parse("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:10px solid transparent;
                          background-color:#ff0000"></div>
            </body>
            """);
        using var controlBmp = painter.RenderDocument(controlDoc, new LayoutSize(300, 200));
        controlBmp.GetPixel(4, 4).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255),
            "with the default border-box clip the color paints under the transparent border");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "2.4")]
    public void Clip_content_box_shrinks_the_color_fill_past_border_and_padding()
    {
        // 10px border + 5px padding → content box (15,15,100,50).
        var list = Build("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:10px solid transparent; padding:5px;
                          background-color:#ff0000; background-clip:content-box"></div>
            </body>
            """, new LayoutSize(400, 300));

        var fill = list.Items.OfType<FillRect>().Single(f => f.Color is { R: 255, G: 0, B: 0 });
        fill.Bounds.X.Should().BeApproximately(15, 0.5);
        fill.Bounds.Y.Should().BeApproximately(15, 0.5);
        fill.Bounds.Width.Should().BeApproximately(100, 0.5);
        fill.Bounds.Height.Should().BeApproximately(50, 0.5);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#corner-clipping", section: "5.3")]
    public void Clip_padding_box_corrects_the_inner_corner_radii()
    {
        // border-radius 20, border 12 → inner radius 20 − 12 = 8 on every
        // component, and the fill rect is the padding box.
        var list = Build("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:12px solid #000000; border-radius:20px;
                          background-color:#ff0000; background-clip:padding-box"></div>
            </body>
            """, new LayoutSize(400, 300));

        var fill = list.Items.OfType<FillRoundedRect>().Single(f => f.Color is { R: 255, G: 0, B: 0 });
        fill.Bounds.X.Should().BeApproximately(12, 0.5);
        fill.Bounds.Y.Should().BeApproximately(12, 0.5);
        fill.Radii.TopLeftX.Should().BeApproximately(8, 0.5);
        fill.Radii.TopLeftY.Should().BeApproximately(8, 0.5);
        fill.Radii.BottomRightX.Should().BeApproximately(8, 0.5);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "2.4")]
    public void Background_color_is_clipped_by_the_last_layers_clip()
    {
        // Two gradient layers via the `background` shorthand: the FIRST is
        // border-box, the LAST padding-box. The color must clip by the LAST
        // layer's box (padding), not the first.
        var list = Build("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:10px solid transparent;
                          background: linear-gradient(180deg, #ff0000, #ff0000) border-box,
                                      linear-gradient(180deg, #0000ff, #0000ff) padding-box;
                          background-color:#00ff00"></div>
            </body>
            """, new LayoutSize(400, 300));

        var colorFill = list.Items.OfType<FillRect>().Single(f => f.Color is { R: 0, G: 255, B: 0 });
        colorFill.Bounds.X.Should().BeApproximately(10, 0.5, "the LAST layer's clip is padding-box");
        colorFill.Bounds.Width.Should().BeApproximately(100, 0.5);

        // The gradient layers keep their own per-layer clip geometry, painted
        // back-to-front (last layer first).
        var gradients = list.Items.OfType<FillGradient>().ToList();
        gradients.Should().HaveCount(2);
        gradients[0].Bounds.Width.Should().BeApproximately(100, 0.5, "the bottom (last) layer clips to the padding box");
        gradients[1].Bounds.Width.Should().BeApproximately(120, 0.5, "the top (first) layer clips to the border box");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "2.4")]
    public void Gradient_layer_clips_to_padding_box()
    {
        var list = Build("""
            <body style="margin:0">
              <div style="width:100px; height:50px; border:10px solid transparent;
                          background-image: linear-gradient(180deg, #ff0000, #0000ff);
                          background-clip: padding-box"></div>
            </body>
            """, new LayoutSize(400, 300));

        var gradient = list.Items.OfType<FillGradient>().Single();
        gradient.Bounds.X.Should().BeApproximately(10, 0.5);
        gradient.Bounds.Y.Should().BeApproximately(10, 0.5);
        gradient.Bounds.Width.Should().BeApproximately(100, 0.5);
        gradient.Bounds.Height.Should().BeApproximately(50, 0.5);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#background-clip", section: "3.8")]
    public void Background_clip_text_still_emits_the_glyph_clipped_fill()
    {
        // Guard: the box-keyword path must not swallow the `text` keyword.
        var list = Build("""
            <body style="margin:0">
              <h1 style="font-size:40px; background-color:#ff00ff;
                         background-clip:text; color:transparent">KEEP</h1>
            </body>
            """, new LayoutSize(800, 200));

        list.Items.OfType<FillBackgroundTextClip>().Should().ContainSingle(
            "background-clip:text keeps its dedicated glyph-clip path");
    }
}
