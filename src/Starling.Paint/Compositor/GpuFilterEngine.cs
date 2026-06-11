// SPDX-License-Identifier: Apache-2.0
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;

namespace Starling.Paint.Compositor;

/// <summary>
/// Texture-to-texture CSS filter passes on the compositor's wgpu device: a
/// separable Gaussian blur (with downsampling for large sigmas) and the
/// color-matrix functions of Filter Effects 1 §10.1 (brightness, contrast,
/// grayscale, sepia, saturate, hue-rotate, invert, opacity). Replaces the CPU
/// ImageSharp filter path for promoted filter layers — the layer's straight-alpha
/// raster goes in, a straight-alpha result texture comes out, and the chain runs
/// entirely on the GPU in one command-buffer submit.
/// </summary>
/// <remarks>
/// Alpha convention: blur must average premultiplied colour (averaging straight
/// RGB across alpha edges bleeds the colour of transparent texels), so the first
/// pass premultiplies its input taps, every intermediate texture holds
/// premultiplied pixels, the colour-matrix stage un/re-premultiplies around the
/// matrix (the spec's matrices operate on straight RGBA), and the final pass
/// un-premultiplies — keeping the "adopted layer textures are straight RGBA"
/// contract the blend engine's <c>fs_straight</c> entry expects.
/// <para>
/// Large blurs downsample first: a Gaussian's cost is O(radius) per texel per
/// pass, so sigma is capped at <see cref="MaxSigmaPerPass"/> by rendering at
/// 1/2^n resolution with sigma/2^n (the standard pyramid trick). The result
/// texture stays small — the blend quad's linear sampling upscales it, which is
/// visually exact for a low-passed image.
/// </para>
/// </remarks>
internal sealed unsafe class GpuFilterEngine : IDisposable
{
    // Above this sigma a pass downsamples instead of widening the kernel:
    // radius = ceil(3σ) taps per texel per direction, so σ=6 → 37-tap loops,
    // about the cost sweet spot before memory bandwidth dominates.
    private const float MaxSigmaPerPass = 6.0f;

    private readonly WebGPU _api;
    private readonly Device* _device;
    private readonly Queue* _queue;

    private BindGroupLayout* _bindLayout;
    private PipelineLayout* _pipelineLayout;
    private Sampler* _sampler;
    private ShaderModule* _shader;
    private RenderPipeline* _pipeline;
    private bool _disposed;

    internal GpuFilterEngine(WebGPU api, Device* device, Queue* queue)
    {
        _api = api;
        _device = device;
        _queue = queue;
        BuildPipeline();
    }

