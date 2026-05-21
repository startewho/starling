using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Layout;
using Starling.Layout.Text;

namespace Starling.Paint.DisplayList;

/// <summary>
/// Discriminated union of paint operations. Painters replay these in order to
/// produce the final raster. Decoupling paint from layout this way lets us
/// cache, diff, and serialize the paint stream.
/// </summary>
public abstract record DisplayItem;

public enum FillRectPixelAlignment
{
    Preserve,
    SnapToDevicePixels,
}

public sealed record FillRect(Rect Bounds, CssColor Color, FillRectPixelAlignment PixelAlignment) : DisplayItem;

public sealed record StrokeRect(Rect Bounds, CssColor Color, double Width) : DisplayItem;

public sealed record DrawText(
    string Text,
    double X,
    double Y,
    double FontSize,
    CssColor Color,
    IReadOnlyList<string> FontFamilies,
    bool Bold,
    bool Italic,
    ShapedRun? Shaped = null) : DisplayItem;

/// <summary>
/// Blit a decoded image into <paramref name="Bounds"/>. <paramref name="Source"/>
/// is a backend-neutral <see cref="DecodedImage"/> (straight RGBA8888); the
/// paint backend reads its pixels directly. If <c>Bounds</c> differs from the
/// source's native size the backend resamples. When
/// <paramref name="SourceRect"/> is non-null, only that sub-rectangle of the
/// source pixels is blitted — used for CSS sprite-sheet painting where
/// <c>background-position</c> picks a slice out of a larger image.
/// </summary>
public sealed record DrawImage(Rect Bounds, DecodedImage Source, Rect? SourceRect = null) : DisplayItem;

/// <summary>
/// Pushes a 2D affine <paramref name="Matrix"/> onto the backend's transform
/// stack. Subsequent paint items between this <see cref="PushTransform"/> and
/// its matching <see cref="PopTransform"/> are rendered with <c>current ×
/// Matrix</c> applied — left-to-right composition matches CSS Transforms 1
/// §6.1 (the outer push applies last, so a nested transform sees its parent's
/// matrix as the surrounding coordinate frame).
/// <para>
/// The <see cref="DisplayListBuilder"/> pre-bakes the box's
/// <c>transform-origin</c> into the matrix
/// (<c>T(+origin) × M × T(-origin)</c>), so the backend just applies the
/// composed matrix verbatim — it does not need to know the box geometry.
/// </para>
/// </summary>
public sealed record PushTransform(Matrix2D Matrix) : DisplayItem;

/// <summary>Pops the most recent <see cref="PushTransform"/> off the backend stack.</summary>
public sealed record PopTransform : DisplayItem
{
    public static PopTransform Instance { get; } = new();
}

// ---------------------------------------------------------------------------
// CSS Text Decoration 3 (wp:M5-css-15): real decoration lines + text-shadow.
// Appended at the end of the file per shared-paint-file etiquette.
// ---------------------------------------------------------------------------

/// <summary>
/// Which decoration lines to draw (CSS Text Decoration 3 §2.1
/// <c>text-decoration-line</c>). Combinable, so this is a [Flags] set.
/// </summary>
[Flags]
public enum TextDecorationLines
{
    None = 0,
    Underline = 1 << 0,
    Overline = 1 << 1,
    LineThrough = 1 << 2,
}

/// <summary>
/// Stroke style for a decoration line (CSS Text Decoration 3 §2.2
/// <c>text-decoration-style</c>).
/// </summary>
public enum TextDecorationStyleKind
{
    Solid,
    Double,
    Dotted,
    Dashed,
    Wavy,
}

/// <summary>
/// Paints one or more decoration lines (underline / overline / line-through)
/// across a single text run (CSS Text Decoration 3 §2). The run is described by
/// its left edge <paramref name="X"/>, <paramref name="Width"/>, the glyph
/// baseline <paramref name="BaselineY"/>, and the <paramref name="FontSize"/> so
/// the backend can resolve exact vertical positions from real font metrics
/// (ascender / x-height / underline position). <paramref name="Thickness"/> is
/// the resolved line thickness in CSS px (<c>auto</c> already resolved by the
/// builder), and <paramref name="UnderlineOffset"/> is the extra
/// <c>text-underline-offset</c> in px (0 when <c>auto</c>). The font identity is
/// carried so the backend resolves the same face the glyphs used.
/// </summary>
public sealed record DrawTextDecoration(
    double X,
    double Width,
    double BaselineY,
    double FontSize,
    CssColor Color,
    TextDecorationLines Lines,
    TextDecorationStyleKind Style,
    double Thickness,
    double UnderlineOffset,
    IReadOnlyList<string> FontFamilies,
    bool Bold,
    bool Italic) : DisplayItem;

/// <summary>
/// Paints a single <c>text-shadow</c> layer beneath a glyph run (CSS Text
/// Decoration 3 §5): the same <paramref name="Text"/> drawn at
/// (<paramref name="X"/>+OffsetX, <paramref name="Y"/>+OffsetY) in
/// <paramref name="Color"/>, blurred by <paramref name="Blur"/> px. The builder
/// emits one of these per layer, back-to-front, before the foreground
/// <see cref="DrawText"/>.
/// </summary>
public sealed record DrawTextShadow(
    string Text,
    double X,
    double Y,
    double FontSize,
    CssColor Color,
    double OffsetX,
    double OffsetY,
    double Blur,
    IReadOnlyList<string> FontFamilies,
    bool Bold,
    bool Italic,
    ShapedRun? Shaped = null) : DisplayItem;
