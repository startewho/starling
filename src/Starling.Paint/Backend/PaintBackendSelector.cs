using Starling.Layout.Text;

namespace Starling.Paint.Backend;

internal enum PaintBackendKind
{
    ImageSharp,
    ImageSharpWebGpu,
}

/// <summary>
/// Reads <c>STARLING_PAINT_BACKEND</c> once and dispenses the matching paint
/// backend and text measurer so layout and raster never disagree within a
/// single render.
/// <para>
/// After the Skia/Graphite native shim was removed, ImageSharp.Drawing 3.0 is
/// the only paint backend. The default is the WebGPU compute-shader target
/// (equivalent to <c>STARLING_PAINT_BACKEND=imagesharp-webgpu</c>); callers
/// can opt back to the pure-CPU path with <c>STARLING_PAINT_BACKEND=imagesharp</c>.
/// Any other non-empty value is rejected loudly rather than silently falling
/// back, so a typo in an Aspire manifest or CI matrix surfaces immediately.
/// </para>
/// </summary>
internal static class PaintBackendSelector
{
    private const string EnvVar = "STARLING_PAINT_BACKEND";

    private static readonly Lazy<PaintBackendKind> _selected = new(ReadEnv);

    internal static PaintBackendKind Selected => _selected.Value;

    private static PaintBackendKind ReadEnv() => Parse(Environment.GetEnvironmentVariable(EnvVar));

    internal static PaintBackendKind Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return PaintBackendKind.ImageSharpWebGpu;

        return raw.Trim().ToLowerInvariant() switch
        {
            "imagesharp" => PaintBackendKind.ImageSharp,
            "imagesharp-webgpu" or "imagesharp-gpu" => PaintBackendKind.ImageSharpWebGpu,
            _ => throw new InvalidOperationException(
                $"{EnvVar}='{raw}' is not a recognised paint backend. Allowed values: 'imagesharp', 'imagesharp-webgpu'."),
        };
    }

    /// <summary>The factory for the currently-selected backend kind.</summary>
    internal static IPaintBackendFactory Factory => FactoryFor(Selected);

    /// <summary>Single registration point: map each backend kind to its factory.
    /// A non-ImageSharp backend adds an arm here.</summary>
    internal static IPaintBackendFactory FactoryFor(PaintBackendKind kind) => kind switch
    {
        PaintBackendKind.ImageSharp => new ImageSharpPaintBackendFactory(useWebGpu: false),
        PaintBackendKind.ImageSharpWebGpu => new ImageSharpPaintBackendFactory(useWebGpu: true),
        _ => throw new InvalidOperationException($"Unhandled paint backend: {kind}."),
    };

    internal static IPaintBackend Create(FontResolver fonts, FontFaceRegistry? webFonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Factory.CreateBackend(fonts, webFonts);
    }

    internal static ITextMeasurer CreateMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        return Factory.CreateMeasurer(fonts, webFonts);
    }
}
