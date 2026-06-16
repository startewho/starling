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
/// Responsible for rendering composited pages using GPU textures. This class handles
/// the rendering of a visual representation of a page layout, taking into account
/// styles, images, animations, and overlays.
/// </summary>
public sealed class CompositedPageRenderer : IDisposable
{
    private readonly ILogger _log;
    private readonly IPaintBackend _backend;
    private readonly TileGrid _tiles;
    private bool _disposed;

    public CompositedPageRenderer(
        FontResolver? fonts = null,
        FontFaceRegistry? webFonts = null,
        ILoggerFactory? loggerFactory = null)
    {
        loggerFactory ??= NullLoggerFactory.Instance;
        _log = loggerFactory.CreateLogger<CompositedPageRenderer>();
        _backend = PaintBackendSelector.Create(fonts ?? FontResolver.Default, webFonts);
        _tiles = new TileGrid();
    }

    public RenderedBitmap Render(
        BlockBox root,
        LayoutRect viewport,
        float scale = 1.0f,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        Func<Box, bool>? isAnimatingLayerRoot = null,
        Func<Element, (double X, double Y)>? scrollOffsets = null,
        IReadOnlyList<SurfaceOverlayLayer>? drawingOverlays = null,
        Func<Element, (double X, double Y)>? stickyShifts = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        try
        {
            var tree = new LayerTreeBuilder(styleOverride, images,
                isAnimatingLayerRoot: isAnimatingLayerRoot,
                layerIdFor: _tiles.LayerIdFor,
                scrollOffsets: scrollOffsets,
                stickyShifts: stickyShifts).Build(root);
            var compositor = new Starling.Paint.Compositor.Compositor(_backend, _tiles);
            return compositor.RenderGpuReadback(tree, viewport, scale, drawingOverlays);
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
