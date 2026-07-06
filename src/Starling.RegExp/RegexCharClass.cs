// SPDX-License-Identifier: Apache-2.0

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
        // _ranges is sorted and coalesced; binary search keeps large
        // property tables (hundreds of ranges) cheap on the match path.
        var lo = 0;
        var hi = _ranges.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var r = _ranges[mid];
            if (codePoint < r.Lo)
            {
                hi = mid - 1;
            }
            else if (codePoint > r.Hi)
            {
                lo = mid + 1;
            }
            else
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

    // ---------------- Unicode property tables ----------------

    /// <summary>Cache of decoded property range lists, keyed by the exact
    /// escape spelling ("Script=Latin", "gc=Lu", "Alphabetic", ...). The
    /// lists are shared and must not be mutated by callers.</summary>
    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<(int, int)>> _propertyCache
        = new(System.StringComparer.Ordinal);

    /// <summary>Resolves a Unicode property spelling to its code point
    /// ranges. Property names and values are case-sensitive. Returns false
    /// for names the tables do not know, which the parser reports as a
    /// SyntaxError.</summary>
    public static bool TryGetPropertyRanges(string name, out System.Collections.Generic.List<(int, int)> ranges)
    {
        lock (_propertyCache)
        {
            if (_propertyCache.TryGetValue(name, out ranges!))
            {
                return true;
            }

            if (!UnicodePropertyData.TryGetRanges(name, out var pairs, out var start, out var count))
            {
                ranges = null!;
                return false;
            }

            var list = new System.Collections.Generic.List<(int, int)>(count);
            for (var i = 0; i < count; i++)
            {
                list.Add((pairs[(start + i) * 2], pairs[(start + i) * 2 + 1]));
            }

            _propertyCache[name] = list;
            ranges = list;
            return true;
        }
    }

    /// <summary>Range lookup for names known to exist (sequence-property
    /// sub-patterns). Unknown names return an empty list.</summary>
    public static System.Collections.Generic.List<(int, int)> GetPropertyRanges(string name)
    {
        if (TryGetPropertyRanges(name, out var ranges))
        {
            return ranges;
        }

        return new System.Collections.Generic.List<(int, int)>();
    }
}
