using System.Globalization;
using System.Text;

namespace Starling.Js.Intrinsics;

/// <summary>Unicode Default Case Conversion for String.prototype
/// toUpperCase/toLowerCase: SpecialCasing.txt full (1:many) mappings plus the
/// language-independent Final_Sigma condition, layered over .NET's simple
/// invariant mappings.</summary>
internal static class StringCasing
{
    // SpecialCasing.txt unconditional uppercase full mappings.
    private static readonly Dictionary<char, string> UpperSpecial = new()
    {
        ['\u00DF'] = "\u0053\u0053",
        ['\u0130'] = "\u0130",
        ['\uFB00'] = "\u0046\u0046",
        ['\uFB01'] = "\u0046\u0049",
        ['\uFB02'] = "\u0046\u004C",
        ['\uFB03'] = "\u0046\u0046\u0049",
        ['\uFB04'] = "\u0046\u0046\u004C",
        ['\uFB05'] = "\u0053\u0054",
        ['\uFB06'] = "\u0053\u0054",
        ['\u0587'] = "\u0535\u0552",
        ['\uFB13'] = "\u0544\u0546",
        ['\uFB14'] = "\u0544\u0535",
        ['\uFB15'] = "\u0544\u053B",
        ['\uFB16'] = "\u054E\u0546",
        ['\uFB17'] = "\u0544\u053D",
        ['\u0149'] = "\u02BC\u004E",
        ['\u0390'] = "\u0399\u0308\u0301",
        ['\u03B0'] = "\u03A5\u0308\u0301",
        ['\u01F0'] = "\u004A\u030C",
        ['\u1E96'] = "\u0048\u0331",
        ['\u1E97'] = "\u0054\u0308",
        ['\u1E98'] = "\u0057\u030A",
        ['\u1E99'] = "\u0059\u030A",
        ['\u1E9A'] = "\u0041\u02BE",
        ['\u1F50'] = "\u03A5\u0313",
        ['\u1F52'] = "\u03A5\u0313\u0300",
        ['\u1F54'] = "\u03A5\u0313\u0301",
        ['\u1F56'] = "\u03A5\u0313\u0342",
        ['\u1FB6'] = "\u0391\u0342",
        ['\u1FC6'] = "\u0397\u0342",
        ['\u1FD2'] = "\u0399\u0308\u0300",
        ['\u1FD3'] = "\u0399\u0308\u0301",
        ['\u1FD6'] = "\u0399\u0342",
        ['\u1FD7'] = "\u0399\u0308\u0342",
        ['\u1FE2'] = "\u03A5\u0308\u0300",
        ['\u1FE3'] = "\u03A5\u0308\u0301",
        ['\u1FE4'] = "\u03A1\u0313",
        ['\u1FE6'] = "\u03A5\u0342",
        ['\u1FE7'] = "\u03A5\u0308\u0342",
        ['\u1FF6'] = "\u03A9\u0342",
        ['\u1F80'] = "\u1F08\u0399",
        ['\u1F81'] = "\u1F09\u0399",
        ['\u1F82'] = "\u1F0A\u0399",
        ['\u1F83'] = "\u1F0B\u0399",
        ['\u1F84'] = "\u1F0C\u0399",
        ['\u1F85'] = "\u1F0D\u0399",
        ['\u1F86'] = "\u1F0E\u0399",
        ['\u1F87'] = "\u1F0F\u0399",
        ['\u1F88'] = "\u1F08\u0399",
        ['\u1F89'] = "\u1F09\u0399",
        ['\u1F8A'] = "\u1F0A\u0399",
        ['\u1F8B'] = "\u1F0B\u0399",
        ['\u1F8C'] = "\u1F0C\u0399",
        ['\u1F8D'] = "\u1F0D\u0399",
        ['\u1F8E'] = "\u1F0E\u0399",
        ['\u1F8F'] = "\u1F0F\u0399",
        ['\u1F90'] = "\u1F28\u0399",
        ['\u1F91'] = "\u1F29\u0399",
        ['\u1F92'] = "\u1F2A\u0399",
        ['\u1F93'] = "\u1F2B\u0399",
        ['\u1F94'] = "\u1F2C\u0399",
        ['\u1F95'] = "\u1F2D\u0399",
        ['\u1F96'] = "\u1F2E\u0399",
        ['\u1F97'] = "\u1F2F\u0399",
        ['\u1F98'] = "\u1F28\u0399",
        ['\u1F99'] = "\u1F29\u0399",
        ['\u1F9A'] = "\u1F2A\u0399",
        ['\u1F9B'] = "\u1F2B\u0399",
        ['\u1F9C'] = "\u1F2C\u0399",
        ['\u1F9D'] = "\u1F2D\u0399",
        ['\u1F9E'] = "\u1F2E\u0399",
        ['\u1F9F'] = "\u1F2F\u0399",
        ['\u1FA0'] = "\u1F68\u0399",
        ['\u1FA1'] = "\u1F69\u0399",
        ['\u1FA2'] = "\u1F6A\u0399",
        ['\u1FA3'] = "\u1F6B\u0399",
        ['\u1FA4'] = "\u1F6C\u0399",
        ['\u1FA5'] = "\u1F6D\u0399",
        ['\u1FA6'] = "\u1F6E\u0399",
        ['\u1FA7'] = "\u1F6F\u0399",
        ['\u1FA8'] = "\u1F68\u0399",
        ['\u1FA9'] = "\u1F69\u0399",
        ['\u1FAA'] = "\u1F6A\u0399",
        ['\u1FAB'] = "\u1F6B\u0399",
        ['\u1FAC'] = "\u1F6C\u0399",
        ['\u1FAD'] = "\u1F6D\u0399",
        ['\u1FAE'] = "\u1F6E\u0399",
        ['\u1FAF'] = "\u1F6F\u0399",
        ['\u1FB3'] = "\u0391\u0399",
        ['\u1FBC'] = "\u0391\u0399",
        ['\u1FC3'] = "\u0397\u0399",
        ['\u1FCC'] = "\u0397\u0399",
        ['\u1FF3'] = "\u03A9\u0399",
        ['\u1FFC'] = "\u03A9\u0399",
        ['\u1FB2'] = "\u1FBA\u0399",
        ['\u1FB4'] = "\u0386\u0399",
        ['\u1FC2'] = "\u1FCA\u0399",
        ['\u1FC4'] = "\u0389\u0399",
        ['\u1FF2'] = "\u1FFA\u0399",
        ['\u1FF4'] = "\u038F\u0399",
        ['\u1FB7'] = "\u0391\u0342\u0399",
        ['\u1FC7'] = "\u0397\u0342\u0399",
        ['\u1FF7'] = "\u03A9\u0342\u0399",
    };

