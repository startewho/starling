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
    private bool _disposed;

    public NativeViewportRenderer(IDiagnostics? diagnostics = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _backend = PaintBackendSelector.Create(FontResolver.Default, webFonts: null, _diag);
        _layerCaches = new LayerCacheStore(_diag);
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
        Func<Box, bool>? isAnimatingLayerRoot = null)
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
            return compositor.RenderToSurface(tree, region, scale, presenter);
        }
        catch (Exception ex)
        {
            _diag.LogException("shell", ex, $"layer-tree present via '{_backend.Name}' failed");
            throw;
        }
    }

    /// <summary>Drops all per-layer caches — call on navigation to a new document.</summary>
    public void ResetForNavigation() => _layerCaches.Clear();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }
}