    /// <summary>True when every function in <paramref name="filters"/> runs on
    /// the GPU. drop-shadow (silhouette rebuild) is not implemented yet and
    /// falls back to the CPU bracket path.</summary>
    internal static bool Supports(IReadOnlyList<FilterFunction> filters)
    {
        for (var i = 0; i < filters.Count; i++)
        {
            if (filters[i].Kind == FilterFunctionKind.DropShadow)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Runs <paramref name="filters"/> in order over <paramref name="source"/>
    /// (straight-alpha RGBA8) and returns a NEW straight-alpha texture, possibly
    /// smaller than the source (blur downsampling). Consumes
    /// <paramref name="source"/>. Returns the source unchanged when the chain is
    /// entirely no-op.
    /// </summary>
    internal GpuPaintTexture Apply(GpuPaintTexture source, IReadOnlyList<FilterFunction> filters, float scale)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var passes = PlanPasses(filters, scale, source.Width, source.Height);
        if (passes.Count == 0)
        {
            return source;
        }

        // First pass premultiplies the straight-alpha source; the last emits
        // straight alpha again for the blend engine's adopt contract.
        var p0 = passes[0];
        p0.PremultiplyInput = true;
        passes[0] = p0;
        var pn = passes[^1];
        pn.UnpremultiplyOutput = true;
        passes[^1] = pn;

        var encoder = _api.DeviceCreateCommandEncoder(_device, (CommandEncoderDescriptor*)null);
        var srcView = (TextureView*)source.TextureViewHandle;
        // Intermediates of finished passes, released after submit (wgpu defers
        // the actual destruction until the queue is done with them).
        var retired = new List<(nint Tex, nint View)>(passes.Count);
        Texture* curTex = null;
        TextureView* curView = srcView;

        try
        {
            for (var i = 0; i < passes.Count; i++)
            {
                var pass = passes[i];
                CreateTarget(pass.TargetWidth, pass.TargetHeight, out var tex, out var view);
                EncodePass(encoder, curView, view, pass);
                if (curTex != null)
                {
                    retired.Add(((nint)curTex, (nint)curView));
                }

                curTex = tex;
                curView = view;
            }

            var cmd = _api.CommandEncoderFinish(encoder, (CommandBufferDescriptor*)null);
            _api.QueueSubmit(_queue, 1, &cmd);
            _api.CommandBufferRelease(cmd);
        }
        catch
        {
            if (curTex != null)
            {
                _api.TextureViewRelease(curView);
                _api.TextureRelease(curTex);
            }

            throw;
        }
        finally
        {
            _api.CommandEncoderRelease(encoder);
            foreach (var (tex, view) in retired)
            {
                _api.TextureViewRelease((TextureView*)view);
                _api.TextureRelease((Texture*)tex);
            }

            source.Dispose();
        }

        var final = passes[^1];
        return new GpuPaintTexture(
            (nint)curTex,
            (nint)curView,
            final.TargetWidth,
            final.TargetHeight,
            PaintTextureFormat.Rgba8Unorm,
            new OwnedTexture(_api, (nint)curTex, (nint)curView));
    }

    private struct FilterPass
    {
        public int TargetWidth;
        public int TargetHeight;
        public float DirX; // blur step in source-texture UV units
        public float DirY;
        public int Radius; // taps each side; 0 = plain sampled copy
        public float Sigma;
        public bool UseMatrix;
        public float[]? Matrix; // 16 floats m0..m3 (rows), straight-alpha space
        public float[]? Offset; // 4 floats
        public bool PremultiplyInput;
        public bool UnpremultiplyOutput;
    }

    /// <summary>Turns the chain into a pass list, tracking the working surface
    /// size across blur downsampling. No-op functions emit no pass (matching the
    /// CPU chain's skips), so an all-no-op chain returns an empty plan.</summary>
    private static List<FilterPass> PlanPasses(IReadOnlyList<FilterFunction> filters, float scale, int width, int height)
    {
        var passes = new List<FilterPass>();
        var w = width;
        var h = height;

        for (var i = 0; i < filters.Count; i++)
        {
            var f = filters[i];
            switch (f.Kind)
            {
                case FilterFunctionKind.Blur:
                    {
                        var sigma = (float)(FilterFunction.BlurSigma(f.Amount) * scale);
                        if (sigma <= 0)
                        {
                            break;
                        }

                        while (sigma > MaxSigmaPerPass && w > 1 && h > 1)
                        {
                            w = Math.Max(1, (w + 1) / 2);
                            h = Math.Max(1, (h + 1) / 2);
                            sigma /= 2;
                            passes.Add(new FilterPass { TargetWidth = w, TargetHeight = h });
                        }

                        var radius = Math.Max(1, (int)Math.Ceiling(3 * sigma));
                        passes.Add(new FilterPass
                        {
                            TargetWidth = w,
                            TargetHeight = h,
                            DirX = 1f / w,
                            Radius = radius,
                            Sigma = sigma,
                        });
                        passes.Add(new FilterPass
                        {
                            TargetWidth = w,
                            TargetHeight = h,
                            DirY = 1f / h,
                            Radius = radius,
                            Sigma = sigma,
                        });
                        break;
                    }
                default:
                    {
                        if (TryColorMatrix(f, out var m, out var off))
                        {
                            passes.Add(new FilterPass
                            {
                                TargetWidth = w,
                                TargetHeight = h,
                                UseMatrix = true,
                                Matrix = m,
                                Offset = off,
                            });
                        }

                        break;
                    }
            }
        }

        return passes;
    }

    /// <summary>
    /// The Filter Effects 1 §10.2 colour matrix for one function, in
    /// straight-alpha space: out.ch = dot(row, rgba) + offset.ch. Returns false
    /// for a no-op amount (the CPU chain skips those too).
    /// </summary>
    private static bool TryColorMatrix(FilterFunction f, out float[] m, out float[] offset)
    {
        m = [];
        offset = [0f, 0f, 0f, 0f];
        switch (f.Kind)
        {
            case FilterFunctionKind.Brightness:
                {
                    var t = (float)Math.Max(0, f.Amount);
                    m = Scale3(t);
                    return true;
                }

            case FilterFunctionKind.Contrast:
                {
                    var t = (float)Math.Max(0, f.Amount);
                    m = Scale3(t);
                    var o = 0.5f * (1 - t);
                    offset = [o, o, o, 0f];
                    return true;
                }

            case FilterFunctionKind.Saturate:
                {
                    var t = (float)Math.Max(0, f.Amount);
                    m = SaturateMatrix(t);
                    return true;
                }

            case FilterFunctionKind.Grayscale:
                {
                    var t = (float)Math.Clamp(f.Amount, 0, 1);
                    if (t <= 0)
                    {
                        return false;
                    }

                    // §10.1: grayscale(t) ≡ saturate(1 − t).
                    m = SaturateMatrix(1 - t);
                    return true;
                }

            case FilterFunctionKind.Sepia:
                {
                    var t = (float)Math.Clamp(f.Amount, 0, 1);
                    if (t <= 0)
                    {
                        return false;
                    }

                    var u = 1 - t;
                    m =
                    [
                        0.393f + 0.607f * u, 0.769f - 0.769f * u, 0.189f - 0.189f * u, 0,
                        0.349f - 0.349f * u, 0.686f + 0.314f * u, 0.168f - 0.168f * u, 0,
                        0.272f - 0.272f * u, 0.534f - 0.534f * u, 0.131f + 0.869f * u, 0,
                        0, 0, 0, 1,
                    ];
                    return true;
                }

            case FilterFunctionKind.HueRotate:
                {
                    var deg = (float)f.Amount;
                    if (deg == 0)
                    {
                        return false;
                    }

                    var rad = deg * MathF.PI / 180f;
                    var c = MathF.Cos(rad);
                    var s = MathF.Sin(rad);
                    m =
                    [
                        0.213f + c * 0.787f - s * 0.213f, 0.715f - c * 0.715f - s * 0.715f, 0.072f - c * 0.072f + s * 0.928f, 0,
                        0.213f - c * 0.213f + s * 0.143f, 0.715f + c * 0.285f + s * 0.140f, 0.072f - c * 0.072f - s * 0.283f, 0,
                        0.213f - c * 0.213f - s * 0.787f, 0.715f - c * 0.715f + s * 0.715f, 0.072f + c * 0.928f + s * 0.072f, 0,
                        0, 0, 0, 1,
                    ];
                    return true;
                }

            case FilterFunctionKind.Invert:
                {
                    var a = (float)Math.Clamp(f.Amount, 0, 1);
                    if (a <= 0)
                    {
                        return false;
                    }

                    m = Scale3(1 - 2 * a);
                    offset = [a, a, a, 0f];
                    return true;
                }

            case FilterFunctionKind.Opacity:
                {
                    var a = (float)Math.Clamp(f.Amount, 0, 1);
                    if (a >= 1)
                    {
                        return false;
                    }

                    m =
                    [
                        1, 0, 0, 0,
                        0, 1, 0, 0,
                        0, 0, 1, 0,
                        0, 0, 0, a,
                    ];
                    return true;
                }

            default:
                return false;
        }
    }

    private static float[] Scale3(float t) =>
    [
        t, 0, 0, 0,
        0, t, 0, 0,
        0, 0, t, 0,
        0, 0, 0, 1,
    ];

    // §10.2 saturate matrix, with the spec's rounded Rec.709 luminance weights.
    private static float[] SaturateMatrix(float t) =>
    [
        0.213f + 0.787f * t, 0.715f - 0.715f * t, 0.072f - 0.072f * t, 0,
        0.213f - 0.213f * t, 0.715f + 0.285f * t, 0.072f - 0.072f * t, 0,
        0.213f - 0.213f * t, 0.715f - 0.715f * t, 0.072f + 0.928f * t, 0,
        0, 0, 0, 1,
    ];

    private void CreateTarget(int width, int height, out Texture* tex, out TextureView* view)
    {
        var desc = new TextureDescriptor
        {
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        tex = _api.DeviceCreateTexture(_device, in desc);
        if (tex == null)
        {
            throw new InvalidOperationException("WebGPU filter target texture creation failed.");
        }

        view = _api.TextureCreateView(tex, (TextureViewDescriptor*)null);
        if (view == null)
        {
            _api.TextureRelease(tex);
            tex = null;
            throw new InvalidOperationException("WebGPU filter target view creation failed.");
        }
    }

    // Params uniform block: 8 vec4<f32> = 128 bytes. Layout matches the WGSL
    // struct below field-for-field.
    private const int UniformFloats = 32;

    private void EncodePass(CommandEncoder* encoder, TextureView* src, TextureView* dst, FilterPass pass)
    {
        var uniforms = new float[UniformFloats];
        uniforms[0] = pass.DirX;
        uniforms[1] = pass.DirY;
        if (pass.Matrix is { } m)
        {
            Array.Copy(m, 0, uniforms, 4, 16);
        }

        if (pass.Offset is { } off)
        {
            Array.Copy(off, 0, uniforms, 20, 4);
        }

        uniforms[24] = pass.Radius;
        uniforms[25] = pass.Sigma;
        uniforms[28] = pass.UseMatrix ? 1 : 0;
        uniforms[29] = pass.PremultiplyInput ? 1 : 0;
        uniforms[30] = pass.UnpremultiplyOutput ? 1 : 0;

        var bufDesc = new BufferDescriptor
        {
            Usage = BufferUsage.Uniform | BufferUsage.CopyDst,
            Size = UniformFloats * sizeof(float),
            MappedAtCreation = false,
        };
        var buffer = _api.DeviceCreateBuffer(_device, in bufDesc);
        fixed (float* p = uniforms)
        {
            _api.QueueWriteBuffer(_queue, buffer, 0, p, UniformFloats * sizeof(float));
        }

        var entries = stackalloc BindGroupEntry[3];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = src };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = _sampler };
        entries[2] = new BindGroupEntry { Binding = 2, Buffer = buffer, Offset = 0, Size = UniformFloats * sizeof(float) };
        var bgDesc = new BindGroupDescriptor { Layout = _bindLayout, EntryCount = 3, Entries = entries };
        var bindGroup = _api.DeviceCreateBindGroup(_device, in bgDesc);

        var colorAttachment = new RenderPassColorAttachment
        {
            View = dst,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Color { R = 0, G = 0, B = 0, A = 0 },
        };
        var passDesc = new RenderPassDescriptor { ColorAttachmentCount = 1, ColorAttachments = &colorAttachment };
        var rp = _api.CommandEncoderBeginRenderPass(encoder, in passDesc);
        _api.RenderPassEncoderSetPipeline(rp, _pipeline);
        _api.RenderPassEncoderSetBindGroup(rp, 0, bindGroup, 0, (uint*)null);
        _api.RenderPassEncoderDraw(rp, 3, 1, 0, 0);
        _api.RenderPassEncoderEnd(rp);
        _api.RenderPassEncoderRelease(rp);

        // Safe to drop the refs now: wgpu keeps the resources alive until the
        // submitted command buffer has executed.
        _api.BindGroupRelease(bindGroup);
        _api.BufferRelease(buffer);
    }

