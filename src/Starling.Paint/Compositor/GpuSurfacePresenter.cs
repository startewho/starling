using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using Starling.Common.Diagnostics;
using Starling.Paint.Backend;

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
/// <c>SurfacePresent</c> instead of copying to a buffer and mapping it. Changed
/// layer tiles render into GPU textures on this presenter device and are adopted
/// by the cache, so the surface path does not need GPU-to-CPU tile readback.
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

    internal bool HasResidentTexture(long contentHash, int width, int height)
    {
        lock (_gate)
        {
            return _engine.HasResidentTexture(contentHash, width, height);
        }
    }

    internal GpuPaintDeviceContext ImageSharpContext => _engine.ImageSharpContext;

    internal void AdoptTexture(long contentHash, GpuPaintTexture texture)
    {
        lock (_gate)
        {
            _engine.AdoptTexture(contentHash, texture);
        }
    }

    /// <summary>
    /// Configures (or reconfigures) the swapchain for a device-pixel size. Call
    /// once after creation and again on every window/framebuffer resize.
    /// </summary>
    public void Configure(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Surface dimensions must be positive.");
        }

        GpuBlendEngine.ThrowIfTextureOversized("WebGPU surface target", width, height);
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
    /// Returns <c>true</c> after a successful present. Surface and GPU failures
    /// throw; a GPU session must not fall back to a CPU frame.
    /// </summary>
    internal bool PresentOps(int width, int height, IReadOnlyList<LayerBlend> ops)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Surface present target must have positive dimensions.");
        }

        lock (_gate)
        {
            if (!_configured || _width != width || _height != height)
                Configure(width, height);

            var api = _engine.Api;
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
                if (st.Texture != null)
                {
                    api.TextureRelease(st.Texture);
                }

                throw new InvalidOperationException($"GPU surface acquire failed with status {st.Status}.");
            }
            if (st.Texture == null)
            {
                throw new InvalidOperationException("GPU surface acquire succeeded without a texture.");
            }

            TextureView* view = null;
            CommandEncoder* encoder = null;
            RenderPassEncoder* pass = null;
            CommandBuffer* cmd = null;
            try
            {
                using (_diag.Span(RenderMetrics.PaintArea, RenderMetrics.PresentEncodeOp))
                {
                    _engine.BeginFrame();
                    _engine.UploadLayerTextures(ops);
                    var vertexCount = _engine.BuildAndUploadVertices(ops, width, height);

                    view = api.TextureCreateView(st.Texture, (TextureViewDescriptor*)null);
                    if (view == null)
                    {
                        throw new InvalidOperationException("GPU surface texture view creation failed.");
                    }

                    encoder = api.DeviceCreateCommandEncoder(_engine.Device, (CommandEncoderDescriptor*)null);
                    if (encoder == null)
                    {
                        throw new InvalidOperationException("GPU surface command encoder creation failed.");
                    }

                    var colorAttachment = new RenderPassColorAttachment
                    {
                        View = view,
                        ResolveTarget = null,
                        LoadOp = LoadOp.Clear,
                        StoreOp = StoreOp.Store,
                        ClearValue = new Color { R = 1, G = 1, B = 1, A = 1 }, // opaque white base
                    };
                    var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };
                    pass = api.CommandEncoderBeginRenderPass(encoder, in passDesc);
                    if (pass == null)
                    {
                        throw new InvalidOperationException("GPU surface render pass creation failed.");
                    }

                    _engine.RecordBlend(pass, ops, _format, vertexCount, width, height);

                    api.RenderPassEncoderEnd(pass);

                    cmd = api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
                    if (cmd == null)
                    {
                        throw new InvalidOperationException("GPU surface command buffer creation failed.");
                    }

                    api.QueueSubmit(_engine.Queue, 1, &cmd);
                }

                using (_diag.Span(RenderMetrics.PaintArea, RenderMetrics.PresentSwapOp))
                    api.SurfacePresent(_surface);

                _engine.EvictStale();
                return true;
            }
            finally
            {
                if (cmd != null)
                {
                    api.CommandBufferRelease(cmd);
                }

                if (pass != null)
                {
                    api.RenderPassEncoderRelease(pass);
                }

                if (encoder != null)
                {
                    api.CommandEncoderRelease(encoder);
                }

                if (view != null)
                {
                    api.TextureViewRelease(view);
                }

                api.TextureRelease(st.Texture);
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
