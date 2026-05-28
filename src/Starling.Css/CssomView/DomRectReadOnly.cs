namespace Starling.Css.CssomView;

/// <summary>
/// Immutable geometry rectangle per
/// <see href="https://drafts.csswg.org/cssom-view/#domrectreadonly">CSSOM View § DOMRectReadOnly</see>.
/// </summary>
/// <remarks>
/// <para>The derived edge properties follow the spec algorithm verbatim so that
/// negative <see cref="Width"/> or <see cref="Height"/> produce the correct
/// "flipped" bounding edges:</para>
/// <list type="bullet">
///   <item><see cref="Top"/>    = min(<c>y</c>, <c>y + height</c>)</item>
///   <item><see cref="Left"/>   = min(<c>x</c>, <c>x + width</c>)</item>
///   <item><see cref="Right"/>  = max(<c>x</c>, <c>x + width</c>)</item>
///   <item><see cref="Bottom"/> = max(<c>y</c>, <c>y + height</c>)</item>
/// </list>
/// <para>This matches
/// <see href="https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-top">CSSOM View §DOMRectReadOnly.top</see>
/// (and the corresponding Left/Right/Bottom members).</para>
/// </remarks>
public class DomRectReadOnly
{
    /// <summary>Initialises a new read-only rect. All values are in CSS px.</summary>
    /// <param name="x">Left edge of the origin point.</param>
    /// <param name="y">Top edge of the origin point.</param>
    /// <param name="width">Width; may be negative (inverts Left/Right).</param>
    /// <param name="height">Height; may be negative (inverts Top/Bottom).</param>
    public DomRectReadOnly(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>X coordinate of the rect's origin. CSSOM View §DOMRectReadOnly.x</summary>
    public double X { get; }

    /// <summary>Y coordinate of the rect's origin. CSSOM View §DOMRectReadOnly.y</summary>
    public double Y { get; }

    /// <summary>Width of the rect (may be negative). CSSOM View §DOMRectReadOnly.width</summary>
    public double Width { get; }

    /// <summary>Height of the rect (may be negative). CSSOM View §DOMRectReadOnly.height</summary>
    public double Height { get; }

    /// <summary>
    /// Smaller of <see cref="Y"/> and <c>Y + Height</c>.
    /// CSSOM View §DOMRectReadOnly.top
    /// </summary>
    public double Top => Math.Min(Y, Y + Height);

    /// <summary>
    /// Smaller of <see cref="X"/> and <c>X + Width</c>.
    /// CSSOM View §DOMRectReadOnly.left
    /// </summary>
    public double Left => Math.Min(X, X + Width);

    /// <summary>
    /// Larger of <see cref="X"/> and <c>X + Width</c>.
    /// CSSOM View §DOMRectReadOnly.right
    /// </summary>
    public double Right => Math.Max(X, X + Width);

    /// <summary>
    /// Larger of <see cref="Y"/> and <c>Y + Height</c>.
    /// CSSOM View §DOMRectReadOnly.bottom
    /// </summary>
    public double Bottom => Math.Max(Y, Y + Height);

    /// <summary>Returns a mutable <see cref="DomRect"/> with the same dimensions.</summary>
    public DomRect ToMutable() => new(X, Y, Width, Height);
}
