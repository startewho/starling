using System.Globalization;
using System.Text;

namespace Starling.Layout.Tree;

/// <summary>
/// CSS Lists 3 §3 — list-item marker text generation. Maps a
/// <c>list-style-type</c> keyword + ordinal to the string a marker box renders
/// (e.g. <c>decimal</c> ordinal 3 → <c>"3."</c>, <c>upper-roman</c> ordinal 4 →
/// <c>"IV."</c>). Glyph markers (<c>disc</c>/<c>circle</c>/<c>square</c>) map to
/// the Unicode bullet characters browsers use. Counter-style numbering is
/// table-driven so the tedious roman/alpha algorithms stay in one place.
/// </summary>
internal static class ListMarker
{
    /// <summary>Unicode bullet for <c>disc</c> (U+2022 BULLET).</summary>
    public const string Disc = "•";

    /// <summary>Unicode bullet for <c>circle</c> (U+25E6 WHITE BULLET).</summary>
    public const string Circle = "◦";

    /// <summary>Unicode bullet for <c>square</c> (U+25AA BLACK SMALL SQUARE).</summary>
    public const string Square = "▪";

    /// <summary>
    /// Returns the rendered marker text for <paramref name="listStyleType"/> at
    /// the 1-based <paramref name="ordinal"/>, or null when the type is
    /// <c>none</c> (no marker) or unrecognised.
    /// </summary>
    public static string? Render(string listStyleType, int ordinal)
    {
        var type = listStyleType.Trim().ToLowerInvariant();
        switch (type)
        {
            case "none":
                return null;
            case "disc":
                return Disc;
            case "circle":
                return Circle;
            case "square":
                return Square;
        }

        // Numeric / alphabetic counter styles render the ordinal followed by a
        // ". " suffix per the UA's list-item marker formatting (CSS Lists 3 §3.2
        // — the suffix for numeric/alphabetic predefined styles is "." + space).
        var counter = Counter(type, ordinal);
        return counter is null ? null : counter + ".";
    }

    /// <summary>
    /// The counter representation (without suffix) for a numeric/alphabetic
    /// <paramref name="type"/> at <paramref name="ordinal"/>. Returns null for
    /// glyph or unknown types. Out-of-range ordinals for bounded systems
    /// (roman, alphabetic) fall back to decimal per §3.2's range clause.
    /// </summary>
    private static string? Counter(string type, int ordinal) => type switch
    {
        "decimal" => ordinal.ToString(CultureInfo.InvariantCulture),
        "decimal-leading-zero" => DecimalLeadingZero(ordinal),
        "lower-alpha" or "lower-latin" => Alphabetic(ordinal, 'a'),
        "upper-alpha" or "upper-latin" => Alphabetic(ordinal, 'A'),
        "lower-roman" => Roman(ordinal, upper: false),
        "upper-roman" => Roman(ordinal, upper: true),
        "lower-greek" => LowerGreek(ordinal),
        _ => null,
    };

    private static string DecimalLeadingZero(int n)
    {
        // CSS Lists 3 §3.2 — pad to at least two digits.
        var s = n.ToString(CultureInfo.InvariantCulture);
        return n is >= 0 and < 10 ? "0" + s : s;
    }

    /// <summary>
    /// Bijective base-26 alphabetic numbering (a, b, …, z, aa, ab, …).
    /// Ordinals &lt; 1 fall back to decimal (the alphabetic system has no
    /// representation for them).
    /// </summary>
    private static string Alphabetic(int n, char first)
    {
        if (n < 1) return n.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        while (n > 0)
        {
            n--; // bijective: 1 → first letter
            sb.Insert(0, (char)(first + n % 26));
            n /= 26;
        }
        return sb.ToString();
    }

    private static readonly (int Value, string Upper, string Lower)[] RomanTable =
    [
        (1000, "M", "m"),
        (900, "CM", "cm"),
        (500, "D", "d"),
        (400, "CD", "cd"),
        (100, "C", "c"),
        (90, "XC", "xc"),
        (50, "L", "l"),
        (40, "XL", "xl"),
        (10, "X", "x"),
        (9, "IX", "ix"),
        (5, "V", "v"),
        (4, "IV", "iv"),
        (1, "I", "i"),
    ];

    /// <summary>
    /// Roman numeral for 1..3999. Outside that range the system has no
    /// representation, so we fall back to decimal per §3.2.
    /// </summary>
    private static string Roman(int n, bool upper)
    {
        if (n is < 1 or > 3999) return n.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        foreach (var (value, up, low) in RomanTable)
        {
            while (n >= value)
            {
                sb.Append(upper ? up : low);
                n -= value;
            }
        }
        return sb.ToString();
    }

    private const string GreekLetters = "αβγδεζηθικλμνξοπρστυφχψω";

    private static string LowerGreek(int n)
    {
        if (n < 1) return n.ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        var count = GreekLetters.Length;
        while (n > 0)
        {
            n--;
            sb.Insert(0, GreekLetters[n % count]);
            n /= count;
        }
        return sb.ToString();
    }
}
