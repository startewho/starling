using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint.Tests;

/// <summary>
/// Regression: presenting a backdrop-filter frame more than once must not fault
/// the wgpu device. The first live present after merge worked, the second
/// aborted in wgpu-core's storage table (a freed resource id was reused). The
/// offscreen readback compositor runs the same segmented blend path, so two
/// consecutive renders of the same tree reproduce the lifetime bug without a
/// window.
/// </summary>
[TestClass]
public sealed class BackdropFilterGpuRepeatTests
{
    [TestMethod]
    public void Backdrop_frame_renders_three_times_without_a_device_fault()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No WebGPU device available.");
        }

        // Mirrors the failing fixture: several backdrop panels (multiple
        // segments per frame), one with a color function, at retina scale.
        var doc = HtmlParser.Parse(
            "<body style=\"margin:0;background-color:#ffffff\">" +
            "<div style=\"position:absolute;left:0;top:40px;width:400px;height:40px;background-color:#222222\"></div>" +
            "<div style=\"position:absolute;left:20px;top:20px;width:100px;height:90px;" +
            "backdrop-filter:blur(8px);background-color:rgba(255,255,255,0.2)\"></div>" +
            "<div style=\"position:absolute;left:140px;top:20px;width:100px;height:90px;" +
            "backdrop-filter:blur(24px);background-color:rgba(255,255,255,0.2)\"></div>" +
            "<div style=\"position:absolute;left:260px;top:20px;width:100px;height:90px;" +
            "backdrop-filter:saturate(0.15);background-color:rgba(255,255,255,0.2)\"></div></body>");
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);
        var tiles = new TileGrid();

        for (var frame = 0; frame < 3; frame++)
        {
            var root = engine.LayoutDocument(doc, new Size(400, 200));
            var tree = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root);
            var compositor = new CompositorEngine(backend, tiles);
            using (compositor.RenderGpuTextures(tree, new LayoutRect(0, 0, 400, 200), 2f)) { }
        }
    }
}
