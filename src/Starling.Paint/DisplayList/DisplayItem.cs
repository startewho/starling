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

/// <summary>
/// Pushes an axis-aligned page-coordinate clip region onto the backend's
/// clip stack. Subsequent draw items are masked to the intersection of every
/// clip currently on the stack. Used by <see cref="DisplayListBuilder"/> to
/// implement <c>overflow: hidden</c>/<c>clip</c>/<c>scroll</c>/<c>auto</c> —
/// the box's border-box clip is opened on descent and closed
/// (<see cref="PopClip"/>) on ascent so content that overflows the box never
/// paints outside it.
/// <para>
/// When <paramref name="Radii"/> is non-zero the clip region is a rounded
/// rectangle (CSS Backgrounds 3 §5 corner radii); the backend clips to the
/// rounded shape so descendants respect <c>border-radius</c> clipping
/// (CSS Overflow 3 §2.4 — a box with a rounded border that also clips clips
/// children to the rounded inner edge).
/// </para>
/// </summary>
public sealed record PushClip(Rect Bounds, CornerRadii Radii) : DisplayItem
{
    /// <summary>Constructs a plain rectangular clip (no rounding).</summary>
    public PushClip(Rect Bounds) : this(Bounds, CornerRadii.None) { }
}

/// <summary>Pops the most recent <see cref="PushClip"/> off the backend clip stack.</summary>
public sealed record PopClip : DisplayItem
{
    public static PopClip Instance { get; } = new();
}

/// <summary>
/// Fills <paramref name="Bounds"/> with a CSS <c>&lt;gradient&gt;</c>
/// (<see href="https://www.w3.org/TR/css-images-3/#gradients">CSS Images 3 §3</see>).
/// The backend maps the gradient's color stops onto an ImageSharp gradient
/// brush (linear or radial) sized to <paramref name="Bounds"/>. Conic gradients
/// have no ImageSharp brush; the backend rasterizes them per-pixel. When
/// <paramref name="Radii"/> is non-zero the gradient fill is clipped to the
/// rounded rectangle (CSS Backgrounds 3 §5 border-radius).
/// </summary>
public sealed record FillGradient(Rect Bounds, CssGradient Gradient, CornerRadii Radii = default) : DisplayItem;
// ---------------------------------------------------------------------------
// wp:M5-css-14 — border-radius painting + box-shadow.
// Appended at the end per the shared-paint-file etiquette (DisplayItem.cs is
// shared with the gradients and text-decoration WPs).
// ---------------------------------------------------------------------------

/// <summary>
/// The four corner radii of a box, in CSS px, in
/// <c>top-left, top-right, bottom-right, bottom-left</c> order — the same order
/// the <c>border-*-radius</c> longhands and the <c>border-radius</c> shorthand
/// use. Each corner carries a horizontal (<c>Rx</c>) and vertical (<c>Ry</c>)
/// radius so elliptical corners are representable; the current builder fills
/// both from the single per-corner length, which is the common circular case.
/// </summary>
public readonly record struct CornerRadii(
    double TopLeftX, double TopLeftY,
    double TopRightX, double TopRightY,
    double BottomRightX, double BottomRightY,
    double BottomLeftX, double BottomLeftY)
{
    /// <summary>All corners square (no rounding).</summary>
    public static CornerRadii None { get; } = default;

    /// <summary>Builds uniform circular radii from one per-corner length each.</summary>
    public static CornerRadii Uniform(double topLeft, double topRight, double bottomRight, double bottomLeft)
        => new(topLeft, topLeft, topRight, topRight, bottomRight, bottomRight, bottomLeft, bottomLeft);

    /// <summary>True when every corner radius is zero (a plain rectangle).</summary>
    public bool IsZero =>
        TopLeftX <= 0 && TopLeftY <= 0 &&
        TopRightX <= 0 && TopRightY <= 0 &&
        BottomRightX <= 0 && BottomRightY <= 0 &&
        BottomLeftX <= 0 && BottomLeftY <= 0;
}

/// <summary>
/// Fills <paramref name="Bounds"/> with <paramref name="Color"/>, rounding the
/// corners by <paramref name="Radii"/> (CSS Backgrounds 3 §5). When the radii
/// are all zero this is equivalent to a <see cref="FillRect"/>; the painter may
/// short-circuit to a plain rectangle in that case.
/// </summary>
public sealed record FillRoundedRect(Rect Bounds, CornerRadii Radii, CssColor Color) : DisplayItem;

