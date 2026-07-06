// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace Starling.RegExp;

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
        _ranges = new List<(int, int)>(ranges); // copy so caller can pass cached/static lists
        Negated = negated;
        CaseInsensitive = caseInsensitive;
        SortAndCoalesce();
    }

    /// <summary>Copies this class's (already coalesced) ranges into
    /// <paramref name="dest"/>; a negated class copies its complement. Used by
    /// the v-flag set-expression parser, which does its algebra on raw range
    /// lists.</summary>
    public void CopyRangesInto(List<(int Lo, int Hi)> dest)
    {
        if (!Negated)
        {
            dest.AddRange(_ranges);
            return;
        }

        var cursor = 0;
        foreach (var (lo, hi) in _ranges)
        {
            if (lo > cursor)
            {
                dest.Add((cursor, lo - 1));
            }

            cursor = hi + 1;
        }

        if (cursor <= 0x10FFFF)
        {
            dest.Add((cursor, 0x10FFFF));
        }
    }

    private void SortAndCoalesce()
    {
        if (_ranges.Count == 0)
        {
            return;
        }

        _ranges.Sort((a, b) => a.Lo.CompareTo(b.Lo));
        var merged = new List<(int Lo, int Hi)>();
        var cur = _ranges[0];
        for (var i = 1; i < _ranges.Count; i++)
        {
            var r = _ranges[i];
            if (r.Lo <= cur.Hi + 1)
            {
                cur = (cur.Lo, System.Math.Max(cur.Hi, r.Hi));
            }
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
                if (ContainsExact(char.ToLowerInvariant(c)))
                {
                    hit = true;
                }
                else if (ContainsExact(char.ToUpperInvariant(c)))
                {
                    hit = true;
                }
            }
        }
        return Negated ? !hit : hit;
    }

    private bool ContainsExact(int codePoint)
    {
        foreach (var (lo, hi) in _ranges)
        {
            if (codePoint >= lo && codePoint <= hi)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------- Builders for predefined classes ----------------

    private static readonly List<(int, int)> _digits = new() { ('0', '9') };
    private static readonly List<(int, int)> _word = new() { ('0', '9'), ('A', 'Z'), ('_', '_'), ('a', 'z') };
    private static readonly List<(int, int)> _whitespace = new()
    {
        (0x09, 0x0D), (0x20, 0x20), (0xA0, 0xA0), (0x1680, 0x1680),
        (0x2000, 0x200A), (0x2028, 0x2029), (0x202F, 0x202F),
        (0x205F, 0x205F), (0x3000, 0x3000), (0xFEFF, 0xFEFF),
    };

    public static List<(int, int)> Digits() => _digits;
    public static List<(int, int)> Word() => _word;
    public static List<(int, int)> Whitespace() => _whitespace;

    public static bool IsWordChar(int cp)
    {
        if (cp >= '0' && cp <= '9')
        {
            return true;
        }

        if (cp >= 'A' && cp <= 'Z')
        {
            return true;
        }

        if (cp == '_')
        {
            return true;
        }

        if (cp >= 'a' && cp <= 'z')
        {
            return true;
        }

        return false;
    }

    public static bool IsLineTerminator(int cp) =>
        cp == 0x0A || cp == 0x0D || cp == 0x2028 || cp == 0x2029;

    public static bool IsWhitespace(int cp)
    {
        foreach (var (lo, hi) in Whitespace())
        {
            if (cp >= lo && cp <= hi)
            {
                return true;
            }
        }

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
        "ID_Start", "IDS",
        "ID_Continue", "IDC",
        // General categories (long + short forms) via UnicodeCategory.
        "Cased_Letter", "LC", "Titlecase_Letter", "Lt", "Modifier_Letter", "Lm", "Other_Letter", "Lo",
        "Mark", "M", "Nonspacing_Mark", "Mn", "Spacing_Mark", "Mc", "Enclosing_Mark", "Me",
        "Letter_Number", "Nl", "Other_Number", "No",
        "Connector_Punctuation", "Pc", "Dash_Punctuation", "Pd",
        "Open_Punctuation", "Ps", "Close_Punctuation", "Pe",
        "Initial_Punctuation", "Pi", "Final_Punctuation", "Pf", "Other_Punctuation", "Po",
        "Math_Symbol", "Sm", "Currency_Symbol", "Sc", "Modifier_Symbol", "Sk", "Other_Symbol", "So",
        "Separator", "Z", "Space_Separator", "Zs", "Line_Separator", "Zl", "Paragraph_Separator", "Zp",
        "Other", "C", "Control", "Cc", "Format", "Cf", "Surrogate", "Cs",
        "Private_Use", "Co", "Unassigned", "Cn",
        // Binary properties.
        "ASCII", "ASCII_Hex_Digit", "AHex", "Hex_Digit", "Hex",
        "Alphabetic", "Alpha", "Any", "Assigned",
        "Cased", "Lowercase", "Lower", "Uppercase", "Upper",
        "Math", "Dash", "Ideographic", "Ideo",
        "Default_Ignorable_Code_Point", "DI",
        // Emoji family (UTS #51) — static SMP-inclusive tables below.
        "Emoji", "Emoji_Presentation", "EPres", "Emoji_Modifier", "EMod",
        "Emoji_Modifier_Base", "EBase", "Emoji_Component", "EComp",
        "Extended_Pictographic", "ExtPict",
    };

    /// <summary>Properties whose members live mostly ABOVE the BMP — provided
    /// as literal range tables (UTS #51 data) because the BMP scan in
    /// BuildPropertyRanges cannot see them.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, (int, int)[]> _astralProperties
        = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["Emoji"] = new[]
        {
            (0x23, 0x23), (0x2A, 0x2A), (0x30, 0x39), (0xA9, 0xA9), (0xAE, 0xAE),
            (0x203C, 0x203C), (0x2049, 0x2049), (0x2122, 0x2122), (0x2139, 0x2139),
            (0x2194, 0x2199), (0x21A9, 0x21AA), (0x231A, 0x231B), (0x2328, 0x2328),
            (0x23CF, 0x23CF), (0x23E9, 0x23F3), (0x23F8, 0x23FA), (0x24C2, 0x24C2),
            (0x25AA, 0x25AB), (0x25B6, 0x25B6), (0x25C0, 0x25C0), (0x25FB, 0x25FE),
            (0x2600, 0x2604), (0x260E, 0x260E), (0x2611, 0x2611), (0x2614, 0x2615),
            (0x2618, 0x2618), (0x261D, 0x261D), (0x2620, 0x2620), (0x2622, 0x2623),
            (0x2626, 0x2626), (0x262A, 0x262A), (0x262E, 0x262F), (0x2638, 0x263A),
            (0x2640, 0x2640), (0x2642, 0x2642), (0x2648, 0x2653), (0x265F, 0x2660),
            (0x2663, 0x2663), (0x2665, 0x2666), (0x2668, 0x2668), (0x267B, 0x267B),
            (0x267E, 0x267F), (0x2692, 0x2697), (0x2699, 0x2699), (0x269B, 0x269C),
            (0x26A0, 0x26A1), (0x26A7, 0x26A7), (0x26AA, 0x26AB), (0x26B0, 0x26B1),
            (0x26BD, 0x26BE), (0x26C4, 0x26C5), (0x26C8, 0x26C8), (0x26CE, 0x26CF),
            (0x26D1, 0x26D1), (0x26D3, 0x26D4), (0x26E9, 0x26EA), (0x26F0, 0x26F5),
            (0x26F7, 0x26FA), (0x26FD, 0x26FD), (0x2702, 0x2702), (0x2705, 0x2705),
            (0x2708, 0x270D), (0x270F, 0x270F), (0x2712, 0x2712), (0x2714, 0x2714),
            (0x2716, 0x2716), (0x271D, 0x271D), (0x2721, 0x2721), (0x2728, 0x2728),
            (0x2733, 0x2734), (0x2744, 0x2744), (0x2747, 0x2747), (0x274C, 0x274C),
            (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2763, 0x2764),
            (0x2795, 0x2797), (0x27A1, 0x27A1), (0x27B0, 0x27B0), (0x27BF, 0x27BF),
            (0x2934, 0x2935), (0x2B05, 0x2B07), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50),
            (0x2B55, 0x2B55), (0x3030, 0x3030), (0x303D, 0x303D), (0x3297, 0x3297),
            (0x3299, 0x3299), (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF),
            (0x1F170, 0x1F171), (0x1F17E, 0x1F17F), (0x1F18E, 0x1F18E),
            (0x1F191, 0x1F19A), (0x1F1E6, 0x1F1FF), (0x1F201, 0x1F202),
            (0x1F21A, 0x1F21A), (0x1F22F, 0x1F22F), (0x1F232, 0x1F23A),
            (0x1F250, 0x1F251), (0x1F300, 0x1F321), (0x1F324, 0x1F393),
            (0x1F396, 0x1F397), (0x1F399, 0x1F39B), (0x1F39E, 0x1F3F0),
            (0x1F3F3, 0x1F3F5), (0x1F3F7, 0x1F4FD), (0x1F4FF, 0x1F53D),
            (0x1F549, 0x1F54E), (0x1F550, 0x1F567), (0x1F56F, 0x1F570),
            (0x1F573, 0x1F57A), (0x1F587, 0x1F587), (0x1F58A, 0x1F58D),
            (0x1F590, 0x1F590), (0x1F595, 0x1F596), (0x1F5A4, 0x1F5A5),
            (0x1F5A8, 0x1F5A8), (0x1F5B1, 0x1F5B2), (0x1F5BC, 0x1F5BC),
            (0x1F5C2, 0x1F5C4), (0x1F5D1, 0x1F5D3), (0x1F5DC, 0x1F5DE),
            (0x1F5E1, 0x1F5E1), (0x1F5E3, 0x1F5E3), (0x1F5E8, 0x1F5E8),
            (0x1F5EF, 0x1F5EF), (0x1F5F3, 0x1F5F3), (0x1F5FA, 0x1F64F),
            (0x1F680, 0x1F6C5), (0x1F6CB, 0x1F6D2), (0x1F6D5, 0x1F6D7),
            (0x1F6DC, 0x1F6E5), (0x1F6E9, 0x1F6E9), (0x1F6EB, 0x1F6EC),
            (0x1F6F0, 0x1F6F0), (0x1F6F3, 0x1F6FC), (0x1F7E0, 0x1F7EB),
            (0x1F7F0, 0x1F7F0), (0x1F90C, 0x1F93A), (0x1F93C, 0x1F945),
            (0x1F947, 0x1F9FF), (0x1FA70, 0x1FA7C), (0x1FA80, 0x1FA88),
            (0x1FA90, 0x1FABD), (0x1FABF, 0x1FAC5), (0x1FACE, 0x1FADB),
            (0x1FAE0, 0x1FAE8), (0x1FAF0, 0x1FAF8),
        },
        ["Emoji_Presentation"] = new[]
        {
            (0x231A, 0x231B), (0x23E9, 0x23EC), (0x23F0, 0x23F0), (0x23F3, 0x23F3),
            (0x25FD, 0x25FE), (0x2614, 0x2615), (0x2648, 0x2653), (0x267F, 0x267F),
            (0x2693, 0x2693), (0x26A1, 0x26A1), (0x26AA, 0x26AB), (0x26BD, 0x26BE),
            (0x26C4, 0x26C5), (0x26CE, 0x26CE), (0x26D4, 0x26D4), (0x26EA, 0x26EA),
            (0x26F2, 0x26F3), (0x26F5, 0x26F5), (0x26FA, 0x26FA), (0x26FD, 0x26FD),
            (0x2705, 0x2705), (0x270A, 0x270B), (0x2728, 0x2728), (0x274C, 0x274C),
            (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757), (0x2795, 0x2797),
            (0x27B0, 0x27B0), (0x27BF, 0x27BF), (0x2B1B, 0x2B1C), (0x2B50, 0x2B50),
            (0x2B55, 0x2B55), (0x1F004, 0x1F004), (0x1F0CF, 0x1F0CF),
            (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A), (0x1F1E6, 0x1F1FF),
            (0x1F201, 0x1F201), (0x1F21A, 0x1F21A), (0x1F22F, 0x1F22F),
            (0x1F232, 0x1F236), (0x1F238, 0x1F23A), (0x1F250, 0x1F251),
            (0x1F300, 0x1F320), (0x1F32D, 0x1F335), (0x1F337, 0x1F37C),
            (0x1F37E, 0x1F393), (0x1F3A0, 0x1F3CA), (0x1F3CF, 0x1F3D3),
            (0x1F3E0, 0x1F3F0), (0x1F3F4, 0x1F3F4), (0x1F3F8, 0x1F43E),
            (0x1F440, 0x1F440), (0x1F442, 0x1F4FC), (0x1F4FF, 0x1F53D),
            (0x1F54B, 0x1F54E), (0x1F550, 0x1F567), (0x1F57A, 0x1F57A),
            (0x1F595, 0x1F596), (0x1F5A4, 0x1F5A4), (0x1F5FB, 0x1F64F),
            (0x1F680, 0x1F6C5), (0x1F6CC, 0x1F6CC), (0x1F6D0, 0x1F6D2),
            (0x1F6D5, 0x1F6D7), (0x1F6DC, 0x1F6DF), (0x1F6EB, 0x1F6EC),
            (0x1F6F4, 0x1F6FC), (0x1F7E0, 0x1F7EB), (0x1F7F0, 0x1F7F0),
            (0x1F90C, 0x1F93A), (0x1F93C, 0x1F945), (0x1F947, 0x1F9FF),
            (0x1FA70, 0x1FA7C), (0x1FA80, 0x1FA88), (0x1FA90, 0x1FABD),
            (0x1FABF, 0x1FAC5), (0x1FACE, 0x1FADB), (0x1FAE0, 0x1FAE8),
            (0x1FAF0, 0x1FAF8),
        },
        ["Emoji_Modifier"] = new[] { (0x1F3FB, 0x1F3FF) },
        ["Emoji_Modifier_Base"] = new[]
        {
            (0x261D, 0x261D), (0x26F9, 0x26F9), (0x270A, 0x270D),
            (0x1F385, 0x1F385), (0x1F3C2, 0x1F3C4), (0x1F3C7, 0x1F3C7),
            (0x1F3CA, 0x1F3CC), (0x1F442, 0x1F443), (0x1F446, 0x1F450),
            (0x1F466, 0x1F478), (0x1F47C, 0x1F47C), (0x1F481, 0x1F483),
            (0x1F485, 0x1F487), (0x1F48F, 0x1F48F), (0x1F491, 0x1F491),
            (0x1F4AA, 0x1F4AA), (0x1F574, 0x1F575), (0x1F57A, 0x1F57A),
            (0x1F590, 0x1F590), (0x1F595, 0x1F596), (0x1F645, 0x1F647),
            (0x1F64B, 0x1F64F), (0x1F6A3, 0x1F6A3), (0x1F6B4, 0x1F6B6),
            (0x1F6C0, 0x1F6C0), (0x1F6CC, 0x1F6CC), (0x1F90C, 0x1F90C),
            (0x1F90F, 0x1F90F), (0x1F918, 0x1F91F), (0x1F926, 0x1F926),
            (0x1F930, 0x1F939), (0x1F93C, 0x1F93E), (0x1F977, 0x1F977),
            (0x1F9B5, 0x1F9B6), (0x1F9B8, 0x1F9B9), (0x1F9BB, 0x1F9BB),
            (0x1F9CD, 0x1F9DD),
        },
        ["Emoji_Component"] = new[]
        {
            (0x23, 0x23), (0x2A, 0x2A), (0x30, 0x39), (0x200D, 0x200D),
            (0x20E3, 0x20E3), (0xFE0F, 0xFE0F), (0x1F1E6, 0x1F1FF),
            (0x1F3FB, 0x1F3FF), (0x1F9B0, 0x1F9B3), (0xE0020, 0xE007F),
        },
        ["Extended_Pictographic"] = new[]
        {
            (0xA9, 0xA9), (0xAE, 0xAE), (0x203C, 0x203C), (0x2049, 0x2049),
            (0x2122, 0x2122), (0x2139, 0x2139), (0x2194, 0x2199), (0x21A9, 0x21AA),
            (0x231A, 0x231B), (0x2328, 0x2328), (0x2388, 0x2388), (0x23CF, 0x23CF),
            (0x23E9, 0x23F3), (0x23F8, 0x23FA), (0x24C2, 0x24C2), (0x25AA, 0x25AB),
            (0x25B6, 0x25B6), (0x25C0, 0x25C0), (0x25FB, 0x25FE), (0x2600, 0x2605),
            (0x2607, 0x2612), (0x2614, 0x2685), (0x2690, 0x2705), (0x2708, 0x2712),
            (0x2714, 0x2714), (0x2716, 0x2716), (0x271D, 0x271D), (0x2721, 0x2721),
            (0x2728, 0x2728), (0x2733, 0x2734), (0x2744, 0x2744), (0x2747, 0x2747),
            (0x274C, 0x274C), (0x274E, 0x274E), (0x2753, 0x2755), (0x2757, 0x2757),
            (0x2763, 0x2767), (0x2795, 0x2797), (0x27A1, 0x27A1), (0x27B0, 0x27B0),
            (0x27BF, 0x27BF), (0x2934, 0x2935), (0x2B05, 0x2B07), (0x2B1B, 0x2B1C),
            (0x2B50, 0x2B50), (0x2B55, 0x2B55), (0x3030, 0x3030), (0x303D, 0x303D),
            (0x3297, 0x3297), (0x3299, 0x3299), (0x1F000, 0x1F0FF),
            (0x1F10D, 0x1F10F), (0x1F12F, 0x1F12F), (0x1F16C, 0x1F171),
            (0x1F17E, 0x1F17F), (0x1F18E, 0x1F18E), (0x1F191, 0x1F19A),
            (0x1F1AD, 0x1F1E5), (0x1F201, 0x1F20F), (0x1F21A, 0x1F21A),
            (0x1F22F, 0x1F22F), (0x1F232, 0x1F23A), (0x1F23C, 0x1F23F),
            (0x1F249, 0x1F3FA), (0x1F400, 0x1F53D), (0x1F546, 0x1F64F),
            (0x1F680, 0x1F6FF), (0x1F774, 0x1F77F), (0x1F7D5, 0x1F7FF),
            (0x1F80C, 0x1F80F), (0x1F848, 0x1F84F), (0x1F85A, 0x1F85F),
            (0x1F888, 0x1F88F), (0x1F8AE, 0x1F8FF), (0x1F90C, 0x1F93A),
            (0x1F93C, 0x1F945), (0x1F947, 0x1FAFF), (0x1FC00, 0x1FFFD),
        },
    };

    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(int, int)>> _propertyRanges
        = BuildPropertyRanges();

    private static System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(int, int)>> BuildPropertyRanges()
    {
        var dict = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(int, int)>>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var name in SupportedProperties)
        {
            if (dict.ContainsKey(name))
            {
                continue;
            }

            // Emoji-family properties come from the static SMP-inclusive
            // tables — the BMP scan below cannot see astral members.
            if (TryGetAstral(name, out var astral))
            {
                dict[name] = new System.Collections.Generic.List<(int, int)>(astral);
                continue;
            }

            var ranges = new System.Collections.Generic.List<(int, int)>();
            int? rangeStart = null;
            for (var cp = 0; cp <= 0xFFFF; cp++)
            {
                if (MatchesProperty(cp, name))
                {
                    rangeStart ??= cp;
                }
                else if (rangeStart.HasValue)
                {
                    ranges.Add((rangeStart.Value, cp - 1));
                    rangeStart = null;
                }
            }
            if (rangeStart.HasValue)
            {
                ranges.Add((rangeStart.Value, 0xFFFF));
            }
            // store a coalesced copy (ctor will copy again but cheap)
            dict[name] = ranges;
            // also store canonical short names if not present
        }
        return dict;
    }

    /// <summary>Returns a (cached) list of ranges for the given property name (without negation/case).
    /// The caller must not mutate the returned list.
    /// </summary>
    public static System.Collections.Generic.List<(int, int)> GetPropertyRanges(string name)
    {
        if (_propertyRanges.TryGetValue(name, out var ranges))
        {
            return ranges;
        }
        // fallback (should not happen after Supported check)
        return new System.Collections.Generic.List<(int, int)>();
    }

    /// <summary>Test a single code unit against a supported property. We use
    /// .NET's <see cref="CharUnicodeInfo"/> for the basic plane; supplementary
    /// code points get conservative answers.</summary>
    private static bool TryGetAstral(string name, out (int, int)[] ranges)
    {
        var canonical = name switch
        {
            "EPres" => "Emoji_Presentation",
            "EMod" => "Emoji_Modifier",
            "EBase" => "Emoji_Modifier_Base",
            "EComp" => "Emoji_Component",
            "ExtPict" => "Extended_Pictographic",
            _ => name,
        };
        return _astralProperties.TryGetValue(canonical, out ranges!);
    }

    public static bool MatchesProperty(int cp, string property)
    {
        if (cp > 0xFFFF)
        {
            cp = '?'; // simplified — only the BMP
        }

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
            "ID_Start" or "IDS" => IsIdStart(c, cat),
            "ID_Continue" or "IDC" => IsIdContinue(c, cat),
            "Cased_Letter" or "LC" => cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter,
            "Titlecase_Letter" or "Lt" => cat == UnicodeCategory.TitlecaseLetter,
            "Modifier_Letter" or "Lm" => cat == UnicodeCategory.ModifierLetter,
            "Other_Letter" or "Lo" => cat == UnicodeCategory.OtherLetter,
            "Mark" or "M" => cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark,
            "Nonspacing_Mark" or "Mn" => cat == UnicodeCategory.NonSpacingMark,
            "Spacing_Mark" or "Mc" => cat == UnicodeCategory.SpacingCombiningMark,
            "Enclosing_Mark" or "Me" => cat == UnicodeCategory.EnclosingMark,
            "Letter_Number" or "Nl" => cat == UnicodeCategory.LetterNumber,
            "Other_Number" or "No" => cat == UnicodeCategory.OtherNumber,
            "Connector_Punctuation" or "Pc" => cat == UnicodeCategory.ConnectorPunctuation,
            "Dash_Punctuation" or "Pd" => cat == UnicodeCategory.DashPunctuation,
            "Open_Punctuation" or "Ps" => cat == UnicodeCategory.OpenPunctuation,
            "Close_Punctuation" or "Pe" => cat == UnicodeCategory.ClosePunctuation,
            "Initial_Punctuation" or "Pi" => cat == UnicodeCategory.InitialQuotePunctuation,
            "Final_Punctuation" or "Pf" => cat == UnicodeCategory.FinalQuotePunctuation,
            "Other_Punctuation" or "Po" => cat == UnicodeCategory.OtherPunctuation,
            "Math_Symbol" or "Sm" => cat == UnicodeCategory.MathSymbol,
            "Currency_Symbol" or "Sc" => cat == UnicodeCategory.CurrencySymbol,
            "Modifier_Symbol" or "Sk" => cat == UnicodeCategory.ModifierSymbol,
            "Other_Symbol" or "So" => cat == UnicodeCategory.OtherSymbol,
            "Separator" or "Z" => char.IsSeparator(c),
            "Space_Separator" or "Zs" => cat == UnicodeCategory.SpaceSeparator,
            "Line_Separator" or "Zl" => cat == UnicodeCategory.LineSeparator,
            "Paragraph_Separator" or "Zp" => cat == UnicodeCategory.ParagraphSeparator,
            "Other" or "C" => cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned,
            "Control" or "Cc" => cat == UnicodeCategory.Control,
            "Format" or "Cf" => cat == UnicodeCategory.Format,
            "Surrogate" or "Cs" => cat == UnicodeCategory.Surrogate,
            "Private_Use" or "Co" => cat == UnicodeCategory.PrivateUse,
            "Unassigned" or "Cn" => cat == UnicodeCategory.OtherNotAssigned,
            "ASCII" => cp <= 0x7F,
            "ASCII_Hex_Digit" or "AHex" => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f',
            "Hex_Digit" or "Hex" => c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f'
                or >= '\uFF10' and <= '\uFF19' or >= '\uFF21' and <= '\uFF26' or >= '\uFF41' and <= '\uFF46',
            "Alphabetic" or "Alpha" => char.IsLetter(c) || cat == UnicodeCategory.LetterNumber,
            "Any" => true,
            "Assigned" => cat != UnicodeCategory.OtherNotAssigned,
            "Cased" => cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter,
            "Lowercase" or "Lower" => char.IsLower(c),
            "Uppercase" or "Upper" => char.IsUpper(c),
            "Math" => cat == UnicodeCategory.MathSymbol || c is '+' or '=' or '<' or '>' or '|' or '~' or '^',
            "Dash" => cat == UnicodeCategory.DashPunctuation || c is '-' or '\u2212',
            "Ideographic" or "Ideo" => c is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF'
                or >= '\uF900' and <= '\uFA6D' or '\u3005' or '\u3007' or >= '\u3021' and <= '\u3029',
            "Default_Ignorable_Code_Point" or "DI" => c is '\u00AD' or '\u034F' or '\u061C'
                or >= '\u115F' and <= '\u1160' or >= '\u17B4' and <= '\u17B5' or >= '\u180B' and <= '\u180E'
                or >= '\u200B' and <= '\u200F' or >= '\u202A' and <= '\u202E' or >= '\u2060' and <= '\u206F'
                or '\u3164' or >= '\uFE00' and <= '\uFE0F' or '\uFEFF' or '\uFFA0' or >= '\uFFF0' and <= '\uFFF8',
            _ => false,
        };
    }

    // ID_Start: practical JS identifier-start set. Unicode also adds
    // Other_ID_Start, but $/_ + IsLetter + Letter_Number (Nl) covers it.
    private static bool IsIdStart(char c, UnicodeCategory cat) =>
        char.IsLetter(c)
        || cat == UnicodeCategory.LetterNumber
        || c == '$'
        || c == '_';

    // ID_Continue: ID_Start plus combining marks, decimal digits,
    // connector punctuation, and the zero-width joiners (U+200C/U+200D).
    private static bool IsIdContinue(char c, UnicodeCategory cat) =>
        IsIdStart(c, cat)
        || cat == UnicodeCategory.NonSpacingMark
        || cat == UnicodeCategory.SpacingCombiningMark
        || cat == UnicodeCategory.DecimalDigitNumber
        || cat == UnicodeCategory.ConnectorPunctuation
        || c == '‌'  // ZERO WIDTH NON-JOINER
        || c == '‍'; // ZERO WIDTH JOINER
}
