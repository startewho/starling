// SPDX-License-Identifier: Apache-2.0
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Layout.Box;
using Starling.Layout.Tree;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint;

/// <summary>
/// Renders laid-out pages through the compositor's GPU texture path. This is the
/// headless/offscreen sibling of the GUI surface path: tiles are rastered as GPU
/// textures on the compositor device, adopted into the resident texture cache,
/// then blended before the final PNG readback.
/// </summary>
public sealed class CompositedPageRenderer : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly IPaintBackend _backend;
    private readonly TileGrid _tiles;
    private bool _disposed;

    public CompositedPageRenderer(
        FontResolver? fonts = null,
        FontFaceRegistry? webFonts = null,
        ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _log = _loggerFactory.CreateLogger<CompositedPageRenderer>();
        _backend = PaintBackendSelector.Create(fonts ?? FontResolver.Default, webFonts);
        _tiles = new TileGrid();
    }

    public string BackendName => _backend.Name;

    public RenderedBitmap Render(
        BlockBox root,
        LayoutRect viewport,
        float scale = 1.0f,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        Func<Box, bool>? isAnimatingLayerRoot = null,
        Func<Element, (double X, double Y)>? scrollOffsets = null,
        IReadOnlyList<SurfaceOverlayLayer>? drawingOverlays = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        try
        {
            var tree = new LayerTreeBuilder(styleOverride, images,
                isAnimatingLayerRoot: isAnimatingLayerRoot,
                layerIdFor: _tiles.LayerIdFor,
                scrollOffsets: scrollOffsets).Build(root);
            var compositor = new Starling.Paint.Compositor.Compositor(_backend, _tiles);
            return compositor.RenderGpuTextures(tree, viewport, scale, drawingOverlays);
        }
        catch (Exception ex)
        {
            CompositedPageRendererLog.RenderFailed(_log, ex, _backend.Name);
            throw;
        }
    }

    public void ResetForNavigation() => _tiles.Clear();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backend.Dispose();
    }
}

internal static partial class CompositedPageRendererLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "composited page render via '{BackendName}' failed")]
    public static partial void RenderFailed(ILogger logger, Exception ex, string backendName);
}