/// <summary>
/// Strokes the rounded-rect path described by <paramref name="Bounds"/> and
/// <paramref name="Radii"/> with a pen of <paramref name="Width"/> CSS px in
/// <paramref name="Color"/>. Used by the builder to paint a uniform rounded
/// border as a single centre-line ring; mixed per-side rounded borders are not
/// yet expressed through this primitive.
/// </summary>
public sealed record StrokeRoundedRect(Rect Bounds, CornerRadii Radii, CssColor Color, double Width) : DisplayItem;

/// <summary>
/// Casts a single outer drop shadow for a (possibly rounded) box per CSS
/// Backgrounds 3 §6. The shadow silhouette is <paramref name="Bounds"/> grown
/// by <paramref name="Spread"/> on every side (its corner radii grow with it),
/// translated by (<paramref name="OffsetX"/>, <paramref name="OffsetY"/>), and
/// softened by a Gaussian whose standard deviation is <paramref name="Blur"/>/2
/// (the spec defines the blur radius as one standard deviation × 2). When
/// <paramref name="Inset"/> is true the layer is an inner shadow (§7.1.1):
/// <paramref name="Bounds"/> then carries the element's PADDING box and
/// <paramref name="Radii"/> the padding-box (inner) radii. The painter fills
/// the ring between the inner silhouette — the padding box translated by the
/// offset and shrunk by <paramref name="Spread"/> on every side — and the
/// padding edge, blurred, clipped to the (rounded) padding box. A positive
/// <paramref name="OffsetX"/> shifts the silhouette right and thickens the
/// LEFT shadow band.
/// </summary>
public sealed record DrawBoxShadow(
    Rect Bounds,
    CornerRadii Radii,
    double OffsetX,
    double OffsetY,
    double Blur,
    double Spread,
    CssColor Color,
    bool Inset) : DisplayItem;
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
// ---------------------------------------------------------------------------
// CSS Backgrounds 3 §3.8 — `background-clip: text`. Appended at the end per the
// shared-paint-file etiquette.
// ---------------------------------------------------------------------------

/// <summary>
/// One glyph run gathered from a descendant text box, in document-space
/// coordinates. Carries the same shaping data as <see cref="DrawText"/> so the
/// backend can render the run identically — here it is used as an alpha mask
/// for a <c>background-clip: text</c> fill rather than as visible foreground
/// text. <paramref name="X"/>/<paramref name="Y"/> are the top-left of the line
/// box (matching <see cref="DrawText"/> origin semantics).
/// </summary>
public readonly record struct ClipGlyphRun(
    string Text,
    double X,
    double Y,
    double FontSize,
    IReadOnlyList<string> FontFamilies,
    bool Bold,
    bool Italic,
    ShapedRun? Shaped);

/// <summary>
/// Paints a box's background (a <see cref="CssGradient"/> when
/// <paramref name="Gradient"/> is non-null, otherwise the solid
/// <paramref name="Color"/>) clipped to the union of the element's text glyphs
/// (CSS Backgrounds 3 §3.8 <c>background-clip: text</c>, with the
/// <c>-webkit-background-clip: text</c> alias). The backend renders the
/// background into an offscreen layer, keeps only the pixels covered by a glyph
/// in <paramref name="Glyphs"/> (an alpha mask), then composites the result.
/// The matching plain background fill and the now-transparent foreground text
/// are not emitted, so the gradient shows through the glyph shapes and the rest
/// of the box stays transparent.
/// </summary>
public sealed record FillBackgroundTextClip(
    Rect Bounds,
    CssGradient? Gradient,
    CssColor Color,
    IReadOnlyList<ClipGlyphRun> Glyphs) : DisplayItem;
// ---------------------------------------------------------------------------
// CSS Masking 1 §6 — `mask-image` (and the `-webkit-mask-*` aliases). Appended
// at the end per the shared-paint-file etiquette.
// ---------------------------------------------------------------------------

/// <summary>
/// How a mask image repeats across the box (CSS Masking 1 §6.5
/// <c>mask-repeat</c>).
/// </summary>
public enum MaskRepeatMode
{
    /// <summary>Tile the mask image on both axes (default).</summary>
    Repeat,
    /// <summary>Paint the mask image once, no tiling.</summary>
    NoRepeat,
    /// <summary>Tile with uniform gap so tiles fill the box exactly (CSS Masking 1 §6.5 <c>space</c>).</summary>
    Space,
    /// <summary>Stretch each tile so an integer number of tiles fills the box exactly (CSS Masking 1 §6.5 <c>round</c>).</summary>
    Round,
    /// <summary>Tile on the X axis only.</summary>
    RepeatX,
    /// <summary>Tile on the Y axis only.</summary>
    RepeatY,
}

