using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Starling.Css.Values;
using Starling.Paint.Backend;
using Rect = Starling.Layout.Rect;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Starling.Paint.Compositor;

/// <summary>
/// A surface overlay scene mapped into device pixels for one frame.
/// </summary>
internal readonly record struct GpuOverlayLayer(
    SurfaceOverlayScene Scene,
    Matrix2D SceneToDevice,
    float Opacity,
    Rect? ClipDevice);

/// <summary>
/// Draws <see cref="SurfaceOverlayLayer"/> scenes into an active WebGPU render
/// pass. The renderer turns scene commands into short-lived vertex buffers and
/// draws them after the page layers.
/// </summary>
/// <remarks>
/// Solid polygons and flattened paths use a triangle pipeline. Ellipses use a
/// separate shader that gives the edge smooth coverage in the fragment stage.
/// The caller owns what the scene means. This class only owns how those commands
/// become GPU geometry.
/// </remarks>
internal sealed unsafe class GpuOverlayRenderer : IDisposable
{
    private const int SolidFloatsPerVertex = 6;
    private const int EllipseFloatsPerVertex = 8;
    private const uint SolidVertexStride = SolidFloatsPerVertex * sizeof(float);
    private const uint EllipseVertexStride = EllipseFloatsPerVertex * sizeof(float);
    private const double CurveTolerance = 1.0;

    private readonly GpuBlendEngine _engine;
    private ShaderModule* _shader;
    private PipelineLayout* _pipelineLayout;
    private readonly Dictionary<TextureFormat, nint> _solidPipelines = new();
    private readonly Dictionary<TextureFormat, nint> _ellipsePipelines = new();
    private WgpuBuffer* _solidBuffer;
    private WgpuBuffer* _ellipseBuffer;
    private nuint _solidCapacity;
    private nuint _ellipseCapacity;
    private bool _disposed;

    internal GpuOverlayRenderer(GpuBlendEngine engine)
    {
        _engine = engine;
        BuildLayout();
    }

    /// <summary>
    /// Records all overlay layers into <paramref name="pass"/>. The target size
    /// is in device pixels. Empty, clipped, and fully transparent layers are
    /// skipped.
    /// </summary>
    internal void Record(
        RenderPassEncoder* pass,
        IReadOnlyList<GpuOverlayLayer>? layers,
        int targetWidth,
        int targetHeight,
        TextureFormat format)
    {
        if (layers is not { Count: > 0 })
        {
            return;
        }

        var api = _engine.Api;
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer.Opacity <= 0 || layer.Scene.Commands.Count == 0)
            {
                continue;
            }

            if (!TrySetScissor(pass, layer.ClipDevice, targetWidth, targetHeight))
            {
                continue;
            }

            var solid = new List<float>();
            var ellipses = new List<float>();
            BuildLayer(layer, targetWidth, targetHeight, solid, ellipses);

            if (solid.Count > 0)
            {
                var byteLen = (nuint)(solid.Count * sizeof(float));
                EnsureBuffer(ref _solidBuffer, ref _solidCapacity, byteLen);
                var data = solid.ToArray();
                fixed (float* p = data)
                {
                    api.QueueWriteBuffer(_engine.Queue, _solidBuffer, 0, p, byteLen);
                }

                api.RenderPassEncoderSetPipeline(pass, PipelineFor(format, analyticEllipse: false));
                api.RenderPassEncoderSetVertexBuffer(pass, 0, _solidBuffer, 0, _solidCapacity);
                api.RenderPassEncoderDraw(pass, (uint)(solid.Count / SolidFloatsPerVertex), 1, 0, 0);
            }

