namespace Starling.Css.Values;

/// <summary>
/// Typed value for the CSS Images 3 <c>&lt;gradient&gt;</c> functions
/// (<see href="https://www.w3.org/TR/css-images-3/#gradients">§3</see>).
/// Produced from a generic <see cref="CssFunctionValue"/> by
/// <see cref="CssGradientParser"/> (mirroring <see cref="CssTransformParser"/>),
/// it carries enough information for the paint backend to build an ImageSharp
/// gradient brush.
/// <para>
/// Scope: <c>linear-gradient</c> and <c>radial-gradient</c> plus their
/// <c>repeating-</c> variants. <c>conic-gradient</c> is recognised as a
/// <see cref="CssGradientKind.Conic"/> kind but is not paintable — ImageSharp.Drawing
/// has no conic/sweep brush — so callers fail soft (box left unpainted).
/// </para>
/// </summary>
public sealed record CssGradient(
    CssGradientKind Kind,
    bool Repeating,
    IReadOnlyList<CssColorStop> Stops,
    CssGradientLine? Line = null,
    CssRadialShape Shape = CssRadialShape.Ellipse,
    CssRadialSize Size = CssRadialSize.FarthestCorner,
    CssGradientPosition? Position = null) : CssValue
{
    /// <summary>True when this gradient is one the paint backend can rasterize
    /// (linear or radial). Conic gradients have no ImageSharp brush.</summary>
    public bool IsPaintable => Kind is CssGradientKind.Linear or CssGradientKind.Radial && Stops.Count >= 1;
}

public enum CssGradientKind
{
    Linear,
    Radial,
    Conic,
}

/// <summary>The direction of a linear gradient — either an explicit
/// <c>&lt;angle&gt;</c> (measured clockwise from "to top", i.e. 0deg points up)
/// or a <c>to &lt;side-or-corner&gt;</c> keyword pair.</summary>
public sealed record CssGradientLine
{
    /// <summary>Angle in degrees, clockwise from straight up (CSS convention:
    /// 0deg = "to top", 90deg = "to right"). Null when a side/corner is set.</summary>
    public double? AngleDegrees { get; init; }

    /// <summary>Horizontal side component for <c>to &lt;side-or-corner&gt;</c>.</summary>
    public CssGradientSideX SideX { get; init; } = CssGradientSideX.None;

    /// <summary>Vertical side component for <c>to &lt;side-or-corner&gt;</c>.</summary>
    public CssGradientSideY SideY { get; init; } = CssGradientSideY.None;

    public bool IsAngle => AngleDegrees is not null;

    public static CssGradientLine FromAngle(double degrees) => new() { AngleDegrees = degrees };

    public static CssGradientLine FromSide(CssGradientSideX x, CssGradientSideY y) => new() { SideX = x, SideY = y };

    /// <summary>Resolve the gradient line to an angle in degrees (clockwise from
    /// "to top"). A <c>to &lt;side-or-corner&gt;</c> using the box's aspect ratio
    /// per CSS Images 3 §3.1; for corners the angle points toward the corner.</summary>
    public double ToDegrees(double boxWidth, double boxHeight)
    {
        if (AngleDegrees is { } a)
            return a;

        // Pure sides.
        if (SideX == CssGradientSideX.None)
        {
            return SideY switch
            {
                CssGradientSideY.Top => 0,
                CssGradientSideY.Bottom => 180,
                _ => 180, // default `to bottom`
            };
        }
        if (SideY == CssGradientSideY.None)
        {
            return SideX switch
            {
                CssGradientSideX.Right => 90,
                CssGradientSideX.Left => 270,
                _ => 180,
            };
        }

        // Corner: the gradient line points so that the line is perpendicular to
        // the line joining the two opposite corners (CSS Images 3 §3.1). We
        // approximate using the box-diagonal angle, which is correct for the
        // common corner cases.
        var w = boxWidth <= 0 ? 1 : boxWidth;
        var h = boxHeight <= 0 ? 1 : boxHeight;
        var diag = Math.Atan2(w, h) * 180.0 / Math.PI; // angle of the corner direction from vertical
        return (SideX, SideY) switch
        {
            (CssGradientSideX.Right, CssGradientSideY.Top) => diag,
            (CssGradientSideX.Right, CssGradientSideY.Bottom) => 180 - diag,
            (CssGradientSideX.Left, CssGradientSideY.Bottom) => 180 + diag,
            (CssGradientSideX.Left, CssGradientSideY.Top) => 360 - diag,
            _ => 180,
        };
    }
}

public enum CssGradientSideX { None, Left, Right }

public enum CssGradientSideY { None, Top, Bottom }

/// <summary>A radial gradient's <c>at &lt;position&gt;</c> as fractions of the
/// box (0..1). Defaults to center.</summary>
public readonly record struct CssGradientPosition(double FractionX, double FractionY)
{
    public static CssGradientPosition Center { get; } = new(0.5, 0.5);
}

public enum CssRadialShape { Circle, Ellipse }

public enum CssRadialSize
{
    ClosestSide,
    ClosestCorner,
    FarthestSide,
    FarthestCorner,
}

/// <summary>One color stop: a color and an optional position. When
/// <see cref="Position"/> is null the stop is evenly distributed at paint time
/// per CSS Images 3 §3.4.3.</summary>
public sealed record CssColorStop(CssColor Color, CssGradientStopPosition? Position = null);

/// <summary>A color-stop position: an absolute length (px-resolved) or a
/// percentage of the gradient line. Only one is set.</summary>
public readonly record struct CssGradientStopPosition(double Value, bool IsPercent)
{
    /// <summary>Resolve to a fraction (0..1) of the gradient line length.</summary>
    public double ResolveFraction(double lineLengthPx)
        => IsPercent
            ? Value / 100.0
            : (lineLengthPx <= 0 ? 0 : Value / lineLengthPx);
}