/// <summary>
/// How the mask's alpha is derived from the mask source (CSS Masking 1 §6.1
/// <c>mask-mode</c>). <see cref="Alpha"/> uses the source's alpha channel directly;
/// <see cref="Luminance"/> converts each pixel to luminance and uses that as the
/// mask value; <see cref="MatchSource"/> picks alpha for raster images (default).
/// </summary>
public enum MaskModeKind
{
    /// <summary>Use the source alpha channel as the mask value.</summary>
    Alpha,
    /// <summary>Use linearised luminance of the source as the mask value.</summary>
    Luminance,
    /// <summary>Default: alpha for raster images (same as Alpha for decoded bitmaps).</summary>
    MatchSource,
}

/// <summary>
/// Opens an offscreen compositing layer for CSS Masking 1 mask-image applied to
/// a whole element (background + border + content + descendants). All display
/// items between this push and the matching <see cref="PopMask"/> are rendered
/// into an offscreen surface; the result is then masked by the alpha (or
/// luminance, per <paramref name="Mode"/>) of the mask source and composited onto
/// the main canvas. The mask source is either the decoded raster image
/// <paramref name="Mask"/> or the CSS gradient <paramref name="MaskGradient"/>
/// (exactly one non-null). Mask geometry (render size, repeat, offset) matches
/// the <c>FillMaskedBackground</c> fields of the same names — sizes are in CSS
/// px relative to the element's top-left.
/// </summary>
public sealed record PushMask(
    Rect Bounds,
    CornerRadii Radii,
    DecodedImage? Mask,
    CssGradient? MaskGradient,
    double MaskRenderWidth,
    double MaskRenderHeight,
    double MaskOffsetX,
    double MaskOffsetY,
    MaskRepeatMode MaskRepeat,
    MaskModeKind Mode = MaskModeKind.MatchSource) : DisplayItem;

/// <summary>Pops the most recent <see cref="PushMask"/> off the mask stack.</summary>
public sealed record PopMask : DisplayItem
{
    public static PopMask Instance { get; } = new();
}

// ---------------------------------------------------------------------------
// CSS Masking 1 §7 — `clip-path` basic shapes. Appended at the end per the
// shared-paint-file etiquette.
// ---------------------------------------------------------------------------

/// <summary>
/// Pushes a CSS basic-shape clip onto the backend clip stack. All display
/// items between this push and the matching <see cref="PopClipPath"/> are
/// clipped to the resolved shape. Unlike <see cref="PushClip"/> (which is
/// always a rect/rounded-rect), this item carries a typed
/// <see cref="CssClipPath"/> value whose shape is resolved at paint time
/// against the element's reference box
/// (<see cref="ReferenceBox"/> = the border-box in page coordinates).
/// <para>
/// <c>clip-path: url(#id)</c> is deferred — callers emit this item only for
/// the basic-shape and geometry-box cases. A url reference is a clean no-op
/// (no item emitted).
/// </para>
/// </summary>
public sealed record PushClipPath(Rect ReferenceBox, CssClipPath ClipPath) : DisplayItem;

/// <summary>Pops the most recent <see cref="PushClipPath"/> off the clip stack.</summary>
public sealed record PopClipPath : DisplayItem
{
    public static PopClipPath Instance { get; } = new();
}

/// <summary>
/// Fills <paramref name="Bounds"/> with a box background (a
/// <see cref="CssGradient"/> when <paramref name="Gradient"/> is non-null, then a
/// background <see cref="BackgroundImage"/> when set, otherwise the solid
/// <paramref name="Color"/>) masked by the resolved mask channel per
/// <paramref name="Mode"/> (CSS Masking 1 §6). The mask source is either a
/// decoded raster image in <paramref name="Mask"/> or a CSS gradient in
/// <paramref name="MaskGradient"/> — exactly one is non-null. The backend renders
/// the background into an offscreen layer, paints/rasterizes the mask source
/// tiled/sized/positioned per <paramref name="MaskRenderWidth"/> /
/// <paramref name="MaskRenderHeight"/> / <paramref name="MaskRepeat"/> /
/// (<paramref name="MaskOffsetX"/>, <paramref name="MaskOffsetY"/>) into a mask
/// layer, applies the mask channel per <paramref name="Mode"/>, then clips the
/// result to <paramref name="Radii"/> before compositing. Sizes/offsets are in
/// CSS px relative to the box top-left.
/// </summary>
public sealed record FillMaskedBackground(
    Rect Bounds,
    CssGradient? Gradient,
    CssColor Color,
    DecodedImage? BackgroundImage,
    CornerRadii Radii,
    DecodedImage? Mask,
    CssGradient? MaskGradient,
    double MaskRenderWidth,
    double MaskRenderHeight,
    double MaskOffsetX,
    double MaskOffsetY,
    MaskRepeatMode MaskRepeat,
    MaskModeKind Mode = MaskModeKind.MatchSource) : DisplayItem;

