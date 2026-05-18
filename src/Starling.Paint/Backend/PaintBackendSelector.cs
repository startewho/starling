using Tessera.Common.Diagnostics;
using Tessera.Layout.Text;

namespace Tessera.Paint.Backend;

internal enum PaintBackendKind
{
    Skia,
    ImageSharp,
    ImageSharpWebGpu,
}

/// <summary>
/// Reads <c>TESSERA_PAINT_BACKEND</c> once and dispenses the matching paint
/// backend and text measurer so layout and raster never disagree within a
/// single render. Mirrors Tessera.Codecs.NativeImageDecoder.SelectBackend.
/// </summary>
internal static class PaintBackendSelector
{
    private const string EnvVar = "TESSERA_PAINT_BACKEND";

    private static readonly Lazy<PaintBackendKind> _selected = new(ReadEnv);

    internal static PaintBackendKind Selected => _selected.Value;

    private static PaintBackendKind ReadEnv() => Parse(Environment.GetEnvironmentVariable(EnvVar));

    internal static PaintBackendKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return PaintBackendKind.Skia;

        return raw.Trim().ToLowerInvariant() switch
        {
            "skia" => PaintBackendKind.Skia,
            "imagesharp" => PaintBackendKind.ImageSharp,
            "imagesharp-webgpu" or "imagesharp-gpu" => PaintBackendKind.ImageSharpWebGpu,
            _ => throw new InvalidOperationException(
                $"{EnvVar}='{raw}' is not a recognised paint backend. Allowed values: 'skia', 'imagesharp', 'imagesharp-webgpu'."),
        };
    }

    internal static IPaintBackend Create(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diag = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Selected switch
        {
            PaintBackendKind.Skia => new SkiaGraphiteBackend(fonts, webFonts, diag),
            PaintBackendKind.ImageSharp => CreateImageSharpBackend(fonts, webFonts, diag, useWebGpu: false),
            PaintBackendKind.ImageSharpWebGpu => CreateImageSharpBackend(fonts, webFonts, diag, useWebGpu: true),
            _ => throw new InvalidOperationException($"Unhandled paint backend: {Selected}."),
        };
    }

    internal static ITextMeasurer CreateMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Selected switch
        {
            PaintBackendKind.Skia => new SkiaTextMeasurer(fonts, webFonts),
            PaintBackendKind.ImageSharp or PaintBackendKind.ImageSharpWebGpu => CreateImageSharpMeasurer(fonts, webFonts),
            _ => throw new InvalidOperationException($"Unhandled paint backend: {Selected}."),
        };
    }

#if TESSERA_IMAGESHARP_DRAWING
    private static ImageSharpBackend CreateImageSharpBackend(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diag, bool useWebGpu)
        => new(fonts, webFonts, diag, useWebGpu);

    private static ImageSharpTextMeasurer CreateImageSharpMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
        => new(fonts, webFonts);
#else
    private static IPaintBackend CreateImageSharpBackend(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diag, bool useWebGpu)
        => throw new InvalidOperationException(
            $"{EnvVar}=imagesharp(-webgpu) requires the assembly to be built with the MSBuild property " +
            "EnableImageSharpDrawing3=true; this binary was compiled without it.");

    private static ITextMeasurer CreateImageSharpMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
        => throw new InvalidOperationException(
            $"{EnvVar}=imagesharp(-webgpu) requires the assembly to be built with the MSBuild property " +
            "EnableImageSharpDrawing3=true; this binary was compiled without it.");
#endif
}
