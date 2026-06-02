using Silk.NET.WebGPU;
using Starling.Common.Image;
using Starling.Css.Values;
using Rect = Starling.Layout.Rect;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Starling.Paint.Compositor;

/// <summary>
/// One layer ready to blend into the viewport: the layer's own bitmap (over a
/// transparent canvas), the content-hash key that lets the GPU keep its texture
/// resident across frames, the affine map from a layer-bitmap pixel to an output
/// device pixel, the effective opacity, and an optional device-space clip rect.
/// The <see cref="Compositor"/> builds these once and hands them to either the
/// CPU blend loop, the offscreen <see cref="GpuLayerCompositor"/>, or the
/// on-screen <see cref="GpuSurfacePresenter"/>, so every path shares the exact
/// same geometry.
/// </summary>
internal readonly struct LayerBlend(
    RenderedBitmap local,
    long contentHash,
    Matrix2D localToDevice,
    float opacity,
    Rect? clipDevice)
{
    public RenderedBitmap Local { get; } = local;
    public long ContentHash { get; } = contentHash;
    public Matrix2D LocalToDevice { get; } = localToDevice;
    public float Opacity { get; } = opacity;
    public Rect? ClipDevice { get; } = clipDevice;
}

/// <summary>
/// Blends a list of cached layer bitmaps into an offscreen texture on the GPU and
/// reads the result back to a CPU bitmap (wp:M12-13-gpu-composite-blend). Each
/// layer uploads to a wgpu texture once, keyed by its slice content hash, and
/// stays resident across frames. The blend is alpha-over in premultiplied space,
/// which reproduces the CPU <see cref="Compositor"/>'s <c>AlphaOver</c> math (the
/// framebuffer base is opaque white, so premultiplied and straight alpha agree on
/// readback). The reusable device + pipeline + texture cache live in
/// <see cref="GpuBlendEngine"/>; this class adds the offscreen render target and
/// the GPU→CPU readback. The on-screen, readback-free sibling is
/// <see cref="GpuSurfacePresenter"/>.
/// </summary>
internal sealed unsafe class GpuLayerCompositor : IDisposable
{
    // One process-wide instance: device acquisition is expensive and there is
    // exactly one paint thread. Lazily probed; null when the host has no adapter.
    private static readonly Lazy<GpuLayerCompositor?> _shared = new(TryCreate);
    internal static GpuLayerCompositor? Shared => _shared.Value;

    private readonly GpuBlendEngine _engine;
    private readonly object _gate = new();

    // Offscreen render target (Rgba8Unorm so readback bytes are straight RGBA) +
    // readback buffer, recreated when the viewport size changes.
    private Texture* _outTex;
    private TextureView* _outView;
    private int _outW, _outH;
    private WgpuBuffer* _readback;
    private nuint _readbackSize;

    private GpuLayerCompositor(GpuBlendEngine engine) => _engine = engine;

    /// <summary>
    /// Probes for a GPU adapter and builds the device once. Returns <c>null</c> on
    /// any failure so the caller can fall back to the CPU blend — a host with no
    /// GPU (CI, a sandbox) must still composite.
    /// </summary>
    internal static GpuLayerCompositor? TryCreate()
    {
        var engine = GpuBlendEngine.CreateOffscreen();
        return engine is null ? null : new GpuLayerCompositor(engine);
    }