    private void BuildPipeline()
    {
        var entries = stackalloc BindGroupLayoutEntry[3];
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
                Multisampled = false,
            },
        };
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering },
        };
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Fragment,
            Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = UniformFloats * sizeof(float) },
        };
        var bglDesc = new BindGroupLayoutDescriptor { EntryCount = 3, Entries = entries };
        _bindLayout = _api.DeviceCreateBindGroupLayout(_device, in bglDesc);

        var bgl = _bindLayout;
        var plDesc = new PipelineLayoutDescriptor { BindGroupLayoutCount = 1, BindGroupLayouts = &bgl };
        _pipelineLayout = _api.DeviceCreatePipelineLayout(_device, in plDesc);

        // Clamp-to-edge replicates the surface's transparent halo margin, which
        // is the spec's "transparent outside the group" for a padded raster.
        var samplerDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0,
            LodMaxClamp = 1,
            MaxAnisotropy = 1,
        };
        _sampler = _api.DeviceCreateSampler(_device, in samplerDesc);

        var codePtr = (byte*)SilkMarshal.StringToPtr(ShaderSource, NativeStringEncoding.UTF8);
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = codePtr,
            };
            var shaderDesc = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
            _shader = _api.DeviceCreateShaderModule(_device, in shaderDesc);
        }
        finally
        {
            SilkMarshal.Free((nint)codePtr);
        }

        var vsEntry = (byte*)SilkMarshal.StringToPtr("vs_filter", NativeStringEncoding.UTF8);
        var fsEntry = (byte*)SilkMarshal.StringToPtr("fs_filter", NativeStringEncoding.UTF8);
        try
        {
            // Replace, not blend: each pass overwrites its whole target.
            var colorTarget = new ColorTargetState { Format = TextureFormat.Rgba8Unorm, Blend = null, WriteMask = ColorWriteMask.All };
            var fragment = new FragmentState { Module = _shader, EntryPoint = fsEntry, TargetCount = 1, Targets = &colorTarget };
            var pipelineDesc = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState { Module = _shader, EntryPoint = vsEntry, BufferCount = 0, Buffers = null },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = ~0u, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            _pipeline = _api.DeviceCreateRenderPipeline(_device, in pipelineDesc);
            if (_pipeline == null)
            {
                throw new InvalidOperationException("WebGPU filter pipeline creation failed.");
            }
        }
        finally
        {
            SilkMarshal.Free((nint)vsEntry);
            SilkMarshal.Free((nint)fsEntry);
        }
    }

    /// <summary>Releases a filter output texture once the cache evicts it.</summary>
    private sealed unsafe class OwnedTexture(WebGPU api, nint texture, nint view) : IDisposable
    {
        private nint _texture = texture;
        private nint _view = view;

        public void Dispose()
        {
            if (_view != 0)
            {
                api.TextureViewRelease((TextureView*)_view);
                _view = 0;
            }

            if (_texture != 0)
            {
                api.TextureRelease((Texture*)_texture);
                _texture = 0;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_pipeline != null) { _api.RenderPipelineRelease(_pipeline); _pipeline = null; }
        if (_shader != null) { _api.ShaderModuleRelease(_shader); _shader = null; }
        if (_sampler != null) { _api.SamplerRelease(_sampler); _sampler = null; }
        if (_pipelineLayout != null) { _api.PipelineLayoutRelease(_pipelineLayout); _pipelineLayout = null; }
        if (_bindLayout != null) { _api.BindGroupLayoutRelease(_bindLayout); _bindLayout = null; }
    }

    // One pass = fullscreen triangle, then per-texel: optional separable
    // Gaussian step (premultiplied space), optional straight-alpha colour
    // matrix, optional final un-premultiply. textureSampleLevel (not
    // textureSample) keeps the loop free of derivative/uniformity rules.
    private const string ShaderSource = @"
struct VsOut {
    @builtin(position) pos : vec4<f32>,
    @location(0) uv : vec2<f32>,
};

@vertex
fn vs_filter(@builtin(vertex_index) vi : u32) -> VsOut {
    var corners = array<vec2<f32>, 3>(
        vec2<f32>(-1.0, 3.0),
        vec2<f32>(-1.0, -1.0),
        vec2<f32>(3.0, -1.0));
    let p = corners[vi];
    var o : VsOut;
    o.pos = vec4<f32>(p, 0.0, 1.0);
    o.uv = vec2<f32>((p.x + 1.0) * 0.5, (1.0 - p.y) * 0.5);
    return o;
}

struct Params {
    dir : vec4<f32>,      // xy: blur step in UV units
    m0 : vec4<f32>,       // colour matrix rows (straight-alpha space)
    m1 : vec4<f32>,
    m2 : vec4<f32>,
    m3 : vec4<f32>,
    offset : vec4<f32>,
    blur : vec4<f32>,     // x: radius (taps each side), y: sigma
    flags : vec4<f32>,    // x: useMatrix, y: premultiplyInput, z: unpremultiplyOutput
};

@group(0) @binding(0) var tex : texture_2d<f32>;
@group(0) @binding(1) var smp : sampler;
@group(0) @binding(2) var<uniform> params : Params;

fn load_tap(uv : vec2<f32>) -> vec4<f32> {
    var c = textureSampleLevel(tex, smp, uv, 0.0);
    if (params.flags.y > 0.5) {
        c = vec4<f32>(c.rgb * c.a, c.a);
    }
    return c;
}

@fragment
fn fs_filter(in : VsOut) -> @location(0) vec4<f32> {
    var c : vec4<f32>;
    let radius = i32(params.blur.x);
    if (radius > 0) {
        let sigma = max(params.blur.y, 0.001);
        var acc = vec4<f32>(0.0);
        var wsum = 0.0;
        for (var i = -radius; i <= radius; i = i + 1) {
            let w = exp(-0.5 * f32(i * i) / (sigma * sigma));
            acc = acc + load_tap(in.uv + params.dir.xy * f32(i)) * w;
            wsum = wsum + w;
        }
        c = acc / wsum;
    } else {
        c = load_tap(in.uv);
    }
    if (params.flags.x > 0.5) {
        let a = max(c.a, 0.00001);
        let straight = vec4<f32>(c.rgb / a, c.a);
        let fr = dot(straight, params.m0) + params.offset.x;
        let fg = dot(straight, params.m1) + params.offset.y;
        let fb = dot(straight, params.m2) + params.offset.z;
        let fa = dot(straight, params.m3) + params.offset.w;
        let clamped = clamp(vec4<f32>(fr, fg, fb, fa), vec4<f32>(0.0), vec4<f32>(1.0));
        c = vec4<f32>(clamped.rgb * clamped.a, clamped.a);
    }
    if (params.flags.z > 0.5) {
        let a = max(c.a, 0.00001);
        c = vec4<f32>(c.rgb / a, c.a);
    }
    return c;
}
";
}
