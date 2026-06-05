using BenchmarkDotNet.Attributes;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;

namespace Starling.Bench;

// Per-frame backend cost, GPU vs CPU. The shipped WebGPU backend allocates a
// fresh WebGPURenderTarget and does a synchronous GPU→CPU readback on every
// Render call (ImageSharpBackend.RenderWebGpu: new target at construction, then
// ReadbackImage + CopyPixelDataTo each frame) — there is no cross-frame target
// reuse. This bench isolates that per-frame cost on one fixed display list so
// the target-setup + readback overhead is visible, and contrasts it with the
// pure-CPU rasterizer on the identical list.
//
// Cpu_Frame is the baseline, so the GPU rows' Ratio reads as "× the CPU cost"
// for the same frame. On modest display lists the GPU path is typically slower
// (texture setup + readback dominate the small fill), which is exactly the
// shipped per-frame cost worth tracking. [Scale] is the device pixel ratio:
// 2.0 (Retina) quadruples the surface area and its readback.
[MemoryDiagnoser]
public class WebGpuFrameBench
{
    [Params(1.0f, 2.0f)]
    public float Scale;

    private static readonly Size Viewport = new(1024, 768);

    private ImageSharpBackend _gpu = null!;
    private ImageSharpBackend _cpu = null!;
    private DisplayList _list = null!;

    [GlobalSetup]
    public void Setup()
    {
        var doc = HtmlParser.Parse(Fixtures.TextHeavyParagraphs(60));
        var style = new StyleEngine();
        using var measurer = new ImageSharpTextMeasurer(FontResolver.Default);
        var root = new LayoutEngine(style, measurer).LayoutDocument(doc, Viewport);
        _list = new DisplayListBuilder().Build(root);

        _gpu = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: true);
        _cpu = new ImageSharpBackend(FontResolver.Default, webFonts: null, useWebGpu: false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _gpu.Dispose();
        _cpu.Dispose();
    }

    [Benchmark(Baseline = true)]
    public int Cpu_Frame()
    {
        using var bmp = _cpu.Render(_list, Viewport, Scale);
        return bmp.Width;
    }

    [Benchmark]
    public int WebGpu_Frame()
    {
        using var bmp = _gpu.Render(_list, Viewport, Scale);
        return bmp.Width;
    }
}
