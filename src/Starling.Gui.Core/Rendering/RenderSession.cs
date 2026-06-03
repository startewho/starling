// SPDX-License-Identifier: Apache-2.0
using Silk.NET.Core.Contexts;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Layout.Box;
using Starling.Layout.Tree;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Gui.Core.Rendering;

/// <summary>
/// A render session owns the backend choice for one browser session. Callers hand it
/// a frame request and a target. The session picks the existing bitmap or surface
/// path behind that target.
/// </summary>
public interface IRenderSession : IDisposable
{
    bool SupportsSurfaceTargets { get; }

    RenderFrame Render(PageFrameRequest request, IFrameTarget target);

    RenderFrame RenderComposited(CompositedFrameRequest request, IFrameTarget target);

    void ResetForNavigation();
}

public static class RenderSessionFactory
{
    public static IRenderSession Create(IDiagnostics? diagnostics = null)
    {
        var diag = diagnostics ?? NoopDiagnostics.Instance;
        var forceReadback = Environment.GetEnvironmentVariable("STARLING_FORCE_READBACK") == "1";
        var supportsSurface = PaintBackendSelector.Selected == PaintBackendKind.ImageSharpWebGpu && !forceReadback;
        if (forceReadback)
        {
            diag.Log(DiagLevel.Info, "gui",
                "STARLING_FORCE_READBACK=1: using the bitmap present path.");
        }

        var backend = PaintBackendSelector.Create(FontResolver.Default, webFonts: null, diag);
        return new DefaultRenderSession(diag, backend, supportsSurface);
    }
}

public sealed class PageFrameRequest
{
    public required BlockBox Root { get; init; }
    public float Scale { get; init; } = 1.0f;
    public Func<Box, ComputedStyle?>? StyleOverride { get; init; }
    public IImageResolver? Images { get; init; }
    public LayoutRect? Viewport { get; init; }
    public int PageVersion { get; init; }
    public Func<Box, bool>? IsAnimatingLayerRoot { get; init; }
    public IReadOnlyList<SurfaceOverlayRect>? Overlays { get; init; }
    public Func<Element, (double X, double Y)>? ScrollOffsets { get; init; }
    public bool UseLayerTree { get; init; }
}

public sealed class CompositedFrameRequest
{
    public required int SurfaceWidth { get; init; }
    public required int SurfaceHeight { get; init; }
    public required float Scale { get; init; }
    public required BlockBox ChromeRoot { get; init; }
    public required double ChromeHeightCss { get; init; }
    public BlockBox? LeftChromeRoot { get; init; }
    public double LeftChromeWidthCss { get; init; }
    public required BlockBox PageRoot { get; init; }
    public double ScrollX { get; init; }
    public double ScrollY { get; init; }
    public Func<Box, bool>? PageAnimating { get; init; }
    public Func<Box, ComputedStyle?>? StyleOverride { get; init; }
    public IImageResolver? Images { get; init; }
    public BlockBox? OverlayRoot { get; init; }
    public BlockBox? ScreenOverlayRoot { get; init; }

    /// <summary>
    /// Optional bottom chrome (status bar) — a strip below the page, to the right
    /// of the sidebar, the same width as the page region. Null leaves the page
    /// filling all the way to the window bottom (the prior behaviour).
    /// </summary>
    public BlockBox? BottomChromeRoot { get; init; }
    public BlockBox? BottomChromeRightRoot { get; init; }
    public double BottomChromeLeftWidthCss { get; init; }
    public double BottomChromeHeightCss { get; init; }
}

public enum FrameTargetKind
{
    CpuBitmap,
    Surface,
}

public interface IFrameTarget
{
    FrameTargetKind Kind { get; }
}

public sealed class CpuBitmapFrameTarget : IFrameTarget
{
    public static CpuBitmapFrameTarget Instance { get; } = new();

    private CpuBitmapFrameTarget()
    {
    }

    public FrameTargetKind Kind => FrameTargetKind.CpuBitmap;
}

