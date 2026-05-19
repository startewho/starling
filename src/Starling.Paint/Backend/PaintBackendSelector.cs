using Tessera.Common.Diagnostics;
using Tessera.Layout.Text;

namespace Tessera.Paint.Backend;

internal enum PaintBackendKind
{
    ImageSharp,
    ImageSharpWebGpu,
}

/// <summary>
/// Reads <c>TESSERA_PAINT_BACKEND</c> once and dispenses the matching paint
/// backend and text measurer so layout and raster never disagree within a
/// single render.
/// <para>
/// After the Skia/Graphite native shim was removed, ImageSharp.Drawing 3.0 is
/// the only paint backend. The env var still exists so callers can opt into
/// the experimental WebGPU compute-shader target
/// (<c>TESSERA_PAINT_BACKEND=imagesharp-webgpu</c>). Any other non-empty
/// value is rejected loudly rather than silently falling back, so a typo in
/// an Aspire manifest or CI matrix surfaces immediately.
/// </para>
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
            return PaintBackendKind.ImageSharp;

        return raw.Trim().ToLowerInvariant() switch
        {
            "imagesharp" => PaintBackendKind.ImageSharp,
            "imagesharp-webgpu" or "imagesharp-gpu" => PaintBackendKind.ImageSharpWebGpu,
            _ => throw new InvalidOperationException(
                $"{EnvVar}='{raw}' is not a recognised paint backend. Allowed values: 'imagesharp', 'imagesharp-webgpu'."),
        };
    }

    internal static IPaintBackend Create(FontResolver fonts, FontFaceRegistry? webFonts, IDiagnostics? diag = null)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Selected switch
        {
            PaintBackendKind.ImageSharp => new ImageSharpBackend(fonts, webFonts, diag, useWebGpu: false),
            PaintBackendKind.ImageSharpWebGpu => new ImageSharpBackend(fonts, webFonts, diag, useWebGpu: true),
            _ => throw new InvalidOperationException($"Unhandled paint backend: {Selected}."),
        };
    }

    internal static ITextMeasurer CreateMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return new ImageSharpTextMeasurer(fonts, webFonts);
    }
}
