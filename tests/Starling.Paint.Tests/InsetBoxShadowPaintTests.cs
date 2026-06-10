using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Css.Values;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using Starling.Spec;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// CSS Backgrounds 3 §6/§7.1.1 — inset <c>box-shadow</c> layers. The shadow
/// fills the ring between the padding edge and the inner silhouette (the
/// padding box offset by the shadow offset and shrunk by spread), clipped to
/// the padding box. Pixel probes drive hand-built display lists through
/// <see cref="ImageSharpBackend"/>; builder tests check geometry + paint order.
/// </summary>
[TestClass]
public sealed class InsetBoxShadowPaintTests
{
    private static readonly CssColor Black = new(0, 0, 0, 255);
    private static readonly CssColor Red = new(255, 0, 0, 255);
    private static readonly CssColor White = new(255, 255, 255, 255);

    private static RenderedBitmap Render(PaintList list, int w, int h)
    {
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        return backend.Render(list, new LayoutSize(w, h));
    }

    private static bool IsDark((byte R, byte G, byte B, byte A) px)
        => px.R < 128 && px.G < 128 && px.B < 128;

    private static bool IsWhite((byte R, byte G, byte B, byte A) px)
        => px.R == 255 && px.G == 255 && px.B == 255;

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Inset_spread_paints_edge_band_on_all_sides_and_leaves_centre_clean()
    {
        // Padding box (40,40,120,80); spread 20, no offset, no blur → a sharp
        // 20px shadow band hugging every padding edge, centre untouched.
        var box = new LayoutRect(40, 40, 120, 80);
        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 0, OffsetY: 0, Blur: 0, Spread: 20, Black, Inset: true));

        using var bmp = Render(list, 220, 200);

        IsDark(bmp.GetPixel(46, 80)).Should().BeTrue("left band (depth 6 < spread 20) is shadowed");
        IsDark(bmp.GetPixel(154, 80)).Should().BeTrue("right band is shadowed");
        IsDark(bmp.GetPixel(100, 46)).Should().BeTrue("top band is shadowed");
        IsDark(bmp.GetPixel(100, 114)).Should().BeTrue("bottom band is shadowed");

        IsWhite(bmp.GetPixel(100, 80)).Should().BeTrue("the centre sits inside the inner silhouette and stays clean");

        // The shadow is clipped to the padding box — nothing paints outside it.
        IsWhite(bmp.GetPixel(36, 80)).Should().BeTrue("no inset shadow may escape the padding box (left)");
        IsWhite(bmp.GetPixel(100, 36)).Should().BeTrue("no inset shadow may escape the padding box (top)");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Positive_offset_x_thickens_the_left_band_only()
    {
        // ox = +15 translates the silhouette right: a 15px band appears on the
        // LEFT edge (verified against the spec offset direction and Chromium);
        // the right/top/bottom edges stay clean.
        var box = new LayoutRect(40, 40, 120, 80);
        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 15, OffsetY: 0, Blur: 0, Spread: 0, Black, Inset: true));

        using var bmp = Render(list, 220, 200);

        IsDark(bmp.GetPixel(47, 80)).Should().BeTrue("positive offset-x must thicken the LEFT band");
        IsWhite(bmp.GetPixel(152, 80)).Should().BeTrue("the right edge has no band for a positive offset-x");
        IsWhite(bmp.GetPixel(100, 44)).Should().BeTrue("the top edge has no band for a pure-x offset");
        IsWhite(bmp.GetPixel(100, 116)).Should().BeTrue("the bottom edge has no band for a pure-x offset");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Negative_offset_x_thickens_the_right_band_only()
    {
        var box = new LayoutRect(40, 40, 120, 80);
        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: -15, OffsetY: 0, Blur: 0, Spread: 0, Black, Inset: true));

        using var bmp = Render(list, 220, 200);

        IsDark(bmp.GetPixel(153, 80)).Should().BeTrue("negative offset-x must thicken the RIGHT band");
        IsWhite(bmp.GetPixel(48, 80)).Should().BeTrue("the left edge has no band for a negative offset-x");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Positive_offset_y_thickens_the_top_band()
    {
        var box = new LayoutRect(40, 40, 120, 80);
        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 0, OffsetY: 15, Blur: 0, Spread: 0, Black, Inset: true));

        using var bmp = Render(list, 220, 200);

        IsDark(bmp.GetPixel(100, 47)).Should().BeTrue("positive offset-y must thicken the TOP band");
        IsWhite(bmp.GetPixel(100, 113)).Should().BeTrue("the bottom edge has no band for a positive offset-y");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Larger_spread_widens_the_band()
    {
        var box = new LayoutRect(40, 40, 120, 80);

        var thin = new PaintList();
        thin.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 0, OffsetY: 0, Blur: 0, Spread: 4, Black, Inset: true));
        using var thinBmp = Render(thin, 220, 200);

        var wide = new PaintList();
        wide.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 0, OffsetY: 0, Blur: 0, Spread: 16, Black, Inset: true));
        using var wideBmp = Render(wide, 220, 200);

        // Probe at depth 10 from the left padding edge: outside a 4px band,
        // inside a 16px band.
        IsWhite(thinBmp.GetPixel(50, 80)).Should().BeTrue("depth 10 is past a 4px spread band");
        IsDark(wideBmp.GetPixel(50, 80)).Should().BeTrue("depth 10 is inside a 16px spread band");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Zero_blur_inset_shadow_has_a_sharp_edge()
    {
        // spread 12, blur 0 — the band/silhouette boundary at depth 12 must be
        // a hard edge: solid shadow at depth 10, pure white at depth 14.
        var box = new LayoutRect(40, 40, 120, 80);
        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 0, OffsetY: 0, Blur: 0, Spread: 12, Black, Inset: true));

        using var bmp = Render(list, 220, 200);

        bmp.GetPixel(50, 80).Should().Be(((byte)0, (byte)0, (byte)0, (byte)255),
            "inside the zero-blur band the shadow is solid");
        bmp.GetPixel(54, 80).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255),
            "two px past the silhouette edge the canvas is untouched — no feathering without blur");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Inset_and_outer_shadows_coexist()
    {
        // Outer shadow offset down-right + a white "background" fill + an inset
        // red ring. Outside the box darkens (outer), the inner edge band is red
        // (inset), and the centre stays the background white.
        var box = new LayoutRect(60, 60, 100, 100);
        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 12, OffsetY: 12, Blur: 0, Spread: 0, Black, Inset: false));
        list.Add(new FillRect(box, White, FillRectPixelAlignment.Preserve));
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 0, OffsetY: 0, Blur: 0, Spread: 10, Red, Inset: true));

        using var bmp = Render(list, 240, 240);

        IsDark(bmp.GetPixel(166, 110)).Should().BeTrue("the outer shadow darkens past the right edge");
        var band = bmp.GetPixel(65, 110);
        (band.Item1 > 200 && band.Item2 < 80 && band.Item3 < 80).Should().BeTrue(
            "the inset ring paints red above the background");
        IsWhite(bmp.GetPixel(110, 110)).Should().BeTrue("the centre keeps the background fill");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Blur_feathers_the_band_across_the_silhouette_edge()
    {
        // spread 12, blur 12 — pixels just inside the silhouette pick up
        // blurred shadow (no longer pure white), while the deep centre stays
        // clean.
        var box = new LayoutRect(40, 40, 140, 100);
        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 0, OffsetY: 0, Blur: 12, Spread: 12, Black, Inset: true));

        using var bmp = Render(list, 240, 200);

        IsWhite(bmp.GetPixel(56, 90)).Should().BeFalse("blur bleeds shadow past the sharp silhouette edge (depth 16)");
        IsWhite(bmp.GetPixel(110, 90)).Should().BeTrue("the deep centre is beyond the blur reach");
    }

    // ---- builder-level: geometry + paint order ------------------------------

    private static PaintList BuildList(string html, LayoutSize viewport)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, viewport);
        return new DisplayListBuilder().Build(root);
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Builder_emits_inset_item_with_padding_box_bounds_between_background_and_border()
    {
        var list = BuildList("""
            <body style="margin:0">
              <div style="width:100px; height:60px; background-color:#0000ff;
                          border:4px solid #000000;
                          box-shadow: inset 0 0 0 10px #ff0000"></div>
            </body>
            """, new LayoutSize(400, 300));

        var shadows = list.Items.OfType<DrawBoxShadow>().ToList();
        shadows.Should().ContainSingle("one inset layer must emit exactly one shadow item");
        var inset = shadows[0];
        inset.Inset.Should().BeTrue();

        // Bounds = the padding box: border box (0,0,108,68) inset by the 4px border.
        inset.Bounds.X.Should().BeApproximately(4, 0.5);
        inset.Bounds.Y.Should().BeApproximately(4, 0.5);
        inset.Bounds.Width.Should().BeApproximately(100, 0.5);
        inset.Bounds.Height.Should().BeApproximately(60, 0.5);
        inset.Spread.Should().Be(10);

        // Paint order: background fill → inset shadow → border strokes.
        var items = list.Items;
        var bgIndex = -1;
        var shadowIndex = -1;
        var borderIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case FillRect { Color: { R: 0, G: 0, B: 255 } } when bgIndex < 0:
                    bgIndex = i;
                    break;
                case DrawBoxShadow { Inset: true } when shadowIndex < 0:
                    shadowIndex = i;
                    break;
                case FillRect { Color: { R: 0, G: 0, B: 0, A: 255 } } when borderIndex < 0:
                    borderIndex = i;
                    break;
            }
        }

        bgIndex.Should().BeGreaterThanOrEqualTo(0, "the background fill must be present");
        shadowIndex.Should().BeGreaterThan(bgIndex, "the inset shadow paints above the background");
        borderIndex.Should().BeGreaterThan(shadowIndex, "the border stroke paints above the inset shadow");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Builder_emits_outer_before_background_and_inset_after_it()
    {
        var list = BuildList("""
            <body style="margin:0">
              <div style="width:100px; height:60px; background-color:#00ff00;
                          box-shadow: 6px 6px 4px #000000, inset 0 0 0 8px #ff0000"></div>
            </body>
            """, new LayoutSize(400, 300));

        var items = list.Items;
        var outerIndex = -1;
        var bgIndex = -1;
        var insetIndex = -1;
        for (var i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case DrawBoxShadow { Inset: false } when outerIndex < 0:
                    outerIndex = i;
                    break;
                case FillRect { Color: { R: 0, G: 255, B: 0 } } when bgIndex < 0:
                    bgIndex = i;
                    break;
                case DrawBoxShadow { Inset: true } when insetIndex < 0:
                    insetIndex = i;
                    break;
            }
        }

        outerIndex.Should().BeGreaterThanOrEqualTo(0, "the outer layer must be emitted");
        bgIndex.Should().BeGreaterThan(outerIndex, "outer shadows paint behind the background");
        insetIndex.Should().BeGreaterThan(bgIndex, "inset shadows paint above the background");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Builder_emits_multi_layer_inset_shadows_back_to_front()
    {
        // First listed layer paints on top, so it must be emitted LAST.
        var list = BuildList("""
            <body style="margin:0">
              <div style="width:100px; height:60px;
                          box-shadow: inset 0 0 0 4px #ff0000, inset 0 0 0 8px #0000ff"></div>
            </body>
            """, new LayoutSize(400, 300));

        var shadows = list.Items.OfType<DrawBoxShadow>().Where(s => s.Inset).ToList();
        shadows.Should().HaveCount(2);
        shadows[0].Color.B.Should().Be(255, "the last listed layer is emitted first (bottom)");
        shadows[1].Color.R.Should().Be(255, "the first listed layer is emitted last (top)");
    }
}
