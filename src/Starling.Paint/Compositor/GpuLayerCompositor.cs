using Silk.NET.WebGPU;
using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Paint.Backend;
using Rect = Starling.Layout.Rect;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Starling.Paint.Compositor;

/// <summary>
/// Describes one layer that is ready to blend. It stores the layer texture key,
/// size, transform, opacity, and optional device-space clip. It may also carry
/// fresh bitmap pixels when the texture cache needs new content.
/// </summary>
internal readonly struct LayerBlend
{
    private LayerBlend(RenderedBitmap local, long contentHash, Matrix2D localToDevice, float opacity, Rect? clipDevice)
        : this(local, local.Width, local.Height, contentHash, localToDevice, opacity, clipDevice)
    {
    }

    private LayerBlend(RenderedBitmap? local, int width, int height, long contentHash, Matrix2D localToDevice, float opacity, Rect? clipDevice)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        Local = local;
        Width = width;
        Height = height;
        ContentHash = contentHash;
        LocalToDevice = localToDevice;
        Opacity = opacity;
        ClipDevice = clipDevice;
    }

    private RenderedBitmap? Local { get; }

    public int Width { get; }

    public int Height { get; }

    public long ContentHash { get; }

    public Matrix2D LocalToDevice { get; }

    public float Opacity { get; }

    public Rect? ClipDevice { get; }

    public bool HasLocalPixels => Local is not null;

    public static LayerBlend Bitmap(
        RenderedBitmap local, long contentHash, Matrix2D localToDevice, float opacity, Rect? clipDevice)
        => new(local, local.Width, local.Height, contentHash, localToDevice, opacity, clipDevice);

    public static LayerBlend ResidentTexture(
        int width,
        int height,
        long contentHash,
        Matrix2D localToDevice,
        float opacity,
        Rect? clipDevice)
        => new(null, width, height, contentHash, localToDevice, opacity, clipDevice);

    public LayerBlend WithGeometry(Matrix2D localToDevice, Rect? clipDevice)
        => new(Local, Width, Height, ContentHash, localToDevice, Opacity, clipDevice);

    public RenderedBitmap RequireLocalPixels()
        => Local ?? throw new InvalidOperationException("CPU blend requires local bitmap pixels.");
}

/// <summary>
/// Blends cached layer textures into an offscreen GPU target, then reads the
/// result into a CPU bitmap. Layer textures stay resident across frames and are
/// keyed by content hash. <see cref="GpuBlendEngine"/> owns the shared device,
/// pipeline, and texture cache. This class owns the offscreen target and the
/// readback buffer.
/// </summary>
internal sealed unsafe class GpuLayerCompositor : IGpuLayerTextureCache, IDisposable
{
    // One process-wide instance: device acquisition is expensive and there is
    // exactly one paint thread. Lazily probed; null when the host has no adapter.
    private static readonly Lazy<GpuLayerCompositor?> _shared = new(TryCreate);
    internal static GpuLayerCompositor? Shared => _shared.Value;

    private readonly GpuBlendEngine _engine;
    private readonly GpuOverlayRenderer _overlays;
    private readonly Lock _gate = new();

    // Offscreen render target (Rgba8Unorm so readback bytes are straight RGBA) +
    // readback buffer, recreated when the viewport size changes.
    private Texture* _outTex;
    private TextureView* _outView;
    private int _outW, _outH;
    private WgpuBuffer* _readback;
    private nuint _readbackSize;

    private GpuLayerCompositor(GpuBlendEngine engine)
    {
        _engine = engine;
        _overlays = new GpuOverlayRenderer(engine);
    }

    /// <summary>
    /// Creates the shared offscreen compositor. Returns <c>null</c> when WebGPU
    /// cannot create a device. Tests can skip GPU checks in that case.
    /// </summary>
    private static GpuLayerCompositor? TryCreate()
    {
        var engine = GpuBlendEngine.CreateOffscreen();
        return engine is null ? null : new GpuLayerCompositor(engine);
    }

    public bool HasResidentTexture(long contentHash, int width, int height)
    {
        lock (_gate)
        {
            return _engine.HasResidentTexture(contentHash, width, height);
        }
    }

    public GpuPaintDevice GpuDevice => _engine.GpuDevice;

    public void AdoptTexture(long contentHash, GpuPaintTexture texture)
    {
        lock (_gate)
        {
            _engine.AdoptTexture(contentHash, texture);
        }
    }

    public bool SupportsLayerFilters(IReadOnlyList<DisplayList.FilterFunction> filters)
        => GpuFilterEngine.Supports(filters);

    public GpuPaintTexture? ApplyLayerFilters(GpuPaintTexture source,
        IReadOnlyList<DisplayList.FilterFunction> filters, float scale)
    {
        lock (_gate)
        {
            try
            {
                return _engine.FilterEngine.Apply(source, filters, scale);
            }
            catch (InvalidOperationException)
            {
                // Apply consumed the source either way; null sends the caller
                // down the legacy bracket path.
                return null;
            }
        }
    }

    /// <summary>
    /// Blends <paramref name="ops"/> into <paramref name="output"/> on the GPU.
    /// The output buffer is RGBA pixels and should already be filled with opaque
    /// white. GPU errors throw because a WebGPU render should not fall back to the
    /// CPU blend path.
    /// </summary>
    public void Composite(
        byte[] output,
        int width,
        int height,
        IReadOnlyList<LayerBlend> ops,
        IReadOnlyList<GpuOverlayLayer>? overlayLayers = null)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Composite target must have positive dimensions.");
        }

        GpuBlendEngine.ThrowIfTextureOversized("WebGPU composite target", width, height);
        lock (_gate)
        {
            _engine.BeginFrame();
            EnsureTarget(width, height);
            _engine.UploadLayerTextures(ops);
            var vertexCount = _engine.BuildAndUploadVertices(ops, width, height);
            RenderPass(width, height, ops, vertexCount, overlayLayers);
            Readback(output, width, height);
            _engine.EvictStale();
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

    private void RenderPass(
        int width,
        int height,
        IReadOnlyList<LayerBlend> ops,
        uint vertexCount,
        IReadOnlyList<GpuOverlayLayer>? overlayLayers)
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
        _overlays.Record(pass, overlayLayers, width, height, TextureFormat.Rgba8Unorm);

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
            _overlays.Dispose();
            _engine.Dispose();
        }
    }
}
