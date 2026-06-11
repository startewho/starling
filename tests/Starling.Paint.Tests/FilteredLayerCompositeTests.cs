using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// A layer-root <c>filter</c> chain rides the CompositorLayer (the slice's
/// bracket is suppressed) and is applied ONCE to the whole rastered layer by
/// <c>Compositor.EmitFilteredLayer</c> — not re-run inside every tile raster.
/// These tests pin the composite output (chain applied, halo painted) and the
/// caching behavior (one raster, cache hit on an unchanged frame, re-raster on
/// a filter change).
/// </summary>
[TestClass]
public sealed class FilteredLayerCompositeTests
{
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
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var compositor = new CompositorEngine(backend, tiles);

        var root = engine.LayoutDocument(doc, new Size(W, H));
        var tree = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root);
        using var bmp = compositor.Render(tree, new LayoutRect(0, 0, W, H), 1f);

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
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var compositor = new CompositorEngine(backend, tiles);

        var root = engine.LayoutDocument(doc, new Size(W, H));
        var tree = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root);
        using var bmp = compositor.Render(tree, new LayoutRect(0, 0, W, H), 1f);

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

    [TestMethod]
    public void Filtered_layer_rasters_once_then_serves_from_cache()
    {
        var (doc, engine, tiles) = Setup("blur(4px)");
        using var inner = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var counting = new CountingFilterBackend(inner);
        var compositor = new CompositorEngine(counting, tiles);

        var root1 = engine.LayoutDocument(doc, new Size(W, H));
        var tree1 = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root1);
        using (compositor.Render(tree1, new LayoutRect(0, 0, W, H), 1f)) { }
        var firstFrame = counting.FilteredCount;
        firstFrame.Should().Be(1, "the filtered layer rasters+filters exactly once on the first frame");

        // Unchanged second frame: every surface serves from cache.
        var root2 = engine.LayoutDocument(doc, new Size(W, H));
        var tree2 = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root2);
        using (compositor.Render(tree2, new LayoutRect(0, 0, W, H), 1f)) { }
        counting.FilteredCount.Should().Be(firstFrame, "an unchanged filtered layer re-blits from cache");
    }

    [TestMethod]
    public void Filter_change_re_rasters_the_layer()
    {
        var (doc, engine, tiles) = Setup("blur(4px)");
        using var inner = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var counting = new CountingFilterBackend(inner);
        var compositor = new CompositorEngine(counting, tiles);

        var root1 = engine.LayoutDocument(doc, new Size(W, H));
        var tree1 = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root1);
        using (compositor.Render(tree1, new LayoutRect(0, 0, W, H), 1f)) { }

        // The chain is folded into the layer's content hash, so an animating
        // radius re-keys the cached surface.
        doc.GetElementById("box")!.SetAttribute("style", BoxStyle + "filter:blur(8px)");
        var root2 = engine.LayoutDocument(doc, new Size(W, H));
        var tree2 = new LayerTreeBuilder(layerIdFor: tiles.LayerIdFor).Build(root2);
        using (compositor.Render(tree2, new LayoutRect(0, 0, W, H), 1f)) { }
        counting.FilteredCount.Should().Be(2, "a filter-amount change must re-raster the layer");
    }

    private sealed class CountingFilterBackend(IPaintBackend inner) : IPaintBackend
    {
        public int FilteredCount { get; private set; }
        public string Name => inner.Name;

        public RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale = 1.0f)
            => inner.Render(list, viewport, scale);

        public RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale, bool opaqueBackground)
            => inner.Render(list, viewport, scale, opaqueBackground);

        public RenderedBitmap RenderFiltered(PaintList list, LayoutRect viewport, float scale,
            IReadOnlyList<Starling.Paint.DisplayList.FilterFunction> filters)
        {
            FilteredCount++;
            return inner.RenderFiltered(list, viewport, scale, filters);
        }

        public void Dispose() => inner.Dispose();
    }
}
