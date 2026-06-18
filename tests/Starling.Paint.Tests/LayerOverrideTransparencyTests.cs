using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint.Tests;

[TestClass]
public sealed class LayerOverrideTransparencyTests
{
    [TestInitialize]
    public void RequireGpu()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available.");
        }
    }

    [TestMethod]
    public void Identity_styleOverride_is_a_noop_in_the_layer_tree_path()
    {
        const int W = 320, H = 240;
        // Text with spaces (to catch collapse) + a promoted (transformed/opacity) box.
        var html =
            "<body style=\"margin:0;background:#101018\">" +
            "<p style=\"color:#fff;font-size:18px\">hello there world</p>" +
            "<div style=\"opacity:0.7;position:absolute;left:40px;top:80px;width:120px;height:80px;" +
            "background:#cc3344;transform:rotate(10deg)\">tile text here</div>" +
            "</body>";
        var doc = HtmlParser.Parse(html);
        var root = new LayoutEngine(new StyleEngine(), Starling.Layout.Text.DefaultTextMeasurer.Instance)
            .LayoutDocument(doc, new Size(W, H));
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);

        RenderedBitmap Render(System.Func<Box, ComputedStyle?>? ov)
        {
            var tree = new LayerTreeBuilder(ov, null, null).Build(root);
            return new CompositorEngine(backend)
                .RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), 1.0f);
        }

        using var noOverride = Render(null);
        using var identity = Render(b => b.Style);   // returns the box's OWN style — a no-op

        var ssim = Ssim.ComputeRgba(noOverride.Rgba, identity.Rgba, W, H);
        ssim.Should().BeGreaterThanOrEqualTo(0.999,
            $"an identity styleOverride must not change the layer-tree render (ssim={ssim})");
    }
}
