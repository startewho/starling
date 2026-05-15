using Tessera.Css.Parser;
using Tessera.Css.Tokenizer;

namespace Tessera.Css.FontFace;

/// <summary>
/// Extracts <see cref="FontFaceRule"/> values from parsed stylesheets. The
/// CSS parser already recognises <c>@font-face</c> as an at-rule with a
/// declaration list (see <see cref="Parser.CssParser"/>); this turns those
/// declarations into a strongly-typed font-face description that the engine
/// can hand to the font-face fetcher.
/// </summary>
/// <remarks>
/// Fail-soft: a rule missing the required <c>font-family</c> or <c>src</c>
/// descriptors is skipped silently (per CSS Fonts 3 §4.1, such a rule must
/// not be applied). Unsupported descriptors (<c>unicode-range</c>,
/// <c>font-variation-settings</c>, <c>font-feature-settings</c>, etc.) are
/// ignored — variable-font axes and Unicode-range coverage are later work.
/// </remarks>
public static class FontFaceParser
{
    public static IEnumerable<FontFaceRule> ParseAll(StyleSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        foreach (var rule in sheet.Rules)
        {
            if (rule is AtRule { Name: "font-face" } atRule)
            {
                if (TryParse(atRule, out var fontFace))
                    yield return fontFace!;
            }
        }
    }

    public static bool TryParse(AtRule rule, out FontFaceRule? fontFace)
    {
        ArgumentNullException.ThrowIfNull(rule);
        fontFace = null;
        if (!string.Equals(rule.Name, "font-face", StringComparison.OrdinalIgnoreCase))
            return false;

        string? family = null;
        var sources = new List<FontFaceSource>();
        var bold = false;
        var italic = false;

        foreach (var decl in rule.Declarations)
        {
            switch (decl.Name.ToLowerInvariant())
            {
                case "font-family":
                    family = ParseFamilyName(decl.Value);
                    break;
                case "src":
                    sources.AddRange(ParseSrc(decl.Value));
                    break;
                case "font-weight":
                    bold = ParseBold(decl.Value);
                    break;
                case "font-style":
                    italic = ParseItalic(decl.Value);
                    break;
            }
        }

        if (string.IsNullOrEmpty(family) || sources.Count == 0)
            return false;

        fontFace = new FontFaceRule(family, sources, bold, italic);
        return true;
    }

    private static string? ParseFamilyName(IReadOnlyList<CssComponentValue> value)
    {
        // The family name is either a quoted string ("Open Sans") or a
        // sequence of idents (Open Sans) — CSS Fonts 3 §4.2 allows both.
        string? quoted = null;
        var idents = new List<string>();
        foreach (var v in value)
        {
            if (v is CssTokenValue token)
            {
                switch (token.Token.Type)
                {
                    case CssTokenType.String:
                        quoted = token.Token.Value;
                        break;
                    case CssTokenType.Ident:
                        idents.Add(token.Token.Value);
                        break;
                }
            }
        }
        if (!string.IsNullOrEmpty(quoted)) return quoted;
        return idents.Count == 0 ? null : string.Join(' ', idents);
    }

    private static IEnumerable<FontFaceSource> ParseSrc(IReadOnlyList<CssComponentValue> value)
    {
        // src is a comma-separated list of either local(name) or url(...) with
        // an optional format() hint. Split on top-level commas and parse each
        // entry.
        foreach (var entry in SplitOnComma(value))
        {
            var source = ParseSrcEntry(entry);
            if (source is not null) yield return source;
        }
    }

    private static FontFaceSource? ParseSrcEntry(IReadOnlyList<CssComponentValue> entry)
    {
        string? formatHint = null;
        FontFaceSource? primary = null;

        foreach (var v in entry)
        {
            switch (v)
            {
                case CssFunction { Name: var name } fn when string.Equals(name, "local", StringComparison.OrdinalIgnoreCase):
                    primary ??= new LocalFontSource(ExtractStringOrIdent(fn.Values) ?? string.Empty);
                    break;
                case CssFunction { Name: var name } fn when string.Equals(name, "url", StringComparison.OrdinalIgnoreCase):
                    var url = ExtractStringOrIdent(fn.Values);
                    if (!string.IsNullOrEmpty(url)) primary ??= new UrlFontSource(url, null);
                    break;
                case CssFunction { Name: var name } fn when string.Equals(name, "format", StringComparison.OrdinalIgnoreCase):
                    formatHint = ExtractStringOrIdent(fn.Values);
                    break;
                case CssTokenValue { Token: { Type: CssTokenType.Url } urlToken }:
                    primary ??= new UrlFontSource(urlToken.Value, null);
                    break;
            }
        }

        return primary switch
        {
            UrlFontSource u => new UrlFontSource(u.Url, formatHint),
            LocalFontSource l => l,
            _ => null,
        };
    }

    private static string? ExtractStringOrIdent(IReadOnlyList<CssComponentValue> values)
    {
        foreach (var v in values)
        {
            if (v is CssTokenValue token)
            {
                if (token.Token.Type == CssTokenType.String) return token.Token.Value;
                if (token.Token.Type == CssTokenType.Url) return token.Token.Value;
                if (token.Token.Type == CssTokenType.Ident) return token.Token.Value;
            }
        }
        return null;
    }

    private static IEnumerable<IReadOnlyList<CssComponentValue>> SplitOnComma(IReadOnlyList<CssComponentValue> values)
    {
        var current = new List<CssComponentValue>();
        foreach (var v in values)
        {
            if (v is CssTokenValue { Token.Type: CssTokenType.Comma })
            {
                if (current.Count > 0) { yield return current; current = []; }
                continue;
            }
            // Skip leading/trailing whitespace so the per-entry list is clean.
            if (v is CssTokenValue { Token.Type: CssTokenType.Whitespace } && current.Count == 0)
                continue;
            current.Add(v);
        }
        if (current.Count > 0) yield return current;
    }

    private static bool ParseBold(IReadOnlyList<CssComponentValue> value)
    {
        // font-weight inside @font-face: bold | <number> | "<number> <number>".
        // Anything ≥ 600 (CSS bold threshold) marks the face as bold. A range
        // ("100 700") flips bold on if the *upper* bound clears the threshold,
        // matching what variable-font browsers do when the requested weight
        // falls inside the range.
        var top = 0d;
        foreach (var v in value)
        {
            if (v is CssTokenValue token)
            {
                if (token.Token.Type == CssTokenType.Ident &&
                    string.Equals(token.Token.Value, "bold", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (token.Token.Type == CssTokenType.Number && token.Token.Number > top)
                    top = token.Token.Number;
            }
        }
        return top >= 600;
    }

    private static bool ParseItalic(IReadOnlyList<CssComponentValue> value)
    {
        foreach (var v in value)
        {
            if (v is CssTokenValue token &&
                token.Token.Type == CssTokenType.Ident &&
                (string.Equals(token.Token.Value, "italic", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(token.Token.Value, "oblique", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
