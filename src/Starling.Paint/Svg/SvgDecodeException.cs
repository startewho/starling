namespace Starling.Paint.Svg;

/// <summary>
/// Thrown when <see cref="SvgImageDecoder"/> cannot turn a byte/text payload
/// into a raster image — malformed XML, a missing <c>&lt;svg&gt;</c> root, or a
/// document with no rasterizable geometry. Callers (the engine image pipeline)
/// catch this to fall back to alt text, mirroring how a raster decode failure
/// is handled.
/// </summary>
public sealed class SvgDecodeException : Exception
{
    public SvgDecodeException() { }
    public SvgDecodeException(string message) : base(message) { }
    public SvgDecodeException(string message, Exception inner) : base(message, inner) { }
}