    // SpecialCasing.txt lowercase full mappings (all unconditional except
    // Final_Sigma, which is handled in code).
    private static readonly Dictionary<char, string> LowerSpecial = new()
    {
        ['\u0130'] = "\u0069\u0307",
        ['\u1F88'] = "\u1F80",
        ['\u1F89'] = "\u1F81",
        ['\u1F8A'] = "\u1F82",
        ['\u1F8B'] = "\u1F83",
        ['\u1F8C'] = "\u1F84",
        ['\u1F8D'] = "\u1F85",
        ['\u1F8E'] = "\u1F86",
        ['\u1F8F'] = "\u1F87",
        ['\u1F98'] = "\u1F90",
        ['\u1F99'] = "\u1F91",
        ['\u1F9A'] = "\u1F92",
        ['\u1F9B'] = "\u1F93",
        ['\u1F9C'] = "\u1F94",
        ['\u1F9D'] = "\u1F95",
        ['\u1F9E'] = "\u1F96",
        ['\u1F9F'] = "\u1F97",
        ['\u1FA8'] = "\u1FA0",
        ['\u1FA9'] = "\u1FA1",
        ['\u1FAA'] = "\u1FA2",
        ['\u1FAB'] = "\u1FA3",
        ['\u1FAC'] = "\u1FA4",
        ['\u1FAD'] = "\u1FA5",
        ['\u1FAE'] = "\u1FA6",
        ['\u1FAF'] = "\u1FA7",
        ['\u1FBC'] = "\u1FB3",
        ['\u1FCC'] = "\u1FC3",
        ['\u1FFC'] = "\u1FF3",
    };

