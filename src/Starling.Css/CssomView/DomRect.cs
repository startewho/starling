namespace Starling.Css.CssomView;

/// <summary>
/// Mutable geometry rectangle per
/// <see href="https://drafts.csswg.org/cssom-view/#domrect">CSSOM View § DOMRect</see>.
/// Extends <see cref="DomRectReadOnly"/> with settable <c>X</c>, <c>Y</c>,
/// <c>Width</c>, and <c>Height</c>; the derived edge properties
/// (<c>Top</c>, <c>Left</c>, <c>Right</c>, <c>Bottom</c>) recompute on every
/// read from the mutable fields using the same spec algorithm.
/// </summary>
public sealed class DomRect
{
    /// <summary>Initialises a mutable rect. All values are in CSS px.</summary>
    /// <param name="x">Left edge of the origin point.</param>
    /// <param name="y">Top edge of the origin point.</param>
    /// <param name="width">Width; may be negative (inverts Left/Right).</param>
    /// <param name="height">Height; may be negative (inverts Top/Bottom).</param>
    public DomRect(double x = 0, double y = 0, double width = 0, double height = 0)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>X coordinate of the rect's origin. CSSOM View §DOMRect.x</summary>
    public double X { get; set; }

    /// <summary>Y coordinate of the rect's origin. CSSOM View §DOMRect.y</summary>
    public double Y { get; set; }

    /// <summary>Width of the rect (may be negative). CSSOM View §DOMRect.width</summary>
    public double Width { get; set; }

    /// <summary>Height of the rect (may be negative). CSSOM View §DOMRect.height</summary>
    public double Height { get; set; }

    /// <summary>
    /// Smaller of <see cref="Y"/> and <c>Y + Height</c>.
    /// CSSOM View §DOMRectReadOnly.top (mutable variant).
    /// </summary>
    public double Top => Math.Min(Y, Y + Height);

    /// <summary>
    /// Smaller of <see cref="X"/> and <c>X + Width</c>.
    /// CSSOM View §DOMRectReadOnly.left (mutable variant).
    /// </summary>
    public double Left => Math.Min(X, X + Width);

    /// <summary>
    /// Larger of <see cref="X"/> and <c>X + Width</c>.
    /// CSSOM View §DOMRectReadOnly.right (mutable variant).
    /// </summary>
    public double Right => Math.Max(X, X + Width);

    /// <summary>
    /// Larger of <see cref="Y"/> and <c>Y + Height</c>.
    /// CSSOM View §DOMRectReadOnly.bottom (mutable variant).
    /// </summary>
    public double Bottom => Math.Max(Y, Y + Height);

    /// <summary>Returns a <see cref="DomRectReadOnly"/> snapshot of this rect's current values.</summary>
    public DomRectReadOnly ToReadOnly() => new(X, Y, Width, Height);
}
