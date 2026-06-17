using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using Starling.Paint.DisplayList;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Acceptance tests for the GPU layer compositor: transform parity vs. the flat
/// path and opacity correctness. Skips when the host has no GPU adapter.
/// </summary>
[TestClass]
public sealed class CompositorTests
{
    [TestInitialize]
    public void RequireGpu()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available.");
        }
    }

    private static BlockBox Layout(string html, int w, int h)
    {
        var document = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        return engine.LayoutDocument(document, new Size(w, h));
    }

    private static RenderedBitmap FlatRender(ImageSharpBackend backend, BlockBox root, int w, int h, float scale)
    {
        PaintList list = new DisplayListBuilder().Build(root);
        return backend.Render(list, new LayoutRect(0, 0, w, h), scale);
    }

    [TestMethod]
    public void Promoted_rotation_matches_flat_path_within_ssim_tolerance()
    {
        const int W = 200, H = 200;
        const float scale = 1f;
        var html =
            "<body style=\"margin:0\">" +
            "<div style=\"position:absolute;left:50px;top:50px;width:100px;height:60px;" +
            "background-color:#cc2222;transform:rotate(45deg)\">Rotate</div>" +
            "</body>";

        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);

        // Flat M5 path bakes the rotation into the display list.
        using var flat = FlatRender(backend, root, W, H, scale);

        // New path: the div's layer holds the UPRIGHT content; the composite
        // applies the rotation.
        var tree = new LayerTreeBuilder().Build(root);
        using var layered = new CompositorEngine(backend).RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), scale);

        layered.Width.Should().Be(flat.Width);
        layered.Height.Should().Be(flat.Height);

        var ssim = Ssim.ComputeRgba(layered.Rgba, flat.Rgba, layered.Width, layered.Height);
        ssim.Should().BeGreaterThanOrEqualTo(0.99,
            "the composited rotation must match the flat pre-baked rotation");
    }

    [TestMethod]
    public void Opacity_layer_alpha_blends_over_the_background()
    {
        const int W = 120, H = 120;
        const float scale = 1f;
        // Solid blue page background; a 50%-opacity solid-red div on top. The
        // div is painted at FULL opacity into its cache, and the composite
        // applies 0.5 — the result over blue is (127.5, 0, 127.5)-ish.
        var html =
            "<body style=\"margin:0;background-color:#0000ff\">" +
            "<div style=\"opacity:0.5;width:120px;height:120px;background-color:#ff0000\"></div>" +
            "</body>";

        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);

        var tree = new LayerTreeBuilder().Build(root);
        using var output = new CompositorEngine(backend).RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), scale);

        // Sample the centre — well inside both the blue page and the red div.
        var (r, g, b, a) = output.GetPixel(W / 2, H / 2);
        a.Should().Be(255);
        // 0.5*red over blue → R≈128, B≈128, G≈0 (straight-alpha blend).
        ((int)r).Should().BeInRange(118, 138, "half-opacity red contributes ~128 R over blue");
        ((int)b).Should().BeInRange(118, 138, "the blue background shows through the 50% red");
        ((int)g).Should().BeLessThan(20, "neither layer contributes green");
    }
}