            if (ellipses.Count > 0)
            {
                var byteLen = (nuint)(ellipses.Count * sizeof(float));
                EnsureBuffer(ref _ellipseBuffer, ref _ellipseCapacity, byteLen);
                var data = ellipses.ToArray();
                fixed (float* p = data)
                {
                    api.QueueWriteBuffer(_engine.Queue, _ellipseBuffer, 0, p, byteLen);
                }

                api.RenderPassEncoderSetPipeline(pass, PipelineFor(format, analyticEllipse: true));
                api.RenderPassEncoderSetVertexBuffer(pass, 0, _ellipseBuffer, 0, _ellipseCapacity);
                api.RenderPassEncoderDraw(pass, (uint)(ellipses.Count / EllipseFloatsPerVertex), 1, 0, 0);
            }
        }
    }

    /// <summary>
    /// Creates the shared pipeline layout and shader module.
    /// </summary>
    private void BuildLayout()
    {
        var plDesc = new PipelineLayoutDescriptor();
        _pipelineLayout = _engine.Api.DeviceCreatePipelineLayout(_engine.Device, in plDesc);

        var codePtr = (byte*)SilkMarshal.StringToPtr(ShaderSource, NativeStringEncoding.UTF8);
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = codePtr,
            };
            var shaderDesc = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
            _shader = _engine.Api.DeviceCreateShaderModule(_engine.Device, in shaderDesc);
        }
        finally
        {
            SilkMarshal.Free((nint)codePtr);
        }
    }

    /// <summary>
    /// Gets the render pipeline for the target format and geometry kind.
    /// </summary>
    private RenderPipeline* PipelineFor(TextureFormat format, bool analyticEllipse)
    {
        var cache = analyticEllipse ? _ellipsePipelines : _solidPipelines;
        if (cache.TryGetValue(format, out var cached))
        {
            return (RenderPipeline*)cached;
        }

        var vsName = analyticEllipse ? "vs_ellipse" : "vs_solid";
        var fsName = analyticEllipse ? "fs_ellipse" : "fs_solid";
        var vsEntry = (byte*)SilkMarshal.StringToPtr(vsName, NativeStringEncoding.UTF8);
        var fsEntry = (byte*)SilkMarshal.StringToPtr(fsName, NativeStringEncoding.UTF8);
        try
        {
            var attrs = stackalloc VertexAttribute[3];
            attrs[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
            attrs[1] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 2 * sizeof(float), ShaderLocation = 1 };
            attrs[2] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 4 * sizeof(float), ShaderLocation = 2 };

            var solidAttrs = stackalloc VertexAttribute[2];
            solidAttrs[0] = new VertexAttribute { Format = VertexFormat.Float32x2, Offset = 0, ShaderLocation = 0 };
            solidAttrs[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 2 * sizeof(float), ShaderLocation = 1 };

            var vbl = analyticEllipse
                ? new VertexBufferLayout
                {
                    ArrayStride = EllipseVertexStride,
                    StepMode = VertexStepMode.Vertex,
                    AttributeCount = 3,
                    Attributes = attrs,
                }
                : new VertexBufferLayout
                {
                    ArrayStride = SolidVertexStride,
                    StepMode = VertexStepMode.Vertex,
                    AttributeCount = 2,
                    Attributes = solidAttrs,
                };

            var blend = new BlendState
            {
                Color = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
                Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            };
            var colorTarget = new ColorTargetState { Format = format, Blend = &blend, WriteMask = ColorWriteMask.All };
            var fragment = new FragmentState { Module = _shader, EntryPoint = fsEntry, TargetCount = 1, Targets = &colorTarget };
            var pipelineDesc = new RenderPipelineDescriptor
            {
                Layout = _pipelineLayout,
                Vertex = new VertexState { Module = _shader, EntryPoint = vsEntry, BufferCount = 1, Buffers = &vbl },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = ~0u, AlphaToCoverageEnabled = false },
                Fragment = &fragment,
            };
            var pipeline = _engine.Api.DeviceCreateRenderPipeline(_engine.Device, in pipelineDesc);
            if (pipeline == null)
            {
                throw new InvalidOperationException("WebGPU overlay render pipeline creation failed.");
            }

            cache[format] = (nint)pipeline;
            return pipeline;
        }
        finally
        {
            SilkMarshal.Free((nint)vsEntry);
            SilkMarshal.Free((nint)fsEntry);
        }
    }

    /// <summary>
    /// Converts one scene into solid-triangle vertices and ellipse vertices.
    /// </summary>
    private void BuildLayer(
        GpuOverlayLayer layer,
        int targetWidth,
        int targetHeight,
        List<float> solid,
        List<float> ellipses)
    {
        foreach (var command in layer.Scene.Commands)
        {
            switch (command)
            {
                case SurfaceOverlayFillEllipse ellipse:
                    AddEllipse(
                        ellipses,
                        layer.SceneToDevice,
                        ellipse.Cx,
                        ellipse.Cy,
                        ellipse.Rx,
                        ellipse.Ry,
                        ellipse.Color,
                        layer.Opacity,
                        targetWidth,
                        targetHeight);
                    break;
                case SurfaceOverlayFillPolygon polygon:
                    AddPolygon(solid, layer.SceneToDevice, polygon.Points, polygon.Color, layer.Opacity, targetWidth, targetHeight);
                    break;
                case SurfaceOverlayFillPath path:
                    AddFillPath(solid, layer.SceneToDevice, path.Path, path.Color, layer.Opacity, targetWidth, targetHeight);
                    break;
                case SurfaceOverlayStrokePath stroke:
                    AddStrokePath(solid, ellipses, layer.SceneToDevice, stroke, layer.Opacity, targetWidth, targetHeight);
                    break;
                case SurfaceOverlayFillInstances instances:
                    AddInstances(solid, ellipses, layer.SceneToDevice, instances, layer.Opacity, targetWidth, targetHeight);
                    break;
            }
        }
    }

    /// <summary>
    /// Expands an instance command into the current vertex streams.
    /// </summary>
    private static void AddInstances(
        List<float> solid,
        List<float> ellipses,
        Matrix2D sceneToDevice,
        SurfaceOverlayFillInstances command,
        float layerOpacity,
        int targetWidth,
        int targetHeight)
    {
        foreach (var instance in command.Instances)
        {
            if (instance.W <= 0 || instance.H <= 0 || instance.Color.A == 0 || instance.Opacity <= 0)
            {
                continue;
            }

            var cx = instance.X + instance.W / 2;
            var cy = instance.Y + instance.H / 2;
            var localToScene = Matrix2D.Translate(cx, cy)
                .Multiply(Matrix2D.Rotate(instance.RotationRadians))
                .Multiply(Matrix2D.Translate(-cx, -cy));
            var matrix = sceneToDevice.Multiply(localToScene);
            var opacity = layerOpacity * instance.Opacity;

            switch (command.Primitive)
            {
                case SurfaceOverlayPrimitive.Circle:
                    AddEllipse(ellipses, matrix, cx, cy, instance.W / 2, instance.H / 2,
                        instance.Color, opacity, targetWidth, targetHeight);
                    break;
                case SurfaceOverlayPrimitive.Triangle:
                    AddPolygon(solid, matrix, Triangle(instance), instance.Color, opacity, targetWidth, targetHeight);
                    break;
                case SurfaceOverlayPrimitive.Diamond:
                    AddPolygon(solid, matrix, Diamond(instance), instance.Color, opacity, targetWidth, targetHeight);
                    break;
                case SurfaceOverlayPrimitive.Star:
                    AddPolygon(solid, matrix, Star(instance), instance.Color, opacity, targetWidth, targetHeight);
                    break;
                default:
                    AddSolidQuad(solid, matrix, instance.X, instance.Y, instance.W, instance.H,
                        instance.Color, opacity, targetWidth, targetHeight);
                    break;
            }
        }
    }

    private static SurfaceOverlayPoint[] Triangle(SurfaceOverlayInstance i)
        =>
        [
            new(i.X + i.W / 2, i.Y),
            new(i.X + i.W, i.Y + i.H),
            new(i.X, i.Y + i.H),
        ];

    private static SurfaceOverlayPoint[] Diamond(SurfaceOverlayInstance i)
        =>
        [
            new(i.X + i.W / 2, i.Y),
            new(i.X + i.W, i.Y + i.H / 2),
            new(i.X + i.W / 2, i.Y + i.H),
            new(i.X, i.Y + i.H / 2),
        ];

    private static SurfaceOverlayPoint[] Star(SurfaceOverlayInstance i)
    {
        var points = new SurfaceOverlayPoint[10];
        var cx = i.X + i.W / 2;
        var cy = i.Y + i.H / 2;
        var outerX = i.W / 2;
        var outerY = i.H / 2;
        var innerX = outerX * 0.5;
        var innerY = outerY * 0.5;
        for (var p = 0; p < points.Length; p++)
        {
            var angle = -Math.PI / 2 + p * Math.PI / 5;
            var rx = (p & 1) == 0 ? outerX : innerX;
            var ry = (p & 1) == 0 ? outerY : innerY;
            points[p] = new SurfaceOverlayPoint(cx + Math.Cos(angle) * rx, cy + Math.Sin(angle) * ry);
        }

        return points;
    }

    /// <summary>
    /// Flattens each path contour and fills it as triangles.
    /// </summary>
    private static void AddFillPath(
        List<float> solid,
        Matrix2D matrix,
        SurfaceOverlayPath path,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        foreach (var contour in Flatten(path))
        {
            if (contour.Points.Count >= 3)
            {
                AddPolygon(solid, matrix, contour.Points, color, opacity, targetWidth, targetHeight);
            }
        }
    }

    /// <summary>
    /// Builds stroke geometry from flattened path segments.
    /// </summary>
    private static void AddStrokePath(
        List<float> solid,
        List<float> ellipses,
        Matrix2D matrix,
        SurfaceOverlayStrokePath stroke,
        float layerOpacity,
        int targetWidth,
        int targetHeight)
    {
        if (stroke.Width <= 0 || stroke.Color.A == 0)
        {
            return;
        }

        foreach (var contour in Flatten(stroke.Path))
        {
            var pts = contour.Points;
            if (pts.Count < 2)
            {
                continue;
            }

            var half = stroke.Width / 2;
            var segmentCount = contour.Closed ? pts.Count : pts.Count - 1;
            for (var i = 0; i < segmentCount; i++)
            {
                var p0 = pts[i];
                var p1 = pts[(i + 1) % pts.Count];
                AddStrokeSegment(solid, matrix, p0, p1, half, stroke.Color, layerOpacity, targetWidth, targetHeight);
            }

            if (stroke.Join == SurfaceOverlayLineJoin.Round)
            {
                var jointCount = contour.Closed ? pts.Count : pts.Count - 2;
                var start = contour.Closed ? 0 : 1;
                for (var i = 0; i < jointCount; i++)
                {
                    var p = pts[start + i];
                    AddEllipse(ellipses, matrix, p.X, p.Y, half, half, stroke.Color, layerOpacity, targetWidth, targetHeight);
                }
            }

            if (!contour.Closed && stroke.Cap == SurfaceOverlayLineCap.Round)
            {
                AddEllipse(ellipses, matrix, pts[0].X, pts[0].Y, half, half, stroke.Color, layerOpacity, targetWidth, targetHeight);
                AddEllipse(ellipses, matrix, pts[^1].X, pts[^1].Y, half, half, stroke.Color, layerOpacity, targetWidth, targetHeight);
            }
        }
    }

    private static void AddStrokeSegment(
        List<float> solid,
        Matrix2D matrix,
        SurfaceOverlayPoint p0,
        SurfaceOverlayPoint p1,
        double halfWidth,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= 0.0001)
        {
            return;
        }

        var nx = -dy / len * halfWidth;
        var ny = dx / len * halfWidth;
        var a = new SurfaceOverlayPoint(p0.X + nx, p0.Y + ny);
        var b = new SurfaceOverlayPoint(p1.X + nx, p1.Y + ny);
        var c = new SurfaceOverlayPoint(p1.X - nx, p1.Y - ny);
        var d = new SurfaceOverlayPoint(p0.X - nx, p0.Y - ny);
        AddTriangle(solid, matrix, a, b, c, color, opacity, targetWidth, targetHeight);
        AddTriangle(solid, matrix, a, c, d, color, opacity, targetWidth, targetHeight);
    }

    /// <summary>
    /// Adds two triangles for a filled rectangle.
    /// </summary>
    private static void AddSolidQuad(
        List<float> solid,
        Matrix2D matrix,
        double x,
        double y,
        double w,
        double h,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        var p0 = new SurfaceOverlayPoint(x, y);
        var p1 = new SurfaceOverlayPoint(x + w, y);
        var p2 = new SurfaceOverlayPoint(x + w, y + h);
        var p3 = new SurfaceOverlayPoint(x, y + h);
        AddTriangle(solid, matrix, p0, p1, p2, color, opacity, targetWidth, targetHeight);
        AddTriangle(solid, matrix, p0, p2, p3, color, opacity, targetWidth, targetHeight);
    }

    /// <summary>
    /// Adds a quad that the ellipse shader clips to a smooth ellipse.
    /// </summary>
    private static void AddEllipse(
        List<float> ellipses,
        Matrix2D matrix,
        double cx,
        double cy,
        double rx,
        double ry,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        if (rx <= 0 || ry <= 0 || color.A == 0 || opacity <= 0)
        {
            return;
        }

        var p0 = new SurfaceOverlayPoint(cx - rx, cy - ry);
        var p1 = new SurfaceOverlayPoint(cx + rx, cy - ry);
        var p2 = new SurfaceOverlayPoint(cx + rx, cy + ry);
        var p3 = new SurfaceOverlayPoint(cx - rx, cy + ry);
        AddEllipseVertex(ellipses, matrix, p0, -1, -1, color, opacity, targetWidth, targetHeight);
        AddEllipseVertex(ellipses, matrix, p1, 1, -1, color, opacity, targetWidth, targetHeight);
        AddEllipseVertex(ellipses, matrix, p2, 1, 1, color, opacity, targetWidth, targetHeight);
        AddEllipseVertex(ellipses, matrix, p0, -1, -1, color, opacity, targetWidth, targetHeight);
        AddEllipseVertex(ellipses, matrix, p2, 1, 1, color, opacity, targetWidth, targetHeight);
        AddEllipseVertex(ellipses, matrix, p3, -1, 1, color, opacity, targetWidth, targetHeight);
    }

    /// <summary>
    /// Triangulates and adds one filled polygon.
    /// </summary>
    private static void AddPolygon(
        List<float> solid,
        Matrix2D matrix,
        IReadOnlyList<SurfaceOverlayPoint> points,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        if (points.Count < 3 || color.A == 0 || opacity <= 0)
        {
            return;
        }

        var clean = Clean(points);
        if (clean.Count < 3)
        {
            return;
        }

        foreach (var (a, b, c) in Triangulate(clean))
        {
            AddTriangle(solid, matrix, clean[a], clean[b], clean[c], color, opacity, targetWidth, targetHeight);
        }
    }

    private static List<SurfaceOverlayPoint> Clean(IReadOnlyList<SurfaceOverlayPoint> points)
    {
        var clean = new List<SurfaceOverlayPoint>(points.Count);
        foreach (var p in points)
        {
            if (clean.Count == 0 || DistanceSquared(clean[^1], p) > 0.000001)
            {
                clean.Add(p);
            }
        }

        if (clean.Count > 1 && DistanceSquared(clean[0], clean[^1]) <= 0.000001)
        {
            clean.RemoveAt(clean.Count - 1);
        }

        return clean;
    }

    private static List<(int A, int B, int C)> Triangulate(List<SurfaceOverlayPoint> points)
    {
        var triangles = new List<(int A, int B, int C)>();
        var remaining = Enumerable.Range(0, points.Count).ToList();
        var ccw = SignedArea(points) > 0;
        var guard = points.Count * points.Count;
        while (remaining.Count > 3 && guard-- > 0)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count; i++)
            {
                var prev = remaining[(i + remaining.Count - 1) % remaining.Count];
                var curr = remaining[i];
                var next = remaining[(i + 1) % remaining.Count];
                if (!IsEar(points, remaining, prev, curr, next, ccw))
                {
                    continue;
                }

                triangles.Add((prev, curr, next));
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped)
            {
                break;
            }
        }

        if (remaining.Count == 3)
        {
            triangles.Add((remaining[0], remaining[1], remaining[2]));
        }

        return triangles;
    }

    private static bool IsEar(
        List<SurfaceOverlayPoint> points,
        List<int> remaining,
        int prev,
        int curr,
        int next,
        bool ccw)
    {
        var cross = Cross(points[prev], points[curr], points[next]);
        if (ccw ? cross <= 0 : cross >= 0)
        {
            return false;
        }

        foreach (var index in remaining)
        {
            if (index == prev || index == curr || index == next)
            {
                continue;
            }

            if (PointInTriangle(points[index], points[prev], points[curr], points[next]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PointInTriangle(
        SurfaceOverlayPoint p,
        SurfaceOverlayPoint a,
        SurfaceOverlayPoint b,
        SurfaceOverlayPoint c)
    {
        var c1 = Cross(p, a, b);
        var c2 = Cross(p, b, c);
        var c3 = Cross(p, c, a);
        var hasNeg = c1 < 0 || c2 < 0 || c3 < 0;
        var hasPos = c1 > 0 || c2 > 0 || c3 > 0;
        return !(hasNeg && hasPos);
    }

    private static double SignedArea(List<SurfaceOverlayPoint> points)
    {
        var area = 0d;
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            area += a.X * b.Y - b.X * a.Y;
        }

        return area * 0.5;
    }

    private static double Cross(SurfaceOverlayPoint a, SurfaceOverlayPoint b, SurfaceOverlayPoint c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static double DistanceSquared(SurfaceOverlayPoint a, SurfaceOverlayPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    private static void AddTriangle(
        List<float> solid,
        Matrix2D matrix,
        SurfaceOverlayPoint a,
        SurfaceOverlayPoint b,
        SurfaceOverlayPoint c,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        AddSolidVertex(solid, matrix, a, color, opacity, targetWidth, targetHeight);
        AddSolidVertex(solid, matrix, b, color, opacity, targetWidth, targetHeight);
        AddSolidVertex(solid, matrix, c, color, opacity, targetWidth, targetHeight);
    }

    private static void AddSolidVertex(
        List<float> solid,
        Matrix2D matrix,
        SurfaceOverlayPoint p,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        var (x, y) = matrix.Transform(p.X, p.Y);
        var (nx, ny) = ToNdc(x, y, targetWidth, targetHeight);
        var (r, g, b, a) = Premul(color, opacity);
        solid.Add(nx);
        solid.Add(ny);
        solid.Add(r);
        solid.Add(g);
        solid.Add(b);
        solid.Add(a);
    }

    private static void AddEllipseVertex(
        List<float> ellipses,
        Matrix2D matrix,
        SurfaceOverlayPoint p,
        float localX,
        float localY,
        CssColor color,
        float opacity,
        int targetWidth,
        int targetHeight)
    {
        var (x, y) = matrix.Transform(p.X, p.Y);
        var (nx, ny) = ToNdc(x, y, targetWidth, targetHeight);
        var (r, g, b, a) = Premul(color, opacity);
        ellipses.Add(nx);
        ellipses.Add(ny);
        ellipses.Add(localX);
        ellipses.Add(localY);
        ellipses.Add(r);
        ellipses.Add(g);
        ellipses.Add(b);
        ellipses.Add(a);
    }

    private static (float X, float Y) ToNdc(double x, double y, int width, int height)
        => ((float)(x / width * 2.0 - 1.0), (float)(1.0 - y / height * 2.0));

    private static (float R, float G, float B, float A) Premul(CssColor color, float opacity)
    {
        var a = color.A / 255f * Math.Clamp(opacity, 0f, 1f);
        return (color.R / 255f * a, color.G / 255f * a, color.B / 255f * a, a);
    }

    private static List<FlattenedContour> Flatten(SurfaceOverlayPath path)
    {
        var contours = new List<FlattenedContour>();
        List<SurfaceOverlayPoint>? current = null;
        var currentPoint = new SurfaceOverlayPoint();
        var startPoint = new SurfaceOverlayPoint();

        void Finish(bool closed)
        {
            if (current is { Count: > 1 })
            {
                contours.Add(new FlattenedContour(current, closed));
            }

            current = null;
        }

        void EnsureCurrent()
        {
            current ??= [currentPoint];
        }

        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case SurfaceOverlayMoveTo move:
                    Finish(closed: false);
                    currentPoint = new SurfaceOverlayPoint(move.X, move.Y);
                    startPoint = currentPoint;
                    current = [currentPoint];
                    break;
                case SurfaceOverlayLineTo line:
                    EnsureCurrent();
                    currentPoint = new SurfaceOverlayPoint(line.X, line.Y);
                    current!.Add(currentPoint);
                    break;
                case SurfaceOverlayQuadraticCurveTo quad:
                    EnsureCurrent();
                    AddQuadratic(current!, currentPoint, quad);
                    currentPoint = new SurfaceOverlayPoint(quad.X, quad.Y);
                    break;
                case SurfaceOverlayCubicCurveTo cubic:
                    EnsureCurrent();
                    AddCubic(current!, currentPoint, cubic);
                    currentPoint = new SurfaceOverlayPoint(cubic.X, cubic.Y);
                    break;
                case SurfaceOverlayClosePath:
                    if (current is { Count: > 1 } && DistanceSquared(current[^1], startPoint) > 0.000001)
                    {
                        current.Add(startPoint);
                    }

                    Finish(closed: true);
                    currentPoint = startPoint;
                    break;
            }
        }

        Finish(closed: false);
        return contours;
    }

    private static void AddQuadratic(
        List<SurfaceOverlayPoint> points,
        SurfaceOverlayPoint p0,
        SurfaceOverlayQuadraticCurveTo q)
    {
        var approxLength =
            Math.Sqrt(Math.Pow(q.Cx - p0.X, 2) + Math.Pow(q.Cy - p0.Y, 2)) +
            Math.Sqrt(Math.Pow(q.X - q.Cx, 2) + Math.Pow(q.Y - q.Cy, 2));
        var segments = Math.Max(4, (int)Math.Ceiling(approxLength / CurveTolerance));
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var mt = 1 - t;
            points.Add(new SurfaceOverlayPoint(
                mt * mt * p0.X + 2 * mt * t * q.Cx + t * t * q.X,
                mt * mt * p0.Y + 2 * mt * t * q.Cy + t * t * q.Y));
        }
    }

    private static void AddCubic(
        List<SurfaceOverlayPoint> points,
        SurfaceOverlayPoint p0,
        SurfaceOverlayCubicCurveTo c)
    {
        var approxLength =
            Math.Sqrt(Math.Pow(c.C1x - p0.X, 2) + Math.Pow(c.C1y - p0.Y, 2)) +
            Math.Sqrt(Math.Pow(c.C2x - c.C1x, 2) + Math.Pow(c.C2y - c.C1y, 2)) +
            Math.Sqrt(Math.Pow(c.X - c.C2x, 2) + Math.Pow(c.Y - c.C2y, 2));
        var segments = Math.Max(4, (int)Math.Ceiling(approxLength / CurveTolerance));
        for (var i = 1; i <= segments; i++)
        {
            var t = i / (double)segments;
            var mt = 1 - t;
            points.Add(new SurfaceOverlayPoint(
                mt * mt * mt * p0.X +
                3 * mt * mt * t * c.C1x +
                3 * mt * t * t * c.C2x +
                t * t * t * c.X,
                mt * mt * mt * p0.Y +
                3 * mt * mt * t * c.C1y +
                3 * mt * t * t * c.C2y +
                t * t * t * c.Y));
        }
    }

    /// <summary>
    /// Sets the scissor for one overlay layer. Returns <c>false</c> when the clip
    /// is outside the target.
    /// </summary>
    private bool TrySetScissor(RenderPassEncoder* pass, Rect? clip, int width, int height)
    {
        var x = 0;
        var y = 0;
        var w = width;
        var h = height;
        if (clip is { } cd)
        {
            var minX = Math.Max(0, (int)Math.Floor(cd.X));
            var minY = Math.Max(0, (int)Math.Floor(cd.Y));
            var maxX = Math.Min(width, (int)Math.Ceiling(cd.Right));
            var maxY = Math.Min(height, (int)Math.Ceiling(cd.Bottom));
            if (maxX <= minX || maxY <= minY)
            {
                return false;
            }

            x = minX;
            y = minY;
            w = maxX - minX;
            h = maxY - minY;
        }

        _engine.Api.RenderPassEncoderSetScissorRect(pass, (uint)x, (uint)y, (uint)w, (uint)h);
        return true;
    }

    /// <summary>
    /// Ensures a reusable vertex buffer can hold the requested bytes.
    /// </summary>
    private void EnsureBuffer(ref WgpuBuffer* buffer, ref nuint capacity, nuint byteLen)
    {
        if (buffer != null && capacity >= byteLen)
        {
            return;
        }

        if (buffer != null)
        {
            _engine.Api.BufferRelease(buffer);
        }

        capacity = (nuint)GpuBlendEngine.Align256((uint)byteLen);
        var desc = new BufferDescriptor { Usage = BufferUsage.Vertex | BufferUsage.CopyDst, Size = capacity };
        buffer = _engine.Api.DeviceCreateBuffer(_engine.Device, in desc);
        if (buffer == null)
        {
            throw new InvalidOperationException("WebGPU overlay vertex buffer creation failed.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pipeline in _solidPipelines.Values)
        {
            _engine.Api.RenderPipelineRelease((RenderPipeline*)pipeline);
        }

        foreach (var pipeline in _ellipsePipelines.Values)
        {
            _engine.Api.RenderPipelineRelease((RenderPipeline*)pipeline);
        }

        _solidPipelines.Clear();
        _ellipsePipelines.Clear();
        if (_solidBuffer != null) { _engine.Api.BufferRelease(_solidBuffer); _solidBuffer = null; }
        if (_ellipseBuffer != null) { _engine.Api.BufferRelease(_ellipseBuffer); _ellipseBuffer = null; }
        if (_shader != null) { _engine.Api.ShaderModuleRelease(_shader); _shader = null; }
        if (_pipelineLayout != null) { _engine.Api.PipelineLayoutRelease(_pipelineLayout); _pipelineLayout = null; }
    }

    private readonly record struct FlattenedContour(IReadOnlyList<SurfaceOverlayPoint> Points, bool Closed);

    private const string ShaderSource = @"
struct SolidOut {
    @builtin(position) pos : vec4<f32>,
    @location(0) color : vec4<f32>,
};

@vertex
fn vs_solid(@location(0) pos : vec2<f32>, @location(1) color : vec4<f32>) -> SolidOut {
    var o : SolidOut;
    o.pos = vec4<f32>(pos, 0.0, 1.0);
    o.color = color;
    return o;
}

@fragment
fn fs_solid(in : SolidOut) -> @location(0) vec4<f32> {
    return in.color;
}

struct EllipseOut {
    @builtin(position) pos : vec4<f32>,
    @location(0) local : vec2<f32>,
    @location(1) color : vec4<f32>,
};

@vertex
fn vs_ellipse(
    @location(0) pos : vec2<f32>,
    @location(1) local : vec2<f32>,
    @location(2) color : vec4<f32>) -> EllipseOut {
    var o : EllipseOut;
    o.pos = vec4<f32>(pos, 0.0, 1.0);
    o.local = local;
    o.color = color;
    return o;
}

@fragment
fn fs_ellipse(in : EllipseOut) -> @location(0) vec4<f32> {
    let d = length(in.local) - 1.0;
    let aa = max(fwidth(d), 0.001);
    let coverage = 1.0 - smoothstep(0.0, aa, d);
    return vec4<f32>(in.color.rgb * coverage, in.color.a * coverage);
}
";
}
