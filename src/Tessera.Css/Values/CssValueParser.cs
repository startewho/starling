using Tessera.Css.Parser;
using Tessera.Css.Tokenizer;

namespace Tessera.Css.Values;

public static class CssValueParser
{
    private static readonly Dictionary<string, CssColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new CssColor(0, 0, 0),
        ["white"] = new CssColor(255, 255, 255),
        ["red"] = new CssColor(255, 0, 0),
        ["green"] = new CssColor(0, 128, 0),
        ["blue"] = new CssColor(0, 0, 255),
        ["gray"] = new CssColor(128, 128, 128),
        ["grey"] = new CssColor(128, 128, 128),
        ["silver"] = new CssColor(192, 192, 192),
        ["maroon"] = new CssColor(128, 0, 0),
        ["purple"] = new CssColor(128, 0, 128),
        ["fuchsia"] = new CssColor(255, 0, 255),
        ["lime"] = new CssColor(0, 255, 0),
        ["olive"] = new CssColor(128, 128, 0),
        ["yellow"] = new CssColor(255, 255, 0),
        ["navy"] = new CssColor(0, 0, 128),
        ["teal"] = new CssColor(0, 128, 128),
        ["aqua"] = new CssColor(0, 255, 255),
        ["orange"] = new CssColor(255, 165, 0),
        ["transparent"] = CssColor.Transparent,
    };

    public static CssValue Parse(IReadOnlyList<CssComponentValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var parsed = values
            .Where(value => value is not CssTokenValue { Token.Type: CssTokenType.Whitespace })
            .Select(ParseComponentValue)
            .ToList();
        return parsed.Count == 1 ? parsed[0] : new CssValueList(parsed);
    }

    public static IReadOnlyList<CssValue> ParseList(IReadOnlyList<CssComponentValue> values)
        => Parse(values) is CssValueList list ? list.Values : [Parse(values)];

    private static CssValue ParseComponentValue(CssComponentValue value)
        => value switch
        {
            CssTokenValue token => ParseToken(token.Token),
            CssFunction function => ParseFunction(function),
            CssSimpleBlock block => new CssFunctionValue(
                block.StartToken.ToString(),
                block.Values.Select(ParseComponentValue).ToList()),
            _ => new CssKeyword("initial"),
        };

    private static CssValue ParseToken(CssToken token)
        => token.Type switch
        {
            CssTokenType.Ident => ParseIdent(token.Value),
            CssTokenType.Number => new CssNumber(token.Number),
            CssTokenType.Percentage => new CssPercentage(token.Number),
            CssTokenType.Dimension => ParseDimension(token.Number, token.Unit),
            CssTokenType.Hash when TryParseHexColor(token.Value, out var color) => color,
            CssTokenType.String => new CssString(token.Value),
            CssTokenType.Url => new CssUrl(token.Value),
            CssTokenType.Delim => new CssKeyword(token.Delimiter.ToString()),
            _ => new CssKeyword(token.Value),
        };

    private static CssValue ParseIdent(string value)
        => NamedColors.TryGetValue(value, out var color)
            ? color
            : new CssKeyword(value.ToLowerInvariant());

    private static CssValue ParseDimension(double value, string unit)
        => Enum.TryParse<CssLengthUnit>(unit, ignoreCase: true, out var lengthUnit)
            ? new CssLength(value, lengthUnit)
            : new CssDimension(value, unit);

    private static CssValue ParseFunction(CssFunction function)
    {
        var name = function.Name.ToLowerInvariant();
        if (name == "var")
            return ParseVar(function.Values);

        return new CssFunctionValue(name, SplitArguments(function.Values).Select(Parse).ToList());
    }

    private static CssVarReference ParseVar(IReadOnlyList<CssComponentValue> values)
    {
        var args = SplitArguments(values).ToList();
        var name = args.Count > 0
            ? args[0].OfType<CssTokenValue>().FirstOrDefault(v => v.Token.Type == CssTokenType.Ident)?.Token.Value
            : null;
        var fallback = args.Count > 1 ? Parse(args[1]) : null;
        return new CssVarReference(name ?? string.Empty, fallback);
    }

    private static IEnumerable<IReadOnlyList<CssComponentValue>> SplitArguments(IReadOnlyList<CssComponentValue> values)
    {
        var current = new List<CssComponentValue>();
        foreach (var value in values)
        {
            if (value is CssTokenValue { Token.Type: CssTokenType.Comma })
            {
                yield return current;
                current = [];
                continue;
            }

            current.Add(value);
        }

        yield return current;
    }

    private static bool TryParseHexColor(string text, out CssColor color)
    {
        color = CssColor.Transparent;
        if (text.Length is not (3 or 4 or 6 or 8) || text.Any(c => !Uri.IsHexDigit(c)))
            return false;

        var expanded = text.Length switch
        {
            3 => string.Concat(text.Select(c => $"{c}{c}")) + "ff",
            4 => string.Concat(text.Select(c => $"{c}{c}")),
            6 => text + "ff",
            _ => text,
        };

        color = new CssColor(
            Convert.ToByte(expanded[..2], 16),
            Convert.ToByte(expanded[2..4], 16),
            Convert.ToByte(expanded[4..6], 16),
            Convert.ToByte(expanded[6..8], 16));
        return true;
    }
}
