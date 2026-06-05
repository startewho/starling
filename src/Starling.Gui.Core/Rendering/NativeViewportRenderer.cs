using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Layout.Box;
using Starling.Layout.Tree;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Gui.Core.Rendering;

/// <summary>
/// Renders page content into a native window surface. The host supplies a
/// <see cref="GpuSurfacePresenter"/>. This renderer builds the layer tree, sends
/// changed layer content to the presenter, and presents the frame.
/// </summary>
/// <remarks>
/// The renderer keeps one tile cache for the session. Frames that only move or
/// fade layers can reuse cached layer textures. The paint backend still draws
/// changed layer slices, then the presenter blends them on the GPU.
/// </remarks>
public sealed class NativeViewportRenderer : IDisposable
{
    private readonly IDiagnostics _diag;
    private readonly IPaintBackend _backend;

    private readonly bool _ownsBackend;

    // One session tile cache for all composited documents (chrome / page / overlay):
    // their layer-root elements are distinct objects, so they never collide on a
    // layer id within the shared grid.
    private readonly TileGrid _tiles;
    private bool _disposed;

    public NativeViewportRenderer(IDiagnostics? diagnostics = null)
        : this(PaintBackendSelector.Create(FontResolver.Default, webFonts: null, diagnostics), diagnostics,
            ownsBackend: true)
    {
    }

    internal NativeViewportRenderer(IPaintBackend backend, IDiagnostics? diagnostics = null)
        : this(backend, diagnostics, ownsBackend: false)
    {
    }

    private NativeViewportRenderer(IPaintBackend backend, IDiagnostics? diagnostics, bool ownsBackend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _backend = backend;
        _ownsBackend = ownsBackend;
        _tiles = new TileGrid(_diag);
    }

