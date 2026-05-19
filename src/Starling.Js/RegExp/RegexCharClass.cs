using System.Globalization;

namespace Starling.Js.RegExp;

/// <summary>
/// A character class: a union of (lo, hi) UTF-16 code unit ranges, with
/// optional negation. Supports the ES2024 predefined classes (\d, \w, \s) and
/// the small unicode-property whitelist documented in B4-1.
/// </summary>
public sealed class RegexCharClass
{
    private readonly List<(int Lo, int Hi)> _ranges;
    public bool Negated { get; }
    public bool CaseInsensitive { get; }

    public RegexCharClass(List<(int Lo, int Hi)> ranges, bool negated, bool caseInsensitive)
    {
        _ranges = ranges;
        Negated = negated;
        CaseInsensitive = caseInsensitive;
        SortAndCoalesce();
    }

    private void SortAndCoalesce()
    {
        if (_ranges.Count == 0) return;
        _ranges.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        var merged = new List<(int Lo, int Hi)>();
        var cur = _ranges[0];
        for (var i = 1; i < _ranges.Count; i++)
        {
            var r = _ranges[i];
            if (r.Lo <= cur.Hi + 1) cur = (cur.Lo, System.Math.Max(cur.Hi, r.Hi));
            else { merged.Add(cur); cur = r; }
        }
        merged.Add(cur);
        _ranges.Clear();
        _ranges.AddRange(merged);
    }

    public bool Contains(int codePoint)
    {
        bool hit = ContainsExact(codePoint);
        if (!hit && CaseInsensitive)
        {
            // Try the upper/lower fold of the codepoint, for code units only.
            if (codePoint <= 0xFFFF)
            {
                var c = (char)codePoint;
                if (ContainsExact(char.ToLowerInvariant(c))) hit = true;
                else if (ContainsExact(char.ToUpperInvariant(c))) hit = true;
            }
        }
        return Negated ? !hit : hit;
    }

    private bool ContainsExact(int codePoint)
    {
        foreach (var (lo, hi) in _ranges)
            if (codePoint >= lo && codePoint <= hi) return true;
        return false;
    }

    // ---------------- Builders for predefined classes ----------------

    public static List<(int, int)> Digits() => new() { ('0', '9') };
    public static List<(int, int)> Word() => new() { ('0', '9'), ('A', 'Z'), ('_', '_'), ('a', 'z') };
    public static List<(int, int)> Whitespace() => new()
    {
        (0x09, 0x0D), (0x20, 0x20), (0xA0, 0xA0), (0x1680, 0x1680),
        (0x2000, 0x200A), (0x2028, 0x2029), (0x202F, 0x202F),
        (0x205F, 0x205F), (0x3000, 0x3000), (0xFEFF, 0xFEFF),
    };

    public static bool IsWordChar(int cp)
    {
        if (cp >= '0' && cp <= '9') return true;
        if (cp >= 'A' && cp <= 'Z') return true;
        if (cp == '_') return true;
        if (cp >= 'a' && cp <= 'z') return true;
        return false;
    }

    public static bool IsLineTerminator(int cp) =>
        cp == 0x0A || cp == 0x0D || cp == 0x2028 || cp == 0x2029;

    public static bool IsWhitespace(int cp)
    {
        foreach (var (lo, hi) in Whitespace()) if (cp >= lo && cp <= hi) return true;
        return false;
    }

    // ---------------- Unicode property whitelist ----------------

    /// <summary>The subset of Unicode property names B4-1 supports. Unknown
    /// names should throw SyntaxError at parse time.</summary>
    public static readonly System.Collections.Generic.HashSet<string> SupportedProperties = new()
    {
        "Letter", "L",
        "Number", "N",
        "Decimal_Number", "Nd",
        "White_Space",
        "Punctuation", "P",
        "Symbol", "S",
        "Uppercase_Letter", "Lu",
        "Lowercase_Letter", "Ll",
    };

    /// <summary>Test a single code unit against a supported property. We use
    /// .NET's <see cref="CharUnicodeInfo"/> for the basic plane; supplementary
    /// code points get conservative answers.</summary>
    public static bool MatchesProperty(int cp, string property)
    {
        if (cp > 0xFFFF) cp = '?'; // simplified — only the BMP
        var c = (char)cp;
        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return property switch
        {
            "Letter" or "L" => char.IsLetter(c),
            "Number" or "N" => char.IsNumber(c),
            "Decimal_Number" or "Nd" => cat == UnicodeCategory.DecimalDigitNumber,
            "White_Space" => IsWhitespace(c),
            "Punctuation" or "P" => char.IsPunctuation(c),
            "Symbol" or "S" => char.IsSymbol(c),
            "Uppercase_Letter" or "Lu" => cat == UnicodeCategory.UppercaseLetter,
            "Lowercase_Letter" or "Ll" => cat == UnicodeCategory.LowercaseLetter,
            _ => false,
        };
    }
}
