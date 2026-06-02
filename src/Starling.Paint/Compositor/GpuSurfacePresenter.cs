using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using Starling.Common.Diagnostics;

namespace Starling.Paint.Compositor;

/// <summary>
/// Presents the layer-composite blend straight to a window's wgpu swapchain — no
/// readback, no re-upload (the zero-copy half of the
/// wp:M12-13-gpu-composite-blend pipeline). A native shell creates one of these
/// from its window via <see cref="CreateForWindow"/>, calls <see cref="Configure"/>
/// when the window resizes, and the compositor blends each frame's layers
/// directly into the surface's current texture and presents it.
/// </summary>
/// <remarks>
/// Shares the reusable <see cref="GpuBlendEngine"/> with the offscreen
/// <see cref="GpuLayerCompositor"/> — same device-resident layer-texture cache,
/// same alpha-over blend — but renders into the surface texture and calls
/// <c>SurfacePresent</c> instead of copying to a buffer and mapping it. The only
/// per-frame GPU↔CPU transfer this path keeps is uploading a *changed* layer's
/// bitmap (a cache miss); an unchanged frame touches no CPU memory.
/// </remarks>
public sealed unsafe class GpuSurfacePresenter : IDisposable
{
    private readonly GpuBlendEngine _engine;
    private readonly Surface* _surface;
    private readonly TextureFormat _format;
    private readonly IDiagnostics _diag;
    private readonly object _gate = new();
    private int _width, _height;
    private bool _configured;

    private GpuSurfacePresenter(GpuBlendEngine engine, Surface* surface, TextureFormat format, IDiagnostics? diagnostics)
    {
        _engine = engine;
        _surface = surface;
        _format = format;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
    }

    /// <summary>
    /// Builds a surface-compatible GPU device for <paramref name="window"/> and a
    /// presenter bound to its swapchain. Returns <c>null</c> when no GPU adapter is
    /// available. <paramref name="window"/> is the shell's window (a Silk.NET
    /// <c>IWindow</c> implements <see cref="INativeWindowSource"/>).
    /// </summary>
    public static GpuSurfacePresenter? CreateForWindow(INativeWindowSource window, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        var engine = GpuBlendEngine.CreateForSurface(window, out var surface, out var format);
        if (engine is null || surface == 0) return null;
        return new GpuSurfacePresenter(engine, (Surface*)surface, format, diagnostics);
    }

    /// <summary>
    /// Builds a surface-compatible GPU device bound to a host-owned
    /// <c>CAMetalLayer</c> (the Avalonia page surface's click-through child-view
    /// layer) and a presenter for its swapchain. Returns <c>null</c> when no GPU
    /// adapter is available or <paramref name="caMetalLayer"/> is zero. macOS only.
    /// </summary>
    public static GpuSurfacePresenter? CreateForMetalLayer(nint caMetalLayer, IDiagnostics? diagnostics = null)
    {
        if (caMetalLayer == 0) return null;
        var engine = GpuBlendEngine.CreateForMetalLayer(caMetalLayer, out var surface, out var format);
        if (engine is null || surface == 0) return null;
        return new GpuSurfacePresenter(engine, (Surface*)surface, format, diagnostics);
    }

    /// <summary>The swapchain colour format wgpu chose for this surface.</summary>
    public TextureFormat Format => _format;

    /// <summary>
    /// Configures (or reconfigures) the swapchain for a device-pixel size. Call
    /// once after creation and again on every window/framebuffer resize.
    /// </summary>
    public void Configure(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        lock (_gate)
        {
            var config = new SurfaceConfiguration
            {
                Device = _engine.Device,
                Format = _format,
                Usage = TextureUsage.RenderAttachment,
                Width = (uint)width,
                Height = (uint)height,
                PresentMode = PresentMode.Fifo, // vsync; the OS paces us
                AlphaMode = CompositeAlphaMode.Auto,
            };
            _engine.Api.SurfaceConfigure(_surface, in config);
            _width = width;
            _height = height;
            _configured = true;
        }
    }

    /// <summary>
    /// Blends <paramref name="ops"/> into the surface's current texture and
    /// presents it. The render target is <paramref name="width"/>×<paramref
    /// name="height"/> device pixels (must match the last <see cref="Configure"/>).
    /// Returns <c>false</c> if the frame could not be presented (e.g. the surface
    /// is outdated and needs reconfiguring).
    /// </summary>
    internal bool PresentOps(int width, int height, IReadOnlyList<LayerBlend> ops)
    {
        if (width <= 0 || height <= 0) return false;
        lock (_gate)
        {
            if (!_configured || _width != width || _height != height)
                Configure(width, height);

            var api = _engine.Api;
            try
            {
                // Acquire the swapchain's next drawable. Under PresentMode.Fifo this is
                // the call that blocks when the drawable pool is starved (on a
                // CAMetalLayer it bottoms out in Metal's nextDrawable, which waits up to
                // ~1s). Wrapped in its own span so a present stall is attributed here
                // rather than vanishing into gui.render's self-time.
                SurfaceTexture st = default;
                using (_diag.Span(RenderMetrics.PaintArea, RenderMetrics.PresentAcquireOp))
                    api.SurfaceGetCurrentTexture(_surface, ref st);
                if (st.Status != SurfaceGetCurrentTextureStatus.Success)
                {
                    // Outdated/Lost: reconfigure so the next frame succeeds.
                    if (st.Texture != null) api.TextureRelease(st.Texture);
                    Configure(width, height);
                    return false;
                }

                using (_diag.Span(RenderMetrics.PaintArea, RenderMetrics.PresentEncodeOp))
                {
                    _engine.BeginFrame();
                    _engine.UploadLayerTextures(ops);
                    var vertexCount = _engine.BuildAndUploadVertices(ops, width, height);

                    var view = api.TextureCreateView(st.Texture, (TextureViewDescriptor*)null);
                    var encoder = api.DeviceCreateCommandEncoder(_engine.Device, (CommandEncoderDescriptor*)null);

                    var colorAttachment = new RenderPassColorAttachment
                    {
                        View = view,
                        ResolveTarget = null,
                        LoadOp = LoadOp.Clear,
                        StoreOp = StoreOp.Store,
                        ClearValue = new Color { R = 1, G = 1, B = 1, A = 1 }, // opaque white base
                    };
                    var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };
                    var pass = api.CommandEncoderBeginRenderPass(encoder, in passDesc);

                    _engine.RecordBlend(pass, ops, _format, vertexCount, width, height);

                    api.RenderPassEncoderEnd(pass);

                    var cmd = api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
                    api.QueueSubmit(_engine.Queue, 1, &cmd);

                    using (_diag.Span(RenderMetrics.PaintArea, RenderMetrics.PresentSwapOp))
                        api.SurfacePresent(_surface);

                    api.CommandBufferRelease(cmd);
                    api.RenderPassEncoderRelease(pass);
                    api.CommandEncoderRelease(encoder);
                    api.TextureViewRelease(view);
                    api.TextureRelease(st.Texture);
                }

                _engine.EvictStale();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _engine.Api.SurfaceRelease(_surface);
            _engine.Dispose();
        }
    }
}