    /// <summary>
    /// Blends <paramref name="ops"/> into <paramref name="output"/> (a
    /// width×height straight-alpha RGBA8 buffer pre-filled opaque white) on the
    /// GPU. Returns <c>false</c> on any GPU failure so the caller can fall back to
    /// the CPU blend for this frame.
    /// </summary>
    public bool Composite(byte[] output, int width, int height, IReadOnlyList<LayerBlend> ops)
    {
        if (width <= 0 || height <= 0) return false;
        lock (_gate)
        {
            try
            {
                _engine.BeginFrame();
                EnsureTarget(width, height);
                _engine.UploadLayerTextures(ops);
                var vertexCount = _engine.BuildAndUploadVertices(ops, width, height);
                RenderPass(width, height, ops, vertexCount);
                Readback(output, width, height);
                _engine.EvictStale();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private void EnsureTarget(int width, int height)
    {
        if (_outTex != null && _outW == width && _outH == height) return;
        var api = _engine.Api;

        if (_outView != null) { api.TextureViewRelease(_outView); _outView = null; }
        if (_outTex != null) { api.TextureRelease(_outTex); _outTex = null; }
        if (_readback != null) { api.BufferRelease(_readback); _readback = null; }

        var desc = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        _outTex = api.DeviceCreateTexture(_engine.Device, in desc);
        _outView = api.TextureCreateView(_outTex, (TextureViewDescriptor*)null);
        _outW = width;
        _outH = height;

        var padded = GpuBlendEngine.Align256((uint)(width * 4));
        _readbackSize = (nuint)((ulong)padded * (ulong)height);
        var bufDesc = new BufferDescriptor { Usage = BufferUsage.CopyDst | BufferUsage.MapRead, Size = _readbackSize, MappedAtCreation = false };
        _readback = api.DeviceCreateBuffer(_engine.Device, in bufDesc);
    }

    private void RenderPass(int width, int height, IReadOnlyList<LayerBlend> ops, uint vertexCount)
    {
        var api = _engine.Api;
        var encoder = api.DeviceCreateCommandEncoder(_engine.Device, (CommandEncoderDescriptor*)null);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _outView,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            // Opaque white base — the page background the flat path also clears to.
            ClearValue = new Color { R = 1, G = 1, B = 1, A = 1 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };
        var pass = api.CommandEncoderBeginRenderPass(encoder, in passDesc);

        _engine.RecordBlend(pass, ops, TextureFormat.Rgba8Unorm, vertexCount, width, height);

        api.RenderPassEncoderEnd(pass);

        // Copy the output texture into the readback buffer (256-byte row pitch).
        var padded = GpuBlendEngine.Align256((uint)(width * 4));
        var src = new ImageCopyTexture { Texture = _outTex, MipLevel = 0, Origin = new Origin3D { X = 0, Y = 0, Z = 0 }, Aspect = TextureAspect.All };
        var dst = new ImageCopyBuffer { Buffer = _readback, Layout = new TextureDataLayout { Offset = 0, BytesPerRow = padded, RowsPerImage = (uint)height } };
        var extent = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
        api.CommandEncoderCopyTextureToBuffer(encoder, in src, in dst, in extent);

        var cmd = api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
        api.QueueSubmit(_engine.Queue, 1, &cmd);
        api.CommandBufferRelease(cmd);
        api.CommandEncoderRelease(encoder);
        api.RenderPassEncoderRelease(pass);
    }

    private void Readback(byte[] output, int width, int height)
    {
        var api = _engine.Api;
        var mapped = false;
        var mapReady = new ManualResetEventSlim(false);
        var status = BufferMapAsyncStatus.Unknown;
        var cb = PfnBufferMapCallback.From((s, _) => { status = s; mapReady.Set(); });
        try
        {
            api.BufferMapAsync(_readback, MapMode.Read, 0, _readbackSize, cb, null);
            if (!WaitForMap(mapReady) || status != BufferMapAsyncStatus.Success)
                throw new InvalidOperationException($"WebGPU readback map failed: {status}");
            mapped = true;

            var padded = (int)GpuBlendEngine.Align256((uint)(width * 4));
            var rowBytes = width * 4;
            var src = (byte*)api.BufferGetConstMappedRange(_readback, 0, _readbackSize);
            if (src == null) throw new InvalidOperationException("WebGPU readback returned no data.");

            var srcSpan = new ReadOnlySpan<byte>(src, (int)_readbackSize);
            for (var row = 0; row < height; row++)
                srcSpan.Slice(row * padded, rowBytes).CopyTo(output.AsSpan(row * rowBytes, rowBytes));
        }
        finally
        {
            if (mapped) api.BufferUnmap(_readback);
            ((IDisposable)cb).Dispose();
            mapReady.Dispose();
        }
    }

    private bool WaitForMap(ManualResetEventSlim signal)
    {
        var poll = _engine.Poll;
        if (poll is null) return signal.Wait(5000);
        var deadline = Environment.TickCount64 + 5000;
        while (!signal.IsSet && Environment.TickCount64 < deadline)
            poll.DevicePoll(_engine.Device, true, (Silk.NET.WebGPU.Extensions.WGPU.WrappedSubmissionIndex*)null);
        return signal.IsSet;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            var api = _engine.Api;
            if (_readback != null) { api.BufferRelease(_readback); _readback = null; }
            if (_outView != null) { api.TextureViewRelease(_outView); _outView = null; }
            if (_outTex != null) { api.TextureRelease(_outTex); _outTex = null; }
            _engine.Dispose();
        }
    }
}