public abstract class SurfaceFrameTarget : IFrameTarget, IDisposable
{
    internal abstract GpuSurfacePresenter? Presenter { get; }

    public FrameTargetKind Kind => FrameTargetKind.Surface;

    public bool IsAvailable => Presenter is not null;

    public void Configure(int width, int height)
        => Presenter?.Configure(width, height);

    public abstract void Dispose();
}

public sealed class MetalLayerFrameTarget : SurfaceFrameTarget
{
    private GpuSurfacePresenter? _presenter;

    private MetalLayerFrameTarget(GpuSurfacePresenter presenter) => _presenter = presenter;

    internal override GpuSurfacePresenter? Presenter => _presenter;

    public static MetalLayerFrameTarget? TryCreate(nint caMetalLayer, IDiagnostics? diagnostics = null)
    {
        var presenter = GpuSurfacePresenter.CreateForMetalLayer(caMetalLayer, diagnostics);
        return presenter is null ? null : new MetalLayerFrameTarget(presenter);
    }

    public override void Dispose()
    {
        _presenter?.Dispose();
        _presenter = null;
    }
}

public sealed class NativePageSurfaceFrameTarget : SurfaceFrameTarget
{
    private GpuSurfacePresenter? _presenter;

    private NativePageSurfaceFrameTarget(GpuSurfacePresenter presenter) => _presenter = presenter;

    internal override GpuSurfacePresenter? Presenter => _presenter;

    public static NativePageSurfaceFrameTarget? TryCreate(
        NativePageSurface surface,
        IDiagnostics? diagnostics = null)
    {
        var presenter = surface.Kind switch
        {
            NativePageSurfaceKind.MetalLayer => GpuSurfacePresenter.CreateForMetalLayer(
                surface.Handle,
                diagnostics),
            NativePageSurfaceKind.WindowsHwnd => GpuSurfacePresenter.CreateForWindowsHwnd(
                surface.Handle,
                surface.AuxiliaryHandle,
                diagnostics),
            NativePageSurfaceKind.XlibWindow => GpuSurfacePresenter.CreateForXlibWindow(
                surface.AuxiliaryHandle,
                surface.WindowId,
                diagnostics),
            _ => null,
        };

        return presenter is null ? null : new NativePageSurfaceFrameTarget(presenter);
    }

    public override void Dispose()
    {
        _presenter?.Dispose();
        _presenter = null;
    }
}

public sealed class WindowSurfaceFrameTarget : SurfaceFrameTarget
{
    private GpuSurfacePresenter? _presenter;

    private WindowSurfaceFrameTarget(GpuSurfacePresenter presenter) => _presenter = presenter;

    internal override GpuSurfacePresenter? Presenter => _presenter;

    public static WindowSurfaceFrameTarget? TryCreate(INativeWindowSource window, IDiagnostics? diagnostics = null)
    {
        var presenter = GpuSurfacePresenter.CreateForWindow(window, diagnostics);
        return presenter is null ? null : new WindowSurfaceFrameTarget(presenter);
    }

    public override void Dispose()
    {
        _presenter?.Dispose();
        _presenter = null;
    }
}

public enum RenderFrameKind
{
    Unavailable,
    CpuBitmap,
    Presented,
}

public sealed class RenderFrame : IDisposable
{
    private RenderFrame(RenderFrameKind kind)
    {
        Kind = kind;
    }

    public RenderFrameKind Kind { get; }

    public bool Presented => Kind == RenderFrameKind.Presented;

    public static RenderFrame Unavailable() => new(RenderFrameKind.Unavailable);

    public static RenderFrame PresentedFrame() => new(RenderFrameKind.Presented);

    public void Dispose()
    {
    }
}

internal sealed class DefaultRenderSession : IRenderSession
{
    private readonly IDiagnostics _diag;
    private readonly IPaintBackend _backend;
    // private readonly PageRendererHost _bitmapRenderer;
    private readonly NativeViewportRenderer? _surfaceRenderer;
    private bool _disposed;

