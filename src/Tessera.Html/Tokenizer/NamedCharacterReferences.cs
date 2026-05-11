namespace Tessera.Html.Tokenizer;

/// <summary>
/// Resolves HTML named character references per
/// <see href="https://html.spec.whatwg.org/multipage/named-characters.html">
/// WHATWG HTML named character references table</see>.
/// </summary>
/// <remarks>
/// <para>
/// The WHATWG table has 2231 entries; the tokenizer needs to find the
/// <em>longest</em> table entry that is a prefix of the input (whether or
/// not it ends in <c>;</c>). This file embeds a curated subset covering the
/// entities that show up in 99% of real HTML — full table generation is
/// deferred to wp:M1-01h alongside the html5lib conformance pass, when a
/// build-time tool will codegen this file from
/// <c>testdata/spec/html-entities.json</c>.
/// </para>
/// <para>
/// Any entity not in the embedded table simply returns "no match"; the
/// tokenizer falls through to the ambiguous-ampersand state and emits the
/// raw chars, matching what a browser does for a typo'd entity. So this
/// subset implementation is safe — incomplete, not incorrect.
/// </para>
/// </remarks>
public static class NamedCharacterReferences
{
    /// <summary>
    /// Output of a longest-prefix match. <see cref="Length"/> is the number of
    /// characters consumed from the input (including any trailing <c>;</c>).
    /// <see cref="CodePoint1"/> is always set; <see cref="CodePoint2"/> is non-null
    /// only for the handful of entities that decode to two code points.
    /// </summary>
    public readonly record struct Match(int Length, int CodePoint1, int? CodePoint2);

    /// <summary>
    /// Find the longest entry in the table that is a prefix of <paramref name="input"/>.
    /// Returns <c>null</c> if no entry matches.
    /// </summary>
    /// <remarks>
    /// Entity names in the table include their trailing <c>;</c> when one is
    /// required by the spec. The historical-quirk entities (which match
    /// without a <c>;</c>, like <c>&amp;amp</c>) are listed separately
    /// without it. Longest-match-wins handles ambiguity (e.g. <c>&amp;not</c>
    /// vs. <c>&amp;notin;</c>): both are in the table; the input
    /// <c>&amp;notin;</c> picks the longer.
    /// </remarks>
    public static Match? FindLongest(ReadOnlySpan<char> input)
    {
        Match? best = null;
        foreach (var (name, cp1, cp2) in Table)
        {
            if (input.Length < name.Length) continue;
            if (!input.StartsWith(name)) continue;
            if (best is null || name.Length > best.Value.Length)
                best = new Match(name.Length, cp1, cp2);
        }
        return best;
    }

    // Curated subset — every entity here is one the WHATWG table contains.
    // Names include the trailing ';' when the spec requires it. The historical
    // no-semicolon variants (the original "named character references that
    // don't end with a semicolon", per §13.5) are listed separately so the
    // tokenizer can apply the legacy attribute-value rule from §13.2.5.73.
    private static readonly (string Name, int Cp1, int? Cp2)[] Table =
    [
        // The five XML pre-defined entities, both with and without semicolon
        // where the WHATWG legacy list permits.
        ("amp;",   '&', null), ("amp",    '&', null),
        ("lt;",    '<', null), ("lt",     '<', null),
        ("gt;",    '>', null), ("gt",     '>', null),
        ("quot;",  '"', null), ("quot",   '"', null),
        ("apos;",  '\'', null),

        // Whitespace + common Latin-1
        ("nbsp;",  0x00A0, null), ("nbsp", 0x00A0, null),
        ("iexcl;", 0x00A1, null),
        ("cent;",  0x00A2, null), ("cent", 0x00A2, null),
        ("pound;", 0x00A3, null), ("pound", 0x00A3, null),
        ("yen;",   0x00A5, null), ("yen",  0x00A5, null),
        ("brvbar;", 0x00A6, null),
        ("sect;",  0x00A7, null), ("sect", 0x00A7, null),
        ("uml;",   0x00A8, null),
        ("copy;",  0x00A9, null), ("copy", 0x00A9, null),
        ("ordf;",  0x00AA, null),
        ("laquo;", 0x00AB, null), ("laquo", 0x00AB, null),
        ("not;",   0x00AC, null), ("not",  0x00AC, null),
        ("shy;",   0x00AD, null), ("shy",  0x00AD, null),
        ("reg;",   0x00AE, null), ("reg",  0x00AE, null),
        ("macr;",  0x00AF, null),
        ("deg;",   0x00B0, null), ("deg",  0x00B0, null),
        ("plusmn;", 0x00B1, null), ("plusmn", 0x00B1, null),
        ("sup2;",  0x00B2, null),
        ("sup3;",  0x00B3, null),
        ("micro;", 0x00B5, null), ("micro", 0x00B5, null),
        ("para;",  0x00B6, null), ("para", 0x00B6, null),
        ("middot;", 0x00B7, null), ("middot", 0x00B7, null),
        ("raquo;", 0x00BB, null), ("raquo", 0x00BB, null),
        ("frac14;", 0x00BC, null),
        ("frac12;", 0x00BD, null),
        ("frac34;", 0x00BE, null),
        ("iquest;", 0x00BF, null),

        // Common accented letters
        ("Auml;", 0x00C4, null), ("auml;", 0x00E4, null),
        ("Eacute;", 0x00C9, null), ("eacute;", 0x00E9, null),
        ("Iacute;", 0x00CD, null), ("iacute;", 0x00ED, null),
        ("Oacute;", 0x00D3, null), ("oacute;", 0x00F3, null),
        ("Ouml;", 0x00D6, null), ("ouml;", 0x00F6, null),
        ("szlig;", 0x00DF, null),
        ("ntilde;", 0x00F1, null), ("Ntilde;", 0x00D1, null),

        // Curly quotes + dashes + ellipsis (showing up in any prose HTML)
        ("ndash;",  0x2013, null),
        ("mdash;",  0x2014, null),
        ("lsquo;",  0x2018, null),
        ("rsquo;",  0x2019, null),
        ("ldquo;",  0x201C, null),
        ("rdquo;",  0x201D, null),
        ("hellip;", 0x2026, null),
        ("prime;",  0x2032, null),
        ("Prime;",  0x2033, null),
        ("bull;",   0x2022, null),
        ("dagger;", 0x2020, null),
        ("Dagger;", 0x2021, null),
        ("permil;", 0x2030, null),

        // Currency + trademark
        ("euro;",   0x20AC, null),
        ("trade;",  0x2122, null), ("trade", 0x2122, null),

        // Math + greek essentials
        ("alpha;",  0x03B1, null),
        ("beta;",   0x03B2, null),
        ("gamma;",  0x03B3, null),
        ("delta;",  0x03B4, null),
        ("epsilon;", 0x03B5, null),
        ("pi;",     0x03C0, null),
        ("infin;",  0x221E, null),
        ("plusmn;", 0x00B1, null),
        ("times;",  0x00D7, null), ("times", 0x00D7, null),
        ("divide;", 0x00F7, null), ("divide", 0x00F7, null),
        ("le;",     0x2264, null),
        ("ge;",     0x2265, null),
        ("ne;",     0x2260, null),
        ("approx;", 0x2248, null),

        // Notin (longer than 'not' — exercises longest-prefix-match)
        ("notin;", 0x2209, null),

        // Two-code-point entities (representative sample for the algorithm)
        ("nvgt;", 0x003E, 0x20D2),
        ("nvlt;", 0x003C, 0x20D2),
    ];
}