    /// <summary>
    /// Builds a layer tree for <paramref name="root"/> and presents it through
    /// <paramref name="presenter"/>. <paramref name="viewport"/> is the visible
    /// page region. Pass <c>null</c> to render the whole page. Returns
    /// <c>false</c> when the frame was not presented.
    /// </summary>
    public bool Present(
        BlockBox root,
        GpuSurfacePresenter presenter,
        float scale = 1.0f,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        LayoutRect? viewport = null,
        Func<Box, bool>? isAnimatingLayerRoot = null,
        IReadOnlyList<SurfaceOverlayLayer>? drawingOverlays = null,
        Func<Starling.Dom.Element, (double X, double Y)>? scrollOffsets = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(presenter);

        try
        {
            // DIAG (open-time investigation): split the cold first present into
            // layer-tree build vs raster+GPU present. Logged only for slow frames.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tree = new LayerTreeBuilder(styleOverride, images, _diag,
                isAnimatingLayerRoot: isAnimatingLayerRoot, layerIdFor: _tiles.LayerIdFor,
                scrollOffsets: scrollOffsets).Build(root);
            var tBuild = sw.ElapsedMilliseconds;
            var region = viewport ?? new LayoutRect(0, 0,
                Math.Max(1, root.Frame.Width),
                Math.Max(1, root.Frame.Height));
            var compositor = new Compositor(_backend, _diag, _tiles);
            var ok = compositor.RenderToSurface(tree, region, scale, presenter, drawingOverlays);
            if (sw.ElapsedMilliseconds > 100)
                _diag.Log(DiagLevel.Info, "shell",
                    $"present.cold: total={sw.ElapsedMilliseconds}ms layertree.build={tBuild}ms renderToSurface={sw.ElapsedMilliseconds - tBuild}ms");
            return ok;
        }
        catch (Exception ex)
        {
            _diag.LogException("shell", ex, $"layer-tree present via '{_backend.Name}' failed");
            throw;
        }
    }

    /// <summary>
    /// Presents chrome and page content in one surface frame. Top chrome, side
    /// chrome, bottom chrome, page content, and overlays are blended into the
    /// same present.
    /// </summary>
    public bool PresentComposited(
        GpuSurfacePresenter presenter,
        int surfaceWidth,
        int surfaceHeight,
        float scale,
        BlockBox chromeRoot,
        double chromeHeightCss,
        BlockBox? leftChromeRoot,
        double leftChromeWidthCss,
        BlockBox pageRoot,
        double scrollX = 0,
        double scrollY = 0,
        Func<Box, bool>? pageAnimating = null,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        BlockBox? overlayRoot = null,
        BlockBox? screenOverlayRoot = null,
        BlockBox? bottomChromeRoot = null,
        BlockBox? bottomChromeRightRoot = null,
        double bottomChromeLeftWidthCss = 0,
        double bottomChromeHeightCss = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(chromeRoot);
        ArgumentNullException.ThrowIfNull(pageRoot);
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
        {
            throw new ArgumentException("Surface dimensions must be positive.");
        }

        if (scale <= 0)
        {
            throw new ArgumentException("Scale must be positive.", nameof(scale));
        }

        var chromeDevH = (int)Math.Ceiling(chromeHeightCss * scale);
        var hasBottomChrome = bottomChromeRoot is not null || bottomChromeRightRoot is not null;
        var bottomChromeCss = hasBottomChrome ? bottomChromeHeightCss : 0;
        var bottomDevH = hasBottomChrome ? (int)Math.Ceiling(bottomChromeCss * scale) : 0;
        var leftChromeCss = leftChromeRoot is null ? 0 : leftChromeWidthCss;
        var leftDevW = leftChromeRoot is null ? 0 : (int)Math.Ceiling(leftChromeCss * scale);
        var logicalW = surfaceWidth / scale;
        var logicalH = surfaceHeight / scale;
        var pageLogicalW = Math.Max(1, logicalW - leftChromeCss);
        var pageLogicalH = Math.Max(1, logicalH - chromeHeightCss - bottomChromeCss);
        var contentDevW = Math.Max(1, surfaceWidth - leftDevW);
        var contentDevH = Math.Max(1, surfaceHeight - chromeDevH - bottomDevH);

        var chromeTree = new LayerTreeBuilder(null, null, _diag, layerIdFor: _tiles.LayerIdFor).Build(chromeRoot);
        var leftChromeTree = leftChromeRoot is null
            ? null
            : new LayerTreeBuilder(null, null, _diag, layerIdFor: _tiles.LayerIdFor).Build(leftChromeRoot);
        var pageTree = new LayerTreeBuilder(styleOverride, images, _diag,
            isAnimatingLayerRoot: pageAnimating, layerIdFor: _tiles.LayerIdFor).Build(pageRoot);
        // Optional overlay (find highlight, context menu, …) drawn in page space,
        // scrolling and clipping with the page content region.
        var overlayTree = overlayRoot is null
            ? null
            : new LayerTreeBuilder(null, images, _diag, layerIdFor: _tiles.LayerIdFor).Build(overlayRoot);
        // Screen-fixed overlay (context menu, devtools panel, …) drawn in window
        // space over everything, no scroll.
        var screenOverlayTree = screenOverlayRoot is null
            ? null
            : new LayerTreeBuilder(null, images, _diag, layerIdFor: _tiles.LayerIdFor).Build(screenOverlayRoot);
        // Bottom chrome (status bar): same width as the page, fixed at the window
        // bottom to the right of the sidebar.
        var bottomChromeTree = bottomChromeRoot is null
            ? null
            : new LayerTreeBuilder(null, null, _diag, layerIdFor: _tiles.LayerIdFor).Build(bottomChromeRoot);
        var bottomChromeRightTree = bottomChromeRightRoot is null
            ? null
            : new LayerTreeBuilder(null, null, _diag, layerIdFor: _tiles.LayerIdFor).Build(bottomChromeRightRoot);

        var compositor = new Compositor(_backend, _diag, _tiles);
        var ops = new List<LayerBlend>();

        // Sidebar: left strip, full height.
        if (leftChromeTree is not null)
            compositor.AppendSurfaceOps(
                leftChromeTree,
                new LayoutRect(0, 0, leftChromeCss, logicalH),
                scale, destOriginXDevice: 0, destOriginYDevice: 0,
                regionClipDevice: new LayoutRect(0, 0, leftDevW, surfaceHeight),
                ops, presenter);

        // Top chrome: to the right of the sidebar.
        compositor.AppendSurfaceOps(
            chromeTree,
            new LayoutRect(0, 0, pageLogicalW, chromeHeightCss),
            scale, destOriginXDevice: leftDevW, destOriginYDevice: 0,
            regionClipDevice: new LayoutRect(leftDevW, 0, contentDevW, chromeDevH),
            ops, presenter);

        // Page: below the toolbar and to the right of the sidebar.
        var pageRegion = new LayoutRect(scrollX, scrollY, pageLogicalW, pageLogicalH);
        var pageClip = new LayoutRect(leftDevW, chromeDevH, contentDevW, contentDevH);
        compositor.AppendSurfaceOps(
            pageTree, pageRegion,
            scale, destOriginXDevice: leftDevW, destOriginYDevice: chromeDevH,
            regionClipDevice: pageClip,
            ops, presenter);

        // Overlay: same region/offset as the page so it tracks scroll.
        if (overlayTree is not null)
            compositor.AppendSurfaceOps(
                overlayTree, pageRegion,
                scale, destOriginXDevice: leftDevW, destOriginYDevice: chromeDevH,
                regionClipDevice: pageClip,
                ops, presenter);

        // Bottom chrome (status bar): the strip below the page, same width as the
        // page region, fixed at the window bottom. Drawn after the page so it sits
        // over any page content that would otherwise bleed into the strip.
        if (bottomChromeTree is not null && bottomChromeRightTree is null)
            compositor.AppendSurfaceOps(
                bottomChromeTree,
                new LayoutRect(0, 0, pageLogicalW, bottomChromeCss),
                scale, destOriginXDevice: leftDevW, destOriginYDevice: surfaceHeight - bottomDevH,
                regionClipDevice: new LayoutRect(leftDevW, surfaceHeight - bottomDevH, contentDevW, bottomDevH),
                ops, presenter);
        else if (bottomChromeTree is not null || bottomChromeRightTree is not null)
        {
            var leftCss = bottomChromeLeftWidthCss <= 0
                ? pageLogicalW / 2
                : Math.Clamp(bottomChromeLeftWidthCss, 1, Math.Max(1, pageLogicalW - 1));
            var rightCss = Math.Max(1, pageLogicalW - leftCss);
            var leftDev = Math.Clamp((int)Math.Round(leftCss * scale), 1, Math.Max(1, contentDevW - 1));
            var rightDev = Math.Max(1, contentDevW - leftDev);
            var bottomDevY = surfaceHeight - bottomDevH;

            if (bottomChromeTree is not null)
                compositor.AppendSurfaceOps(
                    bottomChromeTree,
                    new LayoutRect(0, 0, leftCss, bottomChromeCss),
                    scale, destOriginXDevice: leftDevW, destOriginYDevice: bottomDevY,
                    regionClipDevice: new LayoutRect(leftDevW, bottomDevY, leftDev, bottomDevH),
                    ops, presenter);

            if (bottomChromeRightTree is not null)
                compositor.AppendSurfaceOps(
                    bottomChromeRightTree,
                    new LayoutRect(0, 0, rightCss, bottomChromeCss),
                    scale, destOriginXDevice: leftDevW + leftDev, destOriginYDevice: bottomDevY,
                    regionClipDevice: new LayoutRect(leftDevW + leftDev, bottomDevY, rightDev, bottomDevH),
                    ops, presenter);
        }

        // Screen overlay: whole window, no scroll, on top of everything.
        if (screenOverlayTree is not null)
            compositor.AppendSurfaceOps(
                screenOverlayTree,
                new LayoutRect(0, 0, logicalW, logicalH),
                scale, destOriginXDevice: 0, destOriginYDevice: 0,
                regionClipDevice: new LayoutRect(0, 0, surfaceWidth, surfaceHeight),
                ops, presenter);

        // Emit this frame's tile miss-ratio / rasters-per-frame. The multi-doc
        // path has no single Render exit to hook, so flush after the last
        // AppendSurfaceOps (the Compositor is one frame, so the tally is exact).
        compositor.FlushTileFrameMetrics();
        return presenter.PresentOps(surfaceWidth, surfaceHeight, ops);
    }

    /// <summary>Clears cached tiles after navigation to a new document.</summary>
    public void ResetForNavigation()
    {
        _tiles.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsBackend)
        {
            _backend.Dispose();
        }
    }
}