    public DefaultRenderSession(IDiagnostics diagnostics, IPaintBackend backend, bool supportsSurfaceTargets)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(backend);
        _diag = diagnostics;
        _backend = backend;
        // _bitmapRenderer = new PageRendererHost(_backend, _diag);
        _surfaceRenderer = supportsSurfaceTargets ? new NativeViewportRenderer(_backend, _diag) : null;
    }

    public bool SupportsSurfaceTargets => _surfaceRenderer is not null;

    public RenderFrame Render(PageFrameRequest request, IFrameTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);

        return target.Kind switch
        {
            // FrameTargetKind.CpuBitmap => RenderBitmap(request),
            FrameTargetKind.Surface => RenderSurface(request, target),
            _ => RenderFrame.Unavailable(),
        };
    }

    public RenderFrame RenderComposited(CompositedFrameRequest request, IFrameTarget target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);

        if (_surfaceRenderer is null)
        {
            throw new InvalidOperationException("This render session does not support GPU surface targets.");
        }
        if (target is not SurfaceFrameTarget { Presenter: { } presenter })
        {
            throw new InvalidOperationException("GPU surface target is unavailable.");
        }

        var ok = _surfaceRenderer.PresentComposited(
            presenter,
            request.SurfaceWidth,
            request.SurfaceHeight,
            request.Scale,
            request.ChromeRoot,
            request.ChromeHeightCss,
            request.LeftChromeRoot,
            request.LeftChromeWidthCss,
            request.PageRoot,
            request.ScrollX,
            request.ScrollY,
            request.PageAnimating,
            request.StyleOverride,
            request.Images,
            request.OverlayRoot,
            request.ScreenOverlayRoot,
            request.BottomChromeRoot,
            request.BottomChromeRightRoot,
            request.BottomChromeLeftWidthCss,
            request.BottomChromeHeightCss);
        if (!ok)
        {
            throw new InvalidOperationException("GPU surface compositor did not present the frame.");
        }
        return RenderFrame.PresentedFrame();
    }

    public void ResetForNavigation()
    {
        // _bitmapRenderer.ResetForNavigation();
        _surfaceRenderer?.ResetForNavigation();
    }

    // public void InvalidateBitmapCache()
    //     => _bitmapRenderer.InvalidateCache();

    // private RenderFrame RenderBitmap(PageFrameRequest request)
    // {
    //     var bitmap = request.UseLayerTree
    //         ? _bitmapRenderer.RenderViaLayerTree(
    //             request.Root,
    //             request.Scale,
    //             request.StyleOverride,
    //             request.Images,
    //             request.Viewport,
    //             request.IsAnimatingLayerRoot)
    //         : _bitmapRenderer.Render(
    //             request.Root,
    //             request.Scale,
    //             request.StyleOverride,
    //             request.Images,
    //             request.Viewport,
    //             request.PageVersion,
    //             request.ScrollOffsets);
    //
    //     return RenderFrame.FromBitmap(bitmap);
    // }

    private RenderFrame RenderSurface(PageFrameRequest request, IFrameTarget target)
    {
        if (_surfaceRenderer is null)
        {
            throw new InvalidOperationException("This render session does not support GPU surface targets.");
        }
        if (target is not SurfaceFrameTarget { Presenter: { } presenter })
        {
            throw new InvalidOperationException("GPU surface target is unavailable.");
        }

        var ok = _surfaceRenderer.Present(
            request.Root,
            presenter,
            request.Scale,
            request.StyleOverride,
            request.Images,
            request.Viewport,
            request.IsAnimatingLayerRoot,
            request.Overlays,
            request.ScrollOffsets);
        if (!ok)
        {
            throw new InvalidOperationException("GPU surface renderer did not present the frame.");
        }
        return RenderFrame.PresentedFrame();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _surfaceRenderer?.Dispose();
        // _bitmapRenderer.Dispose();
        _backend.Dispose();
    }
}
