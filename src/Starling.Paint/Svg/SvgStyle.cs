using SixLabors.ImageSharp;

namespace Starling.Paint.Svg;

/// <summary>
/// The resolved presentation state that cascades down the SVG element tree:
/// fill / stroke paints, stroke width, fill rule, and the three opacity
/// channels. A child clones its parent's state and overrides only the
/// properties it declares (presentation attributes, the <c>style="…"</c>
/// attribute, and matched <c>&lt;style&gt;</c> class rules).
/// </summary>
internal sealed class SvgStyle
{
    /// <summary>Fill paint, or null when <c>fill="none"</c>.</summary>
    public Color? Fill { get; set; } = Color.Black; // SVG default fill is black.

    /// <summary>Stroke paint, or null when there is no stroke.</summary>
    public Color? Stroke { get; set; }

    public float StrokeWidth { get; set; } = 1f;

    public bool FillEvenOdd { get; set; }

    public float Opacity { get; set; } = 1f;
    public float FillOpacity { get; set; } = 1f;
    public float StrokeOpacity { get; set; } = 1f;

    /// <summary>The inherited <c>currentColor</c> (CSS <c>color</c>).</summary>
    public Color CurrentColor { get; set; } = Color.Black;

    public SvgStyle Clone() => new()
    {
        Fill = Fill,
        Stroke = Stroke,
        StrokeWidth = StrokeWidth,
        FillEvenOdd = FillEvenOdd,
        Opacity = Opacity,
        FillOpacity = FillOpacity,
        StrokeOpacity = StrokeOpacity,
        CurrentColor = CurrentColor,
    };

    /// <summary>
    /// Effective fill color folding fill-opacity and the group opacity into the
    /// alpha channel, or null when there is no fill paint or it is fully
    /// transparent.
    /// </summary>
    public Color? EffectiveFill()
        => Apply(Fill, FillOpacity * Opacity);

    /// <summary>Effective stroke color, or null when there is no stroke.</summary>
    public Color? EffectiveStroke()
        => Apply(Stroke, StrokeOpacity * Opacity);

    private static Color? Apply(Color? c, float alphaScale)
    {
        if (c is null) return null;
        var px = c.Value.ToPixel<SixLabors.ImageSharp.PixelFormats.Rgba32>();
        float a = px.A / 255f * Math.Clamp(alphaScale, 0f, 1f);
        if (a <= 0f) return null;
        px.A = (byte)Math.Clamp((int)Math.Round(a * 255f), 0, 255);
        return Color.FromPixel(px);
    }

    /// <summary>
    /// Apply one <c>property:value</c> declaration to this style. Unknown
    /// properties are ignored. Used for both presentation attributes and the
    /// parsed contents of <c>style="…"</c> / matched class rules.
    /// </summary>
    public void ApplyDeclaration(string property, string value)
    {
        value = value.Trim();
        switch (property.Trim().ToLowerInvariant())
        {
            case "fill":
                if (SvgColor.TryParse(value, CurrentColor, out var f, out var fNone))
                    Fill = fNone ? null : f;
                break;
            case "stroke":
                if (SvgColor.TryParse(value, CurrentColor, out var s, out var sNone))
                    Stroke = sNone ? null : s;
                break;
            case "stroke-width":
                if (TryLength(value, out var sw)) StrokeWidth = sw;
                break;
            case "fill-rule":
                FillEvenOdd = value.Equals("evenodd", StringComparison.OrdinalIgnoreCase);
                break;
            case "opacity":
                if (TryNumber(value, out var o)) Opacity = Math.Clamp(o, 0f, 1f);
                break;
            case "fill-opacity":
                if (TryNumber(value, out var fo)) FillOpacity = Math.Clamp(fo, 0f, 1f);
                break;
            case "stroke-opacity":
                if (TryNumber(value, out var so)) StrokeOpacity = Math.Clamp(so, 0f, 1f);
                break;
            case "color":
                if (SvgColor.TryParse(value, CurrentColor, out var cc, out var ccNone) && !ccNone)
                    CurrentColor = cc;
                break;
        }
    }

    private static bool TryNumber(string v, out float value)
        => float.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Parse a length that may carry a <c>px</c> suffix (the only unit icons
    /// use for stroke width in practice); other units are ignored as 0-stripped
    /// numbers. Percentages are not supported here (rare for stroke-width).
    /// </summary>
    private static bool TryLength(string v, out float value)
    {
        v = v.Trim();
        if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase)) v = v[..^2].Trim();
        return TryNumber(v, out value);
    }

    /// <summary>
    /// Parse a <c>style="a:b;c:d"</c> string into this style.
    /// </summary>
    public void ApplyStyleString(string? style)
    {
        if (string.IsNullOrWhiteSpace(style)) return;
        foreach (var decl in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = decl.IndexOf(':');
            if (colon <= 0) continue;
            ApplyDeclaration(decl[..colon], decl[(colon + 1)..]);
        }
    }
}
