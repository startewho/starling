using System.Globalization;

namespace Tessera.Css.Values;

public abstract record CssValue;

public sealed record CssKeyword(string Name) : CssValue
{
    public override string ToString() => Name;
}

public sealed record CssNumber(double Value) : CssValue
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record CssPercentage(double Value) : CssValue
{
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture) + "%";
}

public sealed record CssLength(double Value, CssLengthUnit Unit) : CssValue
{
    public static CssLength Zero { get; } = new(0, CssLengthUnit.Px);

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture) + Unit.ToCssText();
}

public enum CssLengthUnit
{
    Px,
    Em,
    Rem,
    Vh,
    Vw,
    Vmin,
    Vmax,
    Pt,
    Pc,
    In,
    Cm,
    Mm,
    Ch,
    Ex,
    Q,
}

public sealed record CssDimension(double Value, string Unit) : CssValue;

public sealed record CssColor(byte R, byte G, byte B, byte A = 255) : CssValue
{
    public static CssColor Transparent { get; } = new(0, 0, 0, 0);

    public static CssColor Black { get; } = new(0, 0, 0);

    public override string ToString()
        => A == 255
            ? $"rgb({R}, {G}, {B})"
            : $"rgba({R}, {G}, {B}, {(A / 255d).ToString("0.###", CultureInfo.InvariantCulture)})";
}

public sealed record CssString(string Value) : CssValue;

public sealed record CssUrl(string Value) : CssValue;

public sealed record CssFunctionValue(string Name, IReadOnlyList<CssValue> Arguments) : CssValue;

public sealed record CssValueList(IReadOnlyList<CssValue> Values) : CssValue;

public sealed record CssVarReference(string Name, CssValue? Fallback) : CssValue;

public static class CssLengthUnitExtensions
{
    public static string ToCssText(this CssLengthUnit unit)
        => unit switch
        {
            CssLengthUnit.Px => "px",
            CssLengthUnit.Em => "em",
            CssLengthUnit.Rem => "rem",
            CssLengthUnit.Vh => "vh",
            CssLengthUnit.Vw => "vw",
            CssLengthUnit.Vmin => "vmin",
            CssLengthUnit.Vmax => "vmax",
            CssLengthUnit.Pt => "pt",
            CssLengthUnit.Pc => "pc",
            CssLengthUnit.In => "in",
            CssLengthUnit.Cm => "cm",
            CssLengthUnit.Mm => "mm",
            CssLengthUnit.Ch => "ch",
            CssLengthUnit.Ex => "ex",
            CssLengthUnit.Q => "q",
            _ => unit.ToString().ToLowerInvariant(),
        };
}
