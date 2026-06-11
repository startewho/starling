using AwesomeAssertions;
using Starling.Css.Values;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using Starling.Spec;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Device-scale box-shadow caching (issue #82 item 5). The shadow raster is
/// cached at DEVICE resolution and blitted 1:1, so a steady-state frame never
/// pays a per-draw resample from CSS to device pixels. The cache key carries
/// the raster scale, so renders at different device scales do not collide.
/// </summary>
[TestClass]
public sealed class BoxShadowDeviceScaleCacheTests
{
    private static readonly CssColor Black = new(0, 0, 0, 255);

    private static bool IsDark((byte R, byte G, byte B, byte A) px)
        => px.R < 128 && px.G < 128 && px.B < 128;

    private static bool IsWhite((byte R, byte G, byte B, byte A) px)
        => px.R == 255 && px.G == 255 && px.B == 255;

    private static PaintList OuterShadowList()
    {
        // Box (40,40,80,60), offset (10,10), no blur/spread → a sharp black
        // rect at CSS (50,50,80,60).
        var list = new PaintList();
        list.Add(new DrawBoxShadow(new LayoutRect(40, 40, 80, 60), CornerRadii.None,
            OffsetX: 10, OffsetY: 10, Blur: 0, Spread: 0, Black, Inset: false));
        return list;
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "6")]
    public void Scale2_render_rasterizes_the_shadow_once_and_serves_repeats_from_cache()
    {
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var list = OuterShadowList();

        backend.Render(list, new LayoutSize(220, 200), 2f).Dispose();
        backend.BoxShadowRasterizationsForTest.Should().Be(1,
            "the first scale-2 frame seeds the device-resolution raster");

        backend.Render(list, new LayoutSize(220, 200), 2f).Dispose();
        backend.Render(list, new LayoutSize(220, 200), 2f).Dispose();
        backend.BoxShadowRasterizationsForTest.Should().Be(1,
            "repeat scale-2 frames must blit the cached device-resolution raster, not re-raster or resample");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "6")]
    public void Scale2_shadow_lands_at_device_coordinates()
    {
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(OuterShadowList(), new LayoutSize(220, 200), 2f);

        // Shadow rect CSS (50,50,80,60) → device (100,100)–(260,220).
        IsDark(bmp.GetPixel(180, 160)).Should().BeTrue("the shadow centre paints at 2x device coordinates");
        IsDark(bmp.GetPixel(104, 104)).Should().BeTrue("the shadow's top-left corner lands at device (100,100)");
        IsWhite(bmp.GetPixel(94, 160)).Should().BeTrue("left of the shadow stays clean");
        IsWhite(bmp.GetPixel(180, 94)).Should().BeTrue("above the shadow stays clean");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "6")]
    public void Blur_sigma_scales_with_the_device_scale()
    {
        // blur 16 → σ = 8 CSS px → σ = 16 device px at scale 2. A pixel 8
        // device px outside the silhouette edge must be a soft penumbra value —
        // neither untouched white nor the full shadow color.
        var list = new PaintList();
        list.Add(new DrawBoxShadow(new LayoutRect(40, 40, 80, 60), CornerRadii.None,
            OffsetX: 0, OffsetY: 0, Blur: 16, Spread: 0, Black, Inset: false));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(220, 200), 2f);

        // Silhouette edge at CSS x = 40 → device x = 80; probe 8 device px out.
        var penumbra = bmp.GetPixel(72, 140);
        IsWhite(penumbra).Should().BeFalse("a device-scaled blur reaches 8 device px outside the edge");
        IsDark(penumbra).Should().BeFalse("8 device px outside the edge is penumbra, not the shadow core");

        IsDark(bmp.GetPixel(160, 140)).Should().BeTrue("the shadow core is still solid");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "7.1.1")]
    public void Inset_shadow_caches_at_device_scale_too()
    {
        var list = new PaintList();
        list.Add(new DrawBoxShadow(new LayoutRect(40, 40, 120, 80), CornerRadii.None,
            OffsetX: 0, OffsetY: 0, Blur: 0, Spread: 20, Black, Inset: true));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        backend.Render(list, new LayoutSize(220, 200), 2f).Dispose();
        backend.Render(list, new LayoutSize(220, 200), 2f).Dispose();
        backend.BoxShadowRasterizationsForTest.Should().Be(1,
            "repeat scale-2 frames serve the inset ring from the device-resolution cache");

        using var bmp = backend.Render(list, new LayoutSize(220, 200), 2f);
        // CSS band probes from InsetBoxShadowPaintTests, at 2x device coords.
        IsDark(bmp.GetPixel(92, 160)).Should().BeTrue("left band is shadowed at device coordinates");
        IsWhite(bmp.GetPixel(200, 160)).Should().BeTrue("the centre stays clean at device coordinates");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "6")]
    public void Different_device_scales_use_distinct_cache_entries()
    {
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var list = OuterShadowList();

        backend.Render(list, new LayoutSize(220, 200), 1f).Dispose();
        backend.Render(list, new LayoutSize(220, 200), 2f).Dispose();
        backend.BoxShadowRasterizationsForTest.Should().Be(2,
            "scale 1 and scale 2 must not share a raster — the key carries the device scale");

        backend.Render(list, new LayoutSize(220, 200), 1f).Dispose();
        backend.Render(list, new LayoutSize(220, 200), 2f).Dispose();
        backend.BoxShadowRasterizationsForTest.Should().Be(2,
            "both scales then serve from their own cached raster");
    }

    [TestMethod]
    [Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow", section: "6")]
    public void Large_blur_shadow_downsamples_but_still_lands_at_the_offset_box()
    {
        // Blur 40 at scale 2 → device sigma 40, well past the downsample
        // threshold. The shadow must still be dark at the offset box's centre
        // and fade past the blur halo — pinning the proportional small-raster
        // geometry (a rounding bug would displace the whole shadow).
        var list = new PaintList();
        list.Add(new DrawBoxShadow(new LayoutRect(60, 60, 80, 60), CornerRadii.None,
            OffsetX: 10, OffsetY: 10, Blur: 40, Spread: 0, Black, Inset: false));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(260, 240), 2f);

        // Centre of the offset box (CSS 110,100 → device 220,200): solidly dark.
        IsDark(bmp.GetPixel(220, 200)).Should().BeTrue("the shadow core is opaque");
        // Far corner outside the halo: untouched white.
        IsWhite(bmp.GetPixel(8, 8)).Should().BeTrue("the halo must not reach the far corner");
        // Just outside the box edge: penumbra — neither solid nor white.
        var p = bmp.GetPixel(330, 200);
        IsWhite(p).Should().BeFalse("the blur bleeds past the box edge");
    }
}
