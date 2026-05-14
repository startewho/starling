namespace Tessera.Paint;

/// <summary>
/// Selects the raster backend <see cref="Painter.RenderDocument"/> drives.
/// Both backends consume the identical <c>DisplayList</c> — the seam — and
/// yield a backend-neutral <see cref="Common.Image.RenderedBitmap"/>.
/// </summary>
public enum PaintBackend
{
    /// <summary>
    /// The pure-managed ImageSharp rasterizer. The default and the PNG encoder;
    /// golden tests are baselined against its output.
    /// </summary>
    ImageSharp = 0,

    /// <summary>
    /// The Skia Graphite (Dawn) GPU rasterizer via the <c>Tessera.Skia</c>
    /// interop seam. Opt-in via <c>TESSERA_PAINT_BACKEND=skia</c>; osx-arm64
    /// only for now and not bit-exact across drivers.
    /// </summary>
    SkiaGraphite = 1,
}