// ---------------------------------------------------------------------------
// CSS Backgrounds 3 §4.2 border-style (dashed / dotted / double) and CSS UI 4
// §3 outline painting. Appended at the end per the shared-paint-file etiquette.
// ---------------------------------------------------------------------------

/// <summary>
/// Line style for one border side (the painted subset of CSS Backgrounds 3
/// §4.2 <c>border-style</c>). <see cref="None"/> covers <c>none</c>/<c>hidden</c>
/// (side not painted); <c>groove</c>/<c>ridge</c>/<c>inset</c>/<c>outset</c>
/// are mapped to <see cref="Solid"/> by the builder at this fidelity.
/// </summary>
public enum BorderSideStyle
{
    None,
    Solid,
    Dashed,
    Dotted,
    Double,
}

/// <summary>
/// Paints the four border sides of a box where at least one painted side uses
/// a non-solid style (dashed / dotted / double). <paramref name="Bounds"/> is
/// the border box. The backend draws each side separately between its corner
/// boundaries: for a square corner the horizontal sides own the corner and the
/// vertical sides start inside the adjacent band; for a rounded corner every
/// side's run stops at the radius tangent and the corner arc is painted as a
/// SOLID quarter ring (documented v1 simplification — dashes do not follow the
/// curve). Dash geometry per side: dashed = rectangular dashes at a 2:1
/// dash:gap ratio anchored at both run ends; dotted = round dots of diameter
/// equal to the side width spaced about twice the width apart; double = two
/// parallel strokes each one-third of the side width with the middle third
/// empty. The outline emitter reuses this primitive for non-solid outline
/// styles on the expanded ring box.
/// </summary>
public sealed record DrawBorderSides(
    Rect Bounds,
    CornerRadii Radii,
    double TopWidth,
    double RightWidth,
    double BottomWidth,
    double LeftWidth,
    CssColor TopColor,
    CssColor RightColor,
    CssColor BottomColor,
    CssColor LeftColor,
    BorderSideStyle TopStyle,
    BorderSideStyle RightStyle,
    BorderSideStyle BottomStyle,
    BorderSideStyle LeftStyle) : DisplayItem;

/// <summary>
/// Two stroked line segments through three points (P0→P1→P2), drawn with a
/// round-capped solid pen. Resolution-independent vector primitive for form
/// control glyphs: the checkbox check mark and the <c>&lt;select&gt;</c>
/// chevron are both a single three-point polyline. Coordinates are
/// page-space CSS px; <paramref name="Color"/> carries the element's resolved
/// <c>currentColor</c> so author <c>color</c> tints the glyph.
/// </summary>
public sealed record StrokeSegments(
    double X0, double Y0,
    double X1, double Y1,
    double X2, double Y2,
    CssColor Color,
    double Width) : DisplayItem;

// ---------------------------------------------------------------------------
// Filter Effects 1 — `filter` / `backdrop-filter` painting (Tier 4 item 18).
// Appended at the end per the shared-paint-file etiquette.
// ---------------------------------------------------------------------------

/// <summary>
/// Which Filter Effects 1 §10.1 shorthand function a <see cref="FilterFunction"/>
/// carries. The kinds map one-to-one onto the parse-side function names.
/// </summary>
public enum FilterFunctionKind
{
    Blur,
    Brightness,
    Contrast,
    Grayscale,
    Sepia,
    Saturate,
    HueRotate,
    Invert,
    Opacity,
    DropShadow,
}

