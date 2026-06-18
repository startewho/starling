using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Css.Values;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint.Tests;

/// <summary>
/// GPU layer-composite behavior (wp:M12-13-gpu-composite-blend): determinism
/// under shared-cache churn, surface-overlay readback, and fully-clipped ops.
/// Skips when the host has no GPU adapter.
/// </summary>
[TestClass]
public sealed class GpuCompositeParityTests
{
    private static BlockBox Layout(string html, int w, int h)
    {
        var document = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        return engine.LayoutDocument(document, new Size(w, h));
    }

    [TestMethod]
    public void Gpu_blend_is_deterministic_under_cache_churn()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available.");
            return;
        }

        // The GPU compositor's texture cache is a process-wide singleton shared
        // across every frame. If rendering the same layer tree twice — with a
        // DIFFERENT tree churning the cache in between — ever differs, that is a
        // frame-to-frame flicker. Render A, then B, then A again, byte-for-byte.
        const int W = 200, H = 200;
        const string htmlA =
            "<body style=\"margin:0;background-color:#202030\">" +
            "<div style=\"position:absolute;left:10px;top:10px;width:120px;height:80px;background-color:#cc3344;transform:rotate(15deg)\"></div>" +
            "<div style=\"opacity:0.6;position:absolute;left:60px;top:60px;width:120px;height:80px;background-color:#3366cc\"></div>" +
            "</body>";
        const string htmlB =
            "<body style=\"margin:0;background-color:#103018\">" +
            "<div style=\"position:absolute;left:20px;top:30px;width:90px;height:90px;background-color:#33aa55;transform:rotate(40deg)\"></div>" +
            "<div style=\"opacity:0.5;position:absolute;left:80px;top:20px;width:100px;height:120px;background-color:#aa33aa\"></div>" +
            "</body>";

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);

        RenderedBitmap RenderGpu(string html)
        {
            var root = Layout(html, W, H);
            var tree = new LayerTreeBuilder().Build(root);
            return new CompositorEngine(backend).RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), 2.0f);
        }

        using var a1 = RenderGpu(htmlA);
        using var b = RenderGpu(htmlB);          // churn the shared cache
        using var a2 = RenderGpu(htmlA);          // same content as a1 — must match
        using var a3 = RenderGpu(htmlA);          // and again

        a1.Rgba.AsSpan().SequenceEqual(a2.Rgba).Should().BeTrue(
            "the same layer tree must blend identically after another tree churned the shared GPU cache");
        a2.Rgba.AsSpan().SequenceEqual(a3.Rgba).Should().BeTrue(
            "repeated renders of the same tree must be byte-identical (no flicker)");
    }

    [TestMethod]
    public void Gpu_texture_readback_blends_surface_overlays_on_top()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available.");
            return;
        }

        const int W = 160, H = 120;
        var root = Layout("<body style=\"margin:0;background-color:#102030\"></body>", W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);
        var tree = new LayerTreeBuilder().Build(root);
        var overlayScene = SurfaceOverlayScene.Create(50, 40, 0x450C0A7E1L,
            builder => builder.FillInstances(
                SurfaceOverlayPrimitive.Rectangle,
                [new SurfaceOverlayInstance(0, 0, 50, 40, new CssColor(255, 0, 0, 255))]));
        var overlays = new[] { new SurfaceOverlayLayer(20, 30, 50, 40, overlayScene) };

        using var rendered = new CompositorEngine(backend)
            .RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), 1.0f, overlays);

        rendered.GetPixel(45, 50).Should().Be((255, 0, 0, 255),
            "viewport readback must include surface overlay quads after page content");
    }

    [TestMethod]
    public void Gpu_blend_skips_fully_clipped_ops_without_losing_the_frame()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available.");
            return;
        }

        const int W = 120, H = 100;
        var html =
            "<body style=\"margin:0\">" +
            "<div style=\"position:absolute;left:0;top:0;width:120px;height:100px;background-color:#123456\"></div>" +
            "<div style=\"position:absolute;left:200px;top:200px;width:40px;height:40px;" +
            "overflow:hidden;opacity:.9;background-color:#ff0000\"></div>" +
            "</body>";
        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);
        var tree = new LayerTreeBuilder().Build(root);

        using var rendered = new CompositorEngine(backend)
            .RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), 1.0f);

        rendered.GetPixel(10, 10).Should().Be((18, 52, 86, 255),
            "a fully clipped promoted layer must not invalidate the GPU command buffer");
    }
}
