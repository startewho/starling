// SPDX-License-Identifier: Apache-2.0
using Starling.Layout.Text;

namespace Starling.Paint.Backend;

/// <summary>
/// <see cref="IPaintBackendFactory"/> for the ImageSharp.Drawing backend. The
/// CPU and WebGPU variants differ only by the <c>useWebGpu</c> flag.
/// </summary>
internal sealed class ImageSharpPaintBackendFactory : IPaintBackendFactory
{
    private readonly bool _useWebGpu;

    public ImageSharpPaintBackendFactory(bool useWebGpu) => _useWebGpu = useWebGpu;

    public PaintBackendKind Kind
        => _useWebGpu ? PaintBackendKind.ImageSharpWebGpu : PaintBackendKind.ImageSharp;

    public IPaintBackend CreateBackend(FontResolver fonts, FontFaceRegistry? webFonts)
        => new ImageSharpBackend(fonts, webFonts, useWebGpu: _useWebGpu);

    public ITextMeasurer CreateMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
        => new ImageSharpTextMeasurer(fonts, webFonts);
}
