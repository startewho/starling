using Tessera.Css.Cascade;
using Tessera.Css.Properties;
using Tessera.Css.Values;

namespace Tessera.Layout.Text;

/// <summary>
/// The font selection layout needs for measurement: a prioritised family list
/// plus bold/italic flags. Layout doesn't resolve this to a concrete typeface
/// — that's the paint module's job. The Skia-backed measurer resolves and
/// caches per spec so each call doesn't re-walk the family list.
/// <para>
/// <see cref="Equals(FontSpec?)"/> compares families element-wise so two
/// instances built from the same cascade compare equal (the default record
/// <c>Equals</c> for a reference-typed list would use reference equality and
/// blow caches keyed on FontSpec).
/// </para>
/// </summary>
public sealed record FontSpec(IReadOnlyList<string> Families, bool Bold, bool Italic)
{
    public static readonly FontSpec Default = new(["sans-serif"], false, false);

    public bool Equals(FontSpec? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (Bold != other.Bold || Italic != other.Italic) return false;
        if (Families.Count != other.Families.Count) return false;
        for (var i = 0; i < Families.Count; i++)
        {
            if (!string.Equals(Families[i], other.Families[i], StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Bold);
        hash.Add(Italic);
        foreach (var f in Families) hash.Add(f, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Builds a <see cref="FontSpec"/> from a computed style. Used by both
    /// inline layout (to call the measurer) and the display-list builder (to
    /// stamp DrawText records) so the two see the same families/bold/italic.
    /// </summary>
    public static FontSpec FromStyle(ComputedStyle? style)
    {
        if (style is null) return Default;
        return new FontSpec(ExtractFamilies(style), IsBold(style), IsItalic(style));
    }

    private static IReadOnlyList<string> ExtractFamilies(ComputedStyle style)
    {
        // The CSS value parser leaves commas in CssValueList as empty-name
        // CssKeywords (the tokenizer emits a Comma token with Value=""); skip
        // those. Falls back to "sans-serif" when nothing usable parses.
        var value = style.Get(PropertyId.FontFamily);
        switch (value)
        {
            case CssKeyword kw when kw.Name.Length > 0: return new[] { kw.Name };
            case CssString s when s.Value.Length > 0: return new[] { s.Value };
            case CssValueList vl:
                var families = new List<string>(vl.Values.Count);
                foreach (var v in vl.Values)
                {
                    switch (v)
                    {
                        case CssKeyword k when k.Name.Length > 0: families.Add(k.Name); break;
                        case CssString str when str.Value.Length > 0: families.Add(str.Value); break;
                    }
                }
                if (families.Count > 0) return families;
                break;
        }
        return Default.Families;
    }

    private static bool IsBold(ComputedStyle style)
        => style.Get(PropertyId.FontWeight) switch
        {
            CssKeyword { Name: "bold" } => true,
            CssNumber n => n.Value >= 600,
            _ => false,
        };

    private static bool IsItalic(ComputedStyle style)
        => style.Get(PropertyId.FontStyle) is CssKeyword { Name: "italic" or "oblique" };
}

/// <summary>
/// Layout's text-measurement seam. The paint module supplies the real
/// implementation backed by font metrics; layout keeps a coarse fallback so
/// it can produce a usable tree without a paint dependency.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>Advance width in CSS px for <paramref name="text"/> at the given font size and spec.</summary>
    double MeasureWidth(string text, double fontSize, FontSpec spec);

    /// <summary>Line-height for the given font at the given size when CSS <c>line-height: normal</c> applies.</summary>
    double NormalLineHeight(double fontSize, FontSpec spec);

    /// <summary>Distance from the top of the line box to the alphabetic baseline.</summary>
    double Baseline(double fontSize, FontSpec spec);
}

/// <summary>
/// Default measurer: assumes a roughly proportional sans-serif with an average
/// glyph advance of ~0.5em. Good enough for layout to wrap text and choose
/// line counts; precise paint metrics come from the paint module. Ignores the
/// FontSpec — the heuristic is family-agnostic.
/// </summary>
public sealed class DefaultTextMeasurer : ITextMeasurer
{
    public static readonly DefaultTextMeasurer Instance = new();

    public double MeasureWidth(string text, double fontSize, FontSpec spec)
    {
        ArgumentNullException.ThrowIfNull(text);
        double total = 0;
        foreach (var c in text)
        {
            total += AverageAdvanceFactor(c) * fontSize;
        }
        return total;
    }

    public double NormalLineHeight(double fontSize, FontSpec spec) => fontSize * 1.2;

    public double Baseline(double fontSize, FontSpec spec) => fontSize * 0.8;

    private static double AverageAdvanceFactor(char c) => c switch
    {
        ' ' or '\t' => 0.28,
        'i' or 'l' or 'I' or '|' or '!' or '.' or ',' or ';' or ':' or '\'' or '`' => 0.28,
        'm' or 'M' or 'w' or 'W' => 0.85,
        _ when c >= '0' && c <= '9' => 0.55,
        _ when char.IsUpper(c) => 0.65,
        _ when char.IsLower(c) => 0.52,
        _ => 0.5,
    };
}
