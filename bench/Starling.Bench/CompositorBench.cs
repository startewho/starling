using BenchmarkDotNet.Attributes;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;

namespace Starling.Bench;

// Compositor cost — the layer-tree paint path the live GUI uses
// (PageRendererHost.RenderViaLayerTree → Compositor.Render), split by cache
// state. The fixture is a grid of promoted cards, each its own layer with a
// persistent picture cache.
//
//   * WarmCache — render the same frame at an unchanged pageVersion. Every
//     layer's PictureCache.TryServe hits, so no backend.Render runs; the cost
//     is pure compositing (alpha-over blit + transform of each cached layer).
//     This is the best case: a frame where nothing a layer bakes-in changed.
//
//   * ColdCache — bump pageVersion every frame, so every layer misses its cache
//     and re-rasterizes through the WebGPU backend before compositing. This is
//     exactly the live animation loop, which busts the cache each frame
//     (pageVersion = DisplayListVersion + animation clock), so the whole layer
//     tree re-rasters every frame.
//
// The gap between the two is what a working per-layer cache buys — and what the
// live loop currently throws away by versioning every frame. Uses the shipped
// WebGPU backend (useWebGpu: true). [Cards] scales the layer count; [Scale] is
// the device pixel ratio (1.0 logical, 2.0 Retina).
[MemoryDiagnoser]
public class CompositorBench
{
    [Params(24, 96)]
    public int Cards;

    [Params(1.0f, 2.0f)]
    public float Scale;

    private static readonly Size Viewport = new(1200, 900);
    private static readonly Rect ViewportRect = new(0, 0, 1200, 900);

    private ImageSharpBackend _backend = null!;
    private Compositor _compositor = null!;
    private BlockBox _root = null!;
    private CompositorLayer _warmTree = null!;

    [GlobalSetup]
    public void Setup()
    {
        var doc = HtmlParser.Parse(Fixtures.PromotedCards(Cards));
        var style = new StyleEngine();
        style.AddStyleSheet(CssParser.ParseStyleSheet(Fixtures.PromotedCardsCss));
        using var measurer = new ImageSharpTextMeasurer(FontResolver.Default);
        _root = new LayoutEngine(style, measurer).LayoutDocument(doc, Viewport);

        _backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);
        // Warm path: a persistent tile grid keyed by layer id + tile position
        // Seed it once so every WarmCache call serves every tile from cache —
        // the steady-state animation frame.
        var warmTiles = new TileGrid();
        _compositor = new Compositor(_backend, warmTiles);
        _warmTree = new LayerTreeBuilder(layerIdFor: warmTiles.LayerIdFor).Build(_root);
        using (_compositor.RenderGpuReadback(_warmTree, ViewportRect, Scale)) { }
    }

    [GlobalCleanup]
    public void Cleanup() => _backend.Dispose();

    [Benchmark(Baseline = true)]
    public int Composite_WarmCache()
    {
        using var bmp = _compositor.RenderGpuReadback(_warmTree, ViewportRect, Scale);
        return bmp.Width;
    }

    [Benchmark]
    public int Composite_ColdCache()
    {
        // Cold path: a fresh tile grid + tree every call, so no tile is cached and
        // each re-rasters through the WebGPU backend before the composite blend —
        // the worst case the tile cache exists to avoid.
        var coldTiles = new TileGrid();
        var coldComp = new Compositor(_backend, coldTiles);
        var coldTree = new LayerTreeBuilder(layerIdFor: coldTiles.LayerIdFor).Build(_root);
        using var bmp = coldComp.RenderGpuReadback(coldTree, ViewportRect, Scale);
        return bmp.Width;
    }
}
