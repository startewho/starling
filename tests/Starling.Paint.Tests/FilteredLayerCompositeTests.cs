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
/// A layer-root <c>filter</c> chain rides the CompositorLayer (the slice's
/// bracket is suppressed) and is applied ONCE to the whole rastered layer. These
/// tests pin the composite output (chain applied, halo painted). Skips when the
/// host has no GPU adapter.
/// </summary>
[TestClass]
public sealed class FilteredLayerCompositeTests
{
    [TestInitialize]
    public void RequireGpu()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available.");
        }
    }

    private const int W = 160, H = 160;
    private const string BoxStyle =
        "position:absolute;left:48px;top:48px;width:64px;height:64px;background-color:#cc2222;";

    private static (Starling.Dom.Document Doc, LayoutEngine Engine, TileGrid Tiles) Setup(string filterCss)
    {
        var doc = HtmlParser.Parse(
            "<body style=\"margin:0;height:160px;background-color:#ffffff\">" +
            $"<div id=box style=\"{BoxStyle}filter:{filterCss}\"></div></body>");
        return (doc, new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance), new TileGrid());
    }

    [TestMethod]
    public void Grayscale_layer_composites_with_the_chain_applied()
    {
        var (doc, engine, tiles) = Setup("grayscale(1)");
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);
        var compositor = new CompositorEngine(backend, tiles);

        var root = engine.LayoutDocument(doc, new Size(W, H));
        var tree = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root);
        using var bmp = compositor.RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), 1f);

        // Center of the box: the red fill must come out gray (R≈G≈B), proving
        // the chain ran on the composited layer, not just on the flat path.
        var (r, g, b, a) = bmp.GetPixel(80, 80);
        a.Should().Be(255);
        Math.Abs(r - g).Should().BeLessThan(8, "grayscale(1) collapses the channels");
        Math.Abs(g - b).Should().BeLessThan(8);
        r.Should().BeLessThan(200, "the red fill must not survive unfiltered");
    }

    [TestMethod]
    public void Blur_halo_paints_outside_the_border_box()
    {
        var (doc, engine, tiles) = Setup("blur(6px)");
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);
        var compositor = new CompositorEngine(backend, tiles);

        var root = engine.LayoutDocument(doc, new Size(W, H));
        var tree = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root);
        using var bmp = compositor.RenderGpuReadback(tree, new LayoutRect(0, 0, W, H), 1f);

        // 4px outside the box's left edge (page x=44, box at 48): the blur halo
        // must tint the white background — the padded surface and its geometry
        // mapping place the bleed correctly.
        var (r, g, b, _) = bmp.GetPixel(44, 80);
        (r < 250 || g < 250 || b < 250).Should().BeTrue("the blur halo bleeds past the border box");
        // Center is still clearly red-ish.
        var (cr, cg, _, _) = bmp.GetPixel(80, 80);
        cr.Should().BeGreaterThan(150);
        cg.Should().BeLessThan(120);
    }
}
