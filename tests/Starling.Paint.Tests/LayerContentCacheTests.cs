using AwesomeAssertions;
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
/// LTF-02: per-layer content-hash cache key. A cache HIT must reproduce the
/// from-scratch raster byte-for-byte. Skips when the host has no GPU adapter.
/// </summary>
[TestClass]
public sealed class LayerContentCacheTests
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
    public void Cached_reblit_is_byte_identical_to_a_from_scratch_render()
    {
        const int W = 160, H = 160;
        const float scale = 1f;
        var html =
            "<body style=\"margin:0;height:160px;background-color:#eef0ff\">" +
            "<div style=\"opacity:0.7;position:absolute;left:20px;top:20px;width:80px;height:80px;background-color:#cc2222\">Hi</div>" +
            "</body>";

        var doc = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);

        // A persistent-tile-grid compositor that has already cached every layer's tiles.
        var tiles = new TileGrid();
        var cached = new CompositorEngine(backend, tiles);
        var root = engine.LayoutDocument(doc, new Size(W, H));
        using (cached.RenderGpuReadback(new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root), new LayoutRect(0, 0, W, H), scale)) { }
        using var reblit = cached.RenderGpuReadback(new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root), new LayoutRect(0, 0, W, H), scale);

        // A cold compositor with its own fresh per-layer caches (no persistence).
        var fresh = new CompositorEngine(backend);
        using var scratch = fresh.RenderGpuReadback(new LayerTreeBuilder().Build(root), new LayoutRect(0, 0, W, H), scale);

        reblit.Width.Should().Be(scratch.Width);
        reblit.Height.Should().Be(scratch.Height);
        reblit.Rgba.AsSpan().SequenceEqual(scratch.Rgba).Should()
            .BeTrue("a content-cache HIT must reproduce the from-scratch raster byte-for-byte");
    }
}
