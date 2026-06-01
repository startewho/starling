using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Layout.Box;
using Starling.Layout.Tree;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Gui.Core.Rendering;

/// <summary>
/// Renders a page's layer tree straight to a window's wgpu swapchain for the
/// native Silk.NET shell — the zero-copy present path. Unlike
/// <c>PageRendererHost</c>, which produces a <c>RenderedBitmap</c> for Avalonia
/// to re-upload, this builds the layer tree and hands its blend ops to a
/// <see cref="GpuSurfacePresenter"/>, which composites into the swapchain texture
/// and presents. The only per-frame GPU↔CPU transfer left is uploading a layer
/// whose content actually changed.
/// </summary>
/// <remarks>
/// Holds the same persistent per-layer caches as the Avalonia path (keyed by
/// slice content hash), so a transform/opacity-only frame re-blits cached layer
/// textures with no re-raster. The paint backend (CPU vs WebGPU) is still chosen
/// by <c>STARLING_PAINT_BACKEND</c>; it rasterizes the changed layer slices that
/// the presenter then uploads and blends.
/// </remarks>
public sealed class NativeViewportRenderer : IDisposable
{
    private readonly IDiagnostics _diag;
    private readonly IPaintBackend _backend;
    private readonly LayerCacheStore _layerCaches;
    private readonly LayerCacheStore _chromeCaches;
    private readonly LayerCacheStore _overlayCaches;
    private bool _disposed;

    public NativeViewportRenderer(IDiagnostics? diagnostics = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _backend = PaintBackendSelector.Create(FontResolver.Default, webFonts: null, _diag);
        _layerCaches = new LayerCacheStore(_diag);
        _chromeCaches = new LayerCacheStore(_diag);
        _overlayCaches = new LayerCacheStore(_diag);
    }

    /// <summary>
    /// Builds <paramref name="root"/>'s layer tree and presents it on
    /// <paramref name="presenter"/>'s swapchain. <paramref name="viewport"/> is the
    /// page-coordinate visible region (scroll offset + size); null renders the
    /// whole page. Returns <c>false</c> if the frame could not be presented (the
    /// surface may need reconfiguring — present the next frame).
    /// </summary>
    public bool Present(
        BlockBox root,
        GpuSurfacePresenter presenter,
        float scale = 1.0f,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        LayoutRect? viewport = null,
        Func<Box, bool>? isAnimatingLayerRoot = null,
        IReadOnlyList<SurfaceOverlayRect>? overlays = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(presenter);

        try
        {
            var tree = new LayerTreeBuilder(styleOverride, images, _diag, _layerCaches.CacheFor, isAnimatingLayerRoot).Build(root);
            var region = viewport ?? new LayoutRect(0, 0,
                Math.Max(1, root.Frame.Width),
                Math.Max(1, root.Frame.Height));
            var compositor = new Compositor(_backend, _diag);
            return compositor.RenderToSurface(tree, region, scale, presenter, overlays);
        }
        catch (Exception ex)
        {
            _diag.LogException("shell", ex, $"layer-tree present via '{_backend.Name}' failed");
            throw;
        }
    }

    /// <summary>
    /// Presents engine-rendered chrome (a strip <paramref name="chromeHeightCss"/>
    /// CSS px tall at the top) composited above the page, in one zero-copy frame.
    /// Both are real Starling documents: <paramref name="chromeRoot"/> is laid out
    /// at the window width × the chrome height, <paramref name="pageRoot"/> fills
    /// the region below. They blend into a single swapchain present — no overlay,
    /// no airspace. <paramref name="surfaceWidth"/>/<paramref name="surfaceHeight"/>
    /// are the swapchain's device-pixel dimensions.
    /// </summary>
    public bool PresentComposited(
        GpuSurfacePresenter presenter,
        int surfaceWidth,
        int surfaceHeight,
        float scale,
        BlockBox chromeRoot,
        double chromeHeightCss,
        BlockBox pageRoot,
        double scrollX = 0,
        double scrollY = 0,
        Func<Box, bool>? pageAnimating = null,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        BlockBox? overlayRoot = null,
        BlockBox? screenOverlayRoot = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(chromeRoot);
        ArgumentNullException.ThrowIfNull(pageRoot);
        if (surfaceWidth <= 0 || surfaceHeight <= 0 || scale <= 0) return false;

        var chromeDevH = (int)Math.Ceiling(chromeHeightCss * scale);
        var logicalW = surfaceWidth / scale;
        var logicalH = surfaceHeight / scale;

        var chromeTree = new LayerTreeBuilder(null, null, _diag, _chromeCaches.CacheFor, null).Build(chromeRoot);
        var pageTree = new LayerTreeBuilder(styleOverride, images, _diag, _layerCaches.CacheFor, pageAnimating).Build(pageRoot);
        // Optional overlay (find highlight, context menu, …) drawn in page space,
        // scrolling and clipping with the page content region.
        var overlayTree = overlayRoot is null
            ? null
            : new LayerTreeBuilder(null, images, _diag, _overlayCaches.CacheFor, null).Build(overlayRoot);
        // Screen-fixed overlay (context menu, devtools panel, …) drawn in window
        // space over everything, no scroll.
        var screenOverlayTree = screenOverlayRoot is null
            ? null
            : new LayerTreeBuilder(null, images, _diag, _overlayCaches.CacheFor, null).Build(screenOverlayRoot);

        var compositor = new Compositor(_backend, _diag);
        var ops = new List<LayerBlend>();
        var keepAlive = new List<RenderedBitmap>();
        try
        {
            // Chrome: top strip, no offset.
            compositor.AppendSurfaceOps(
                chromeTree,
                new LayoutRect(0, 0, logicalW, chromeHeightCss),
                scale, destOriginXDevice: 0, destOriginYDevice: 0,
                regionClipDevice: new LayoutRect(0, 0, surfaceWidth, chromeDevH),
                ops, keepAlive);

            // Page: below the chrome, scrolled, clipped to the content region.
            var pageRegion = new LayoutRect(scrollX, scrollY, logicalW, logicalH - chromeHeightCss);
            var pageClip = new LayoutRect(0, chromeDevH, surfaceWidth, surfaceHeight - chromeDevH);
            compositor.AppendSurfaceOps(
                pageTree, pageRegion,
                scale, destOriginXDevice: 0, destOriginYDevice: chromeDevH,
                regionClipDevice: pageClip,
                ops, keepAlive);

            // Overlay: same region/offset as the page so it tracks scroll.
            if (overlayTree is not null)
                compositor.AppendSurfaceOps(
                    overlayTree, pageRegion,
                    scale, destOriginXDevice: 0, destOriginYDevice: chromeDevH,
                    regionClipDevice: pageClip,
                    ops, keepAlive);

            // Screen overlay: whole window, no scroll, on top of everything.
            if (screenOverlayTree is not null)
                compositor.AppendSurfaceOps(
                    screenOverlayTree,
                    new LayoutRect(0, 0, logicalW, logicalH),
                    scale, destOriginXDevice: 0, destOriginYDevice: 0,
                    regionClipDevice: new LayoutRect(0, 0, surfaceWidth, surfaceHeight),
                    ops, keepAlive);

            return presenter.PresentOps(surfaceWidth, surfaceHeight, ops);
        }
        finally
        {
            foreach (var bmp in keepAlive) bmp.Dispose();
        }
    }

    /// <summary>Drops all per-layer caches — call on navigation to a new document.</summary>
    public void ResetForNavigation()
    {
        _layerCaches.Clear();
        _overlayCaches.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }
}