/// <summary>
/// One resolved filter function in a `filter` / `backdrop-filter` chain.
/// <see cref="Amount"/> semantics depend on <see cref="Kind"/>:
/// <list type="bullet">
///   <item><see cref="FilterFunctionKind.Blur"/> — the blur radius in CSS px.</item>
///   <item><see cref="FilterFunctionKind.HueRotate"/> — the rotation in degrees.</item>
///   <item><see cref="FilterFunctionKind.DropShadow"/> — the shadow blur radius in
///   CSS px; <see cref="OffsetX"/>/<see cref="OffsetY"/> carry the offset and
///   <see cref="Color"/> the shadow color.</item>
///   <item>every other kind — the unit amount (1 = identity, already divided
///   out of a percentage by the builder).</item>
/// </list>
/// </summary>
public readonly record struct FilterFunction(
    FilterFunctionKind Kind,
    double Amount,
    double OffsetX = 0,
    double OffsetY = 0,
    CssColor? Color = null)
{
    /// <summary>
    /// σ of the Gaussian for <c>blur(&lt;length&gt;)</c>. Filter Effects 1
    /// §10.1 defines the parameter AS the standard deviation, so σ = radius —
    /// unlike shadow blur radii, which are double the σ.
    /// </summary>
    internal static double BlurSigma(double radius) => Math.Max(0, radius);

    /// <summary>
    /// σ of the Gaussian for <c>drop-shadow()</c>'s blur radius — interpreted
    /// "as for box-shadow" per Filter Effects 1, i.e. radius/2, matching this
    /// repo's box-shadow rasterizer (<c>GaussianBlur(Blur / 2)</c>).
    /// </summary>
    internal static double ShadowSigma(double radius) => Math.Max(0, radius) / 2d;

    /// <summary>
    /// Padding (CSS px) the offscreen surface needs around the filtered
    /// content so the result is not cropped: 3σ per blur (the Gaussian tail —
    /// without it the layer edge samples transparent pixels and goes dark)
    /// plus the offset + 3σ of each drop-shadow.
    /// </summary>
    internal static double HaloPadding(IReadOnlyList<FilterFunction> filters)
    {
        double pad = 0;
        for (var i = 0; i < filters.Count; i++)
        {
            var f = filters[i];
            switch (f.Kind)
            {
                case FilterFunctionKind.Blur:
                    pad += Math.Ceiling(3 * BlurSigma(f.Amount)) + 2;
                    break;
                case FilterFunctionKind.DropShadow:
                    pad += Math.Ceiling(3 * ShadowSigma(f.Amount))
                           + Math.Max(Math.Abs(f.OffsetX), Math.Abs(f.OffsetY)) + 2;
                    break;
            }
        }
        return pad;
    }
}

/// <summary>
/// Opens an offscreen compositing group for CSS `filter` (Filter Effects 1
/// §10): all display items between this push and the matching
/// <see cref="PopFilter"/> — the element's own box paint plus its descendants —
/// are rendered into a transparent offscreen surface, the
/// <see cref="Filters"/> chain is applied IN ORDER, and the result is
/// composited back through the current canvas transform.
/// <see cref="Bounds"/> is the element's border box in page coordinates; the
/// backend grows the offscreen by the painted extents of the bracketed items
/// and by the chain's blur/offset halo (see
/// <see cref="FilterFunction.HaloPadding"/>).
/// </summary>
public sealed record PushFilter(Rect Bounds, IReadOnlyList<FilterFunction> Filters) : DisplayItem;

/// <summary>Pops the most recent <see cref="PushFilter"/> off the filter stack.</summary>
public sealed record PopFilter : DisplayItem
{
    public static PopFilter Instance { get; } = new();
}

/// <summary>
/// Applies `backdrop-filter` (Filter Effects 2 §6) at the element's paint
/// position: the backend snapshots the CURRENT canvas content under the
/// element's border box <see cref="Bounds"/> (expanded by the chain's blur
/// halo so the Gaussian has real neighbours at the edge), runs
/// <see cref="Filters"/> over the patch, and draws it back clipped to the
/// border box rounded by <see cref="Radii"/>. The element's own background /
/// content items follow this item in paint order, so they paint over the
/// filtered backdrop and stay sharp.
/// <para>
/// v1 simplification: the snapshot reads the flattened canvas painted so far —
/// there is no isolation/backdrop-root grouping for overlapping promoted
/// layers, which matches what the CPU/ImageSharp path can see.
/// </para>
/// </summary>
public sealed record DrawBackdropFilter(
    Rect Bounds,
    CornerRadii Radii,
    IReadOnlyList<FilterFunction> Filters) : DisplayItem;
