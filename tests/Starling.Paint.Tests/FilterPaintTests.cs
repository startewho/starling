// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using Starling.Paint.Compositor;
using Starling.Spec;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using StyleEngine = Starling.Css.Cascade.StyleEngine;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Filter Effects 1 §10 — `filter` / `backdrop-filter` painting (Tier 4 item
/// 18). Pixel probes drive hand-built display lists through
/// <see cref="ImageSharpBackend"/>; builder tests check that the CSS property
/// resolves to the typed chain and the right bracket / item shape.
/// </summary>
[TestClass]
public sealed class FilterPaintTests
{
    private static readonly CssColor Red = new(255, 0, 0);

    private static RenderedBitmap Render(PaintList list, int w, int h)
    {
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        return backend.Render(list, new LayoutSize(w, h));
    }

    private static PaintList FilteredRect(LayoutRect rect, CssColor color, params FilterFunction[] filters)
    {
        var list = new PaintList();
        list.Add(new PushFilter(rect, filters));
        list.Add(new FillRect(rect, color, FillRectPixelAlignment.Preserve));
        list.Add(PopFilter.Instance);
        return list;
    }

    // ---- §10.1 blur() -------------------------------------------------------

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-blur", section: "10.1")]
    public void Blur_spreads_a_hard_edge_in_both_directions()
    {
        // Red box (60,60)-(140,140), blur(8px) → σ = 4 (the repo's radius/2
        // convention). The former hard edge at x=140 must now ramp: red bleeds
        // out past it, white bleeds in before it.
        var list = FilteredRect(new LayoutRect(60, 60, 80, 80), Red,
            new FilterFunction(FilterFunctionKind.Blur, 8));

        using var bmp = Render(list, 200, 200);

        var centre = bmp.GetPixel(100, 100);
        centre.R.Should().BeGreaterThan(200, "the box centre stays red");
        centre.G.Should().BeLessThan(60);

        var inside = bmp.GetPixel(136, 100); // 1σ inside the former edge
        inside.G.Should().BeGreaterThan(12, "white must bleed inward across the former edge");

        var outside = bmp.GetPixel(144, 100); // 1σ outside the former edge
        outside.R.Should().BeGreaterThan(240);
        outside.G.Should().BeLessThan(240, "red must bleed outward past the former edge");

        var far = bmp.GetPixel(178, 100); // ≈ 9.5σ out — untouched canvas
        far.G.Should().BeGreaterThan(250);
        far.R.Should().BeGreaterThan(250);
    }

    // ---- §10.1 brightness() -------------------------------------------------

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-brightness", section: "10.1")]
    public void Brightness_half_halves_a_known_rgb()
    {
        var list = FilteredRect(new LayoutRect(40, 40, 80, 80), new CssColor(200, 100, 50),
            new FilterFunction(FilterFunctionKind.Brightness, 0.5));

        using var bmp = Render(list, 160, 160);

        var px = bmp.GetPixel(80, 80);
        ((int)px.R).Should().BeCloseTo(100, 10);
        ((int)px.G).Should().BeCloseTo(50, 10);
        ((int)px.B).Should().BeCloseTo(25, 10);
    }

    // ---- §10.1 grayscale() --------------------------------------------------

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-grayscale", section: "10.1")]
    public void Grayscale_full_equalizes_the_channels()
    {
        var list = FilteredRect(new LayoutRect(40, 40, 80, 80), Red,
            new FilterFunction(FilterFunctionKind.Grayscale, 1));

        using var bmp = Render(list, 160, 160);

        var px = bmp.GetPixel(80, 80);
        // Pure red → its Rec.709 luminance (≈ 54) on every channel.
        Math.Abs(px.R - px.G).Should().BeLessThan(10, "grayscale(1) equalizes R and G");
        Math.Abs(px.G - px.B).Should().BeLessThan(10, "grayscale(1) equalizes G and B");
        ((int)px.R).Should().BeInRange(30, 90, "red's luminance is ≈ 54");
    }

    // ---- §10.1 hue-rotate() -------------------------------------------------

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-hue-rotate", section: "10.1")]
    public void Hue_rotate_180_flips_red_toward_cyan()
    {
        var list = FilteredRect(new LayoutRect(40, 40, 80, 80), Red,
            new FilterFunction(FilterFunctionKind.HueRotate, 180));

        using var bmp = Render(list, 160, 160);

        // The standard hue-rotation matrix at 180° maps pure red to ≈ (0, 109, 109).
        var px = bmp.GetPixel(80, 80);
        px.R.Should().BeLessThan(50, "red collapses under a 180° hue rotation");
        ((int)px.G).Should().BeInRange(80, 140);
        ((int)px.B).Should().BeInRange(80, 140);
    }

    // ---- §10.1 drop-shadow() ------------------------------------------------

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-drop-shadow", section: "10.1")]
    public void Drop_shadow_offsets_the_silhouette_under_the_source()
    {
        // Red box (60,60)-(100,100); drop-shadow(20px 20px 0 black) paints a
        // sharp black silhouette at (80,80)-(120,120) UNDER the source.
        var list = FilteredRect(new LayoutRect(60, 60, 40, 40), Red,
            new FilterFunction(FilterFunctionKind.DropShadow, 0, 20, 20, CssColor.Black));

        using var bmp = Render(list, 200, 200);

        var source = bmp.GetPixel(70, 70);
        source.R.Should().BeGreaterThan(200, "the source paints over its own shadow");
        source.G.Should().BeLessThan(60);

        var overlap = bmp.GetPixel(90, 90); // source ∩ shadow → source wins
        overlap.R.Should().BeGreaterThan(200);

        var shadow = bmp.GetPixel(110, 110); // shadow-only region
        shadow.R.Should().BeLessThan(60, "the offset silhouette is black");
        shadow.G.Should().BeLessThan(60);
        shadow.B.Should().BeLessThan(60);

        var clear = bmp.GetPixel(130, 130); // past the shadow
        clear.R.Should().BeGreaterThan(240);
        clear.G.Should().BeGreaterThan(240);
    }

    // ---- §10.1 the list applies in order ------------------------------------

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "10.1")]
    public void Filter_list_applies_in_declaration_order()
    {
        var grey = new CssColor(200, 200, 200);
        var box = new LayoutRect(40, 40, 80, 80);

        // brightness(0.5) then invert(1): 200·0.5 = 100 → 255−100 = 155.
        var a = FilteredRect(box, grey,
            new FilterFunction(FilterFunctionKind.Brightness, 0.5),
            new FilterFunction(FilterFunctionKind.Invert, 1));

        // invert(1) then brightness(0.5): 255−200 = 55 → 55·0.5 ≈ 28.
        var b = FilteredRect(box, grey,
            new FilterFunction(FilterFunctionKind.Invert, 1),
            new FilterFunction(FilterFunctionKind.Brightness, 0.5));

        using var bmpA = Render(a, 160, 160);
        using var bmpB = Render(b, 160, 160);

        ((int)bmpA.GetPixel(80, 80).R).Should().BeCloseTo(155, 12, "brightness→invert");
        ((int)bmpB.GetPixel(80, 80).R).Should().BeCloseTo(28, 12, "invert→brightness");
    }

    // ---- Filter Effects 2 §6 backdrop-filter --------------------------------

    [TestMethod]
    [Spec("css-filter-effects-2", "https://drafts.fxtf.org/filter-effects-2/#BackdropFilterProperty", section: "6")]
    public void Backdrop_filter_blurs_only_the_region_under_the_element_and_own_content_stays_sharp()
    {
        var list = new PaintList();
        // Backdrop: a black stripe crossing under the element.
        list.Add(new FillRect(new LayoutRect(40, 90, 120, 20), CssColor.Black, FillRectPixelAlignment.Preserve));
        // The element (80,60)-(160,140) blurs its backdrop…
        list.Add(new DrawBackdropFilter(
            new LayoutRect(80, 60, 80, 80),
            CornerRadii.None,
            new[] { new FilterFunction(FilterFunctionKind.Blur, 8) }));
        // …then paints its own (sharp) content over the filtered patch.
        list.Add(new FillRect(new LayoutRect(100, 80, 10, 10), new CssColor(0, 200, 0), FillRectPixelAlignment.Preserve));

        using var bmp = Render(list, 200, 200);

        // Outside the element the stripe edge stays hard.
        var sharpAbove = bmp.GetPixel(60, 86); // 4px above the stripe
        sharpAbove.R.Should().BeGreaterThan(240, "outside the element the canvas is untouched");
        var sharpInside = bmp.GetPixel(60, 95);
        sharpInside.R.Should().BeLessThan(30, "outside the element the stripe stays black");

        // Under the element the same edge is blurred: black bleeds upward.
        var blurredAbove = bmp.GetPixel(120, 86);
        blurredAbove.R.Should().BeLessThan(230, "the stripe edge under the element must blur upward");

        // The element's own content paints after the patch and stays sharp green.
        var green = bmp.GetPixel(104, 84);
        green.G.Should().BeGreaterThan(150);
        green.R.Should().BeLessThan(120);
        var besideGreen = bmp.GetPixel(115, 84); // 5px right of the green box
        // The blurred backdrop there is neutral (whitish-gray): a green smear
        // would push G far above R. Sharp content keeps the channels balanced.
        (besideGreen.G - besideGreen.R).Should().BeLessThan(40,
            "the element's own content must not smear into the backdrop");
    }

    [TestMethod]
    [Spec("css-filter-effects-2", "https://drafts.fxtf.org/filter-effects-2/#BackdropFilterProperty", section: "6")]
    public void Backdrop_filter_clips_the_patch_to_the_rounded_border_box()
    {
        var list = new PaintList();
        list.Add(new FillRect(new LayoutRect(0, 0, 200, 200), Red, FillRectPixelAlignment.Preserve));
        list.Add(new DrawBackdropFilter(
            new LayoutRect(60, 60, 80, 80),
            CornerRadii.Uniform(40, 40, 40, 40),
            new[] { new FilterFunction(FilterFunctionKind.Grayscale, 1) }));

        using var bmp = Render(list, 200, 200);

        var centre = bmp.GetPixel(100, 100);
        Math.Abs(centre.R - centre.G).Should().BeLessThan(20, "under the element the backdrop is grayscaled");

        var corner = bmp.GetPixel(64, 64); // inside the AABB, outside the rounded shape
        corner.R.Should().BeGreaterThan(200, "the rounded corner is outside the clip — red survives");
        corner.G.Should().BeLessThan(60);

        var outside = bmp.GetPixel(30, 100); // left of the element entirely
        outside.R.Should().BeGreaterThan(200);
        outside.G.Should().BeLessThan(60);
    }

    // ---- Builder: CSS property → display-list shape --------------------------

    private static PaintList Build(string html)
    {
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(400, 400));
        return new DisplayListBuilder().Build(root);
    }

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "10.1")]
    public void Builder_wraps_a_filtered_element_in_a_balanced_bracket()
    {
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px;filter:blur(4px)\">x</div></body>");

        var pushes = dl.Items.OfType<PushFilter>().ToList();
        pushes.Should().HaveCount(1);
        dl.Items.OfType<PopFilter>().Should().HaveCount(1);

        var push = pushes[0];
        push.Filters.Should().HaveCount(1);
        push.Filters[0].Kind.Should().Be(FilterFunctionKind.Blur);
        push.Filters[0].Amount.Should().BeApproximately(4, 0.001);

        // The bracket must enclose the element's own background fill.
        var items = dl.Items.ToList();
        var pushIdx = items.FindIndex(i => i is PushFilter);
        var popIdx = items.FindIndex(i => i is PopFilter);
        var fillIdx = items.FindIndex(i => i is FillRect { Color: { R: 255, G: 0, B: 0 } });
        fillIdx.Should().BeGreaterThan(pushIdx);
        fillIdx.Should().BeLessThan(popIdx);
    }

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#typedef-filter-value-list", section: "10.1")]
    public void Builder_keeps_chained_functions_in_order()
    {
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px;filter:brightness(0.5) invert(1)\">x</div></body>");

        var push = dl.Items.OfType<PushFilter>().Single();
        push.Filters.Should().HaveCount(2);
        push.Filters[0].Kind.Should().Be(FilterFunctionKind.Brightness);
        push.Filters[0].Amount.Should().BeApproximately(0.5, 0.001);
        push.Filters[1].Kind.Should().Be(FilterFunctionKind.Invert);
        push.Filters[1].Amount.Should().BeApproximately(1, 0.001);
    }

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-drop-shadow", section: "10.1")]
    public void Builder_resolves_drop_shadow_offsets_blur_and_color()
    {
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px;filter:drop-shadow(2px 3px 4px black)\">x</div></body>");

        var push = dl.Items.OfType<PushFilter>().Single();
        var f = push.Filters.Single();
        f.Kind.Should().Be(FilterFunctionKind.DropShadow);
        f.OffsetX.Should().BeApproximately(2, 0.001);
        f.OffsetY.Should().BeApproximately(3, 0.001);
        f.Amount.Should().BeApproximately(4, 0.001);
        f.Color.Should().Be(CssColor.Black);
    }

    [TestMethod]
    public void Builder_emits_no_bracket_without_a_filter()
    {
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px\">x</div></body>");
        dl.Items.OfType<PushFilter>().Should().BeEmpty();
        dl.Items.OfType<PopFilter>().Should().BeEmpty();
        dl.Items.OfType<DrawBackdropFilter>().Should().BeEmpty();
    }

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "10.1")]
    public void Builder_drops_the_whole_list_when_one_function_is_unknown()
    {
        // §10.1 — one invalid function invalidates the whole chain.
        var dl = Build("<body><div style=\"background-color:#ff0000;width:100px;height:100px;filter:blur(4px) bogus(3)\">x</div></body>");
        dl.Items.OfType<PushFilter>().Should().BeEmpty();
    }

    [TestMethod]
    [Spec("css-filter-effects-2", "https://drafts.fxtf.org/filter-effects-2/#BackdropFilterProperty", section: "6")]
    public void Builder_emits_backdrop_filter_before_the_elements_own_background()
    {
        var dl = Build("<body><div style=\"background-color:#00ff00;width:100px;height:100px;backdrop-filter:blur(10px)\">x</div></body>");

        var items = dl.Items.ToList();
        var backdropIdx = items.FindIndex(i => i is DrawBackdropFilter);
        backdropIdx.Should().BeGreaterThanOrEqualTo(0, "backdrop-filter must emit a DrawBackdropFilter item");

        var backdrop = (DrawBackdropFilter)items[backdropIdx];
        backdrop.Filters.Should().HaveCount(1);
        backdrop.Filters[0].Kind.Should().Be(FilterFunctionKind.Blur);
        backdrop.Filters[0].Amount.Should().BeApproximately(10, 0.001);

        var fillIdx = items.FindIndex(i => i is FillRect { Color: { R: 0, G: 255, B: 0 } });
        fillIdx.Should().BeGreaterThan(backdropIdx, "the element's own background paints over the filtered patch");
    }

    // ---- End to end: CSS → builder → backend --------------------------------

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "10.1")]
    public void Promoted_filtered_layer_matches_the_flat_path()
    {
        // `filter` promotes a compositor layer (StackingContextResolver flags
        // LayerHint.Filter). The slice carries the PushFilter bracket, so the
        // tile rasterizer applies the chain and the composite blits the
        // already-filtered pixels — output must match the flat path.
        const int W = 200, H = 200;
        var html =
            "<body style=\"margin:0\">" +
            "<div style=\"position:absolute;left:60px;top:60px;width:80px;height:80px;" +
            "background-color:#ff0000;filter:grayscale(1) blur(6px)\">x</div>" +
            "</body>";

        var document = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(W, H));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var flat = backend.Render(new DisplayListBuilder().Build(root), new LayoutRect(0, 0, W, H), 1f);

        var tree = new LayerTreeBuilder().Build(root);
        using var layered = new CompositorEngine(backend).Render(tree, new LayoutRect(0, 0, W, H), 1f);

        // The box must actually be grayscaled on the layered path…
        var centre = layered.GetPixel(100, 100);
        Math.Abs(centre.R - centre.G).Should().BeLessThan(12, "the promoted layer must rasterize its filter");

        // …and the two paths must agree, including the blur halo outside the
        // border box (the layer/tile bounds carry the 3σ padding).
        var ssim = Ssim.ComputeRgba(layered.Rgba, flat.Rgba, layered.Width, layered.Height);
        ssim.Should().BeGreaterThanOrEqualTo(0.98, "the composited filter must match the flat path");
    }

    [TestMethod]
    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-grayscale", section: "10.1")]
    public void Filtered_element_renders_grayscale_end_to_end()
    {
        var dl = Build("<body style=\"margin:0\"><div style=\"background-color:#ff0000;width:100px;height:100px;filter:grayscale(1)\">x</div></body>");

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, new LayoutSize(400, 400));

        var px = bmp.GetPixel(80, 50);
        Math.Abs(px.R - px.G).Should().BeLessThan(10, "the red box must render grayscaled");
        ((int)px.R).Should().BeInRange(30, 90);
    }
}