    internal static string ToUpperJs(string s)
    {
        var special = -1;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] >= '\u00DF' && UpperSpecial.ContainsKey(s[i]))
            {
                special = i;
                break;
            }
        }

        if (special < 0)
        {
            return s.ToUpperInvariant();
        }

        var sb = new StringBuilder(s.Length + 8);
        var runStart = 0;
        for (var i = special; i < s.Length; i++)
        {
            if (s[i] >= '\u00DF' && UpperSpecial.TryGetValue(s[i], out var mapped))
            {
                if (i > runStart)
                {
                    sb.Append(s[runStart..i].ToUpperInvariant());
                }

                sb.Append(mapped);
                runStart = i + 1;
            }
        }
        if (runStart < s.Length)
        {
            sb.Append(s[runStart..].ToUpperInvariant());
        }

        return sb.ToString();
    }

    internal static string ToLowerJs(string s)
    {
        var special = -1;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\u03A3' || (c >= '\u0130' && LowerSpecial.ContainsKey(c)))
            {
                special = i;
                break;
            }
        }

        if (special < 0)
        {
            return s.ToLowerInvariant();
        }

        var sb = new StringBuilder(s.Length + 4);
        var runStart = 0;
        for (var i = special; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\u03A3')
            {
                if (i > runStart)
                {
                    sb.Append(s[runStart..i].ToLowerInvariant());
                }

                sb.Append(IsFinalSigmaContext(s, i) ? '\u03C2' : '\u03C3');
                runStart = i + 1;
            }
            else if (c >= '\u0130' && LowerSpecial.TryGetValue(c, out var mapped))
            {
                if (i > runStart)
                {
                    sb.Append(s[runStart..i].ToLowerInvariant());
                }

                sb.Append(mapped);
                runStart = i + 1;
            }
        }
        if (runStart < s.Length)
        {
            sb.Append(s[runStart..].ToLowerInvariant());
        }

        return sb.ToString();
    }

    /// <summary>Unicode 3.13 Final_Sigma: preceded by a Cased character after
    /// skipping Case_Ignorable ones, and not followed by a Cased character
    /// after skipping Case_Ignorable ones.</summary>
    private static bool IsFinalSigmaContext(string s, int index)
    {
        var precededByCased = false;
        var i = index - 1;
        while (i >= 0)
        {
            var cp = (int)s[i];
            var size = 1;
            if (char.IsLowSurrogate(s[i]) && i > 0 && char.IsHighSurrogate(s[i - 1]))
            {
                cp = char.ConvertToUtf32(s[i - 1], s[i]);
                size = 2;
            }

            if (IsCaseIgnorable(cp))
            {
                i -= size;
                continue;
            }

            precededByCased = IsCased(cp);
            break;
        }
        if (!precededByCased)
        {
            return false;
        }

        var j = index + 1;
        while (j < s.Length)
        {
            var cp = (int)s[j];
            var size = 1;
            if (char.IsHighSurrogate(s[j]) && j + 1 < s.Length && char.IsLowSurrogate(s[j + 1]))
            {
                cp = char.ConvertToUtf32(s[j], s[j + 1]);
                size = 2;
            }

            if (IsCaseIgnorable(cp))
            {
                j += size;
                continue;
            }

            return !IsCased(cp);
        }
        return true;
    }

    private static bool IsCased(int cp) => CharUnicodeInfo.GetUnicodeCategory(cp) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter => true,
        _ => false,
    };

    private static bool IsCaseIgnorable(int cp)
    {
        switch (cp)
        {
            // Word_Break = MidLetter / MidNumLet / Single_Quote.
            case 0x0027 or 0x002E or 0x003A or 0x00B7 or 0x05F4 or 0x2018 or 0x2019 or 0x2024 or 0x2027
                or 0xFE13 or 0xFE52 or 0xFE55 or 0xFF07 or 0xFF0E or 0xFF1A:
                return true;
        }
        return CharUnicodeInfo.GetUnicodeCategory(cp) switch
        {
            UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
                or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol => true,
            _ => false,
        };
    }
}
