using System.Globalization;
using Tessera.Css.Parser;
using Tessera.Css.Tokenizer;

namespace Tessera.Css.Selectors;

public sealed class SelectorParser
{
    private readonly IReadOnlyList<CssComponentValue> _values;
    private int _position;

    public SelectorParser(IReadOnlyList<CssComponentValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
    }

    public static SelectorList ParseSelectorList(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var sheet = CssParser.ParseStyleSheet($"{source} {{ }}");
        var rule = sheet.Rules.OfType<StyleRule>().FirstOrDefault();
        return rule is null ? SelectorList.Empty : ParseSelectorList(rule.Prelude);
    }

    public static SelectorList ParseSelectorList(IReadOnlyList<CssComponentValue> values)
        => new SelectorParser(values).ParseSelectorList();

    public SelectorList ParseSelectorList()
    {
        var selectors = new List<ComplexSelector>();
        while (!IsEnd)
        {
            SkipWhitespace();
            if (ConsumeToken(CssTokenType.Comma))
                continue;
            if (IsEnd)
                break;

            var selector = ParseComplexSelector();
            if (selector.Parts.Count > 0)
                selectors.Add(selector);

            SkipWhitespace();
            if (!ConsumeToken(CssTokenType.Comma) && !IsEnd)
                break;
        }

        return new SelectorList(selectors);
    }

    private ComplexSelector ParseComplexSelector()
    {
        var parts = new List<ComplexSelectorPart>();
        var pendingCombinator = SelectorCombinator.None;

        while (!IsEnd && !IsToken(CssTokenType.Comma))
        {
            var hadWhitespace = SkipWhitespace();
            if (hadWhitespace && parts.Count > 0 && pendingCombinator == SelectorCombinator.None)
                pendingCombinator = SelectorCombinator.Descendant;

            if (TryConsumeCombinator(out var explicitCombinator))
            {
                pendingCombinator = explicitCombinator;
                SkipWhitespace();
                continue;
            }

            var compound = ParseCompoundSelector();
            if (compound.SimpleSelectors.Count == 0)
                break;

            parts.Add(new ComplexSelectorPart(compound, parts.Count == 0
                ? SelectorCombinator.None
                : pendingCombinator));
            pendingCombinator = SelectorCombinator.None;
        }

        return new ComplexSelector(parts);
    }

    private CompoundSelector ParseCompoundSelector()
    {
        var simples = new List<SimpleSelector>();
        if (TryParseTypeSelector(out var typeSelector))
            simples.Add(typeSelector);

        while (!IsEnd)
        {
            if (Current is CssSimpleBlock { StartToken: CssTokenType.LeftSquare } block)
            {
                _position++;
                if (TryParseAttributeSelector(block, out var attribute))
                    simples.Add(attribute);
                continue;
            }

            if (TryConsumeHash(out var id))
            {
                simples.Add(new IdSelector(id));
                continue;
            }

            if (TryConsumeClassSelector(out var className))
            {
                simples.Add(new ClassSelector(className));
                continue;
            }

            if (TryConsumePseudoSelector(out var pseudo))
            {
                simples.Add(pseudo);
                continue;
            }

            break;
        }

        return new CompoundSelector(simples);
    }

    private bool TryParseTypeSelector(out SimpleSelector selector)
    {
        selector = null!;
        if (TryConsumeIdent(out var name))
        {
            selector = new TypeSelector(name.ToLowerInvariant());
            return true;
        }

        if (TryConsumeDelimiter('*'))
        {
            selector = new UniversalSelector();
            return true;
        }

        return false;
    }

    private bool TryConsumeClassSelector(out string className)
    {
        className = string.Empty;
        if (!IsDelimiter('.') || !IsToken(CssTokenType.Ident, offset: 1))
            return false;

        _position++;
        className = TokenAt(0).Value;
        _position++;
        return true;
    }

    private bool TryConsumePseudoSelector(out SimpleSelector selector)
    {
        selector = null!;
        if (!ConsumeToken(CssTokenType.Colon))
            return false;

        var pseudoElement = ConsumeToken(CssTokenType.Colon);
        if (Current is CssFunction function)
        {
            _position++;
            var name = function.Name.ToLowerInvariant();
            selector = pseudoElement
                ? new PseudoElementSelector(name)
                : new PseudoClassSelector(name, ParsePseudoArgument(name, function.Values));
            return true;
        }

        if (TryConsumeIdent(out var ident))
        {
            var name = ident.ToLowerInvariant();
            selector = pseudoElement
                ? new PseudoElementSelector(name)
                : new PseudoClassSelector(name);
            return true;
        }

        return false;
    }

    private static object? ParsePseudoArgument(string name, IReadOnlyList<CssComponentValue> values)
        => name switch
        {
            "is" or "where" or "not" or "has" => ParseSelectorList(values),
            "lang" => FirstValueText(values)?.ToLowerInvariant(),
            "nth-child" or "nth-last-child" or "nth-of-type" or "nth-last-of-type" =>
                ParseNthPattern(ComponentValuesText(values)),
            _ => ComponentValuesText(values),
        };

    private static NthPattern ParseNthPattern(string text)
    {
        var normalized = text.Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        if (normalized == "odd")
            return new NthPattern(2, 1);
        if (normalized == "even")
            return new NthPattern(2, 0);
        if (!normalized.Contains('n', StringComparison.Ordinal))
            return new NthPattern(0, int.Parse(normalized, CultureInfo.InvariantCulture));

        var n = normalized.IndexOf('n', StringComparison.Ordinal);
        var aText = normalized[..n];
        var bText = normalized[(n + 1)..];
        var a = aText switch
        {
            "" or "+" => 1,
            "-" => -1,
            _ => int.Parse(aText, CultureInfo.InvariantCulture),
        };
        var b = string.IsNullOrEmpty(bText) ? 0 : int.Parse(bText, CultureInfo.InvariantCulture);
        return new NthPattern(a, b);
    }

    private bool TryParseAttributeSelector(CssSimpleBlock block, out AttributeSelector selector)
    {
        selector = null!;
        var tokens = block.Values.OfType<CssTokenValue>()
            .Where(value => value.Token.Type != CssTokenType.Whitespace)
            .Select(value => value.Token)
            .ToList();
        if (tokens.Count == 0 || tokens[0].Type != CssTokenType.Ident)
            return false;

        var name = tokens[0].Value.ToLowerInvariant();
        if (tokens.Count == 1)
        {
            selector = new AttributeSelector(name, AttributeOperator.Exists, null, false);
            return true;
        }

        var index = 1;
        var op = AttributeOperator.Equals;
        if (tokens[index].Type == CssTokenType.Delim && tokens[index].Delimiter != '=')
        {
            op = tokens[index].Delimiter switch
            {
                '~' => AttributeOperator.Includes,
                '|' => AttributeOperator.DashMatch,
                '^' => AttributeOperator.Prefix,
                '$' => AttributeOperator.Suffix,
                '*' => AttributeOperator.Substring,
                _ => AttributeOperator.Equals,
            };
            index++;
        }

        if (index >= tokens.Count || tokens[index].Type != CssTokenType.Delim || tokens[index].Delimiter != '=')
            return false;

        index++;
        if (index >= tokens.Count || tokens[index].Type is not (CssTokenType.Ident or CssTokenType.String))
            return false;

        var value = tokens[index].Value;
        index++;
        var caseInsensitive = index < tokens.Count &&
            tokens[index].Type == CssTokenType.Ident &&
            tokens[index].Value.Equals("i", StringComparison.OrdinalIgnoreCase);

        selector = new AttributeSelector(name, op, value, caseInsensitive);
        return true;
    }

    private bool TryConsumeCombinator(out SelectorCombinator combinator)
    {
        combinator = SelectorCombinator.None;
        if (TryConsumeDelimiter('>'))
        {
            combinator = SelectorCombinator.Child;
            return true;
        }
        if (TryConsumeDelimiter('+'))
        {
            combinator = SelectorCombinator.NextSibling;
            return true;
        }
        if (TryConsumeDelimiter('~'))
        {
            combinator = SelectorCombinator.SubsequentSibling;
            return true;
        }

        return false;
    }

    private bool SkipWhitespace()
    {
        var skipped = false;
        while (IsToken(CssTokenType.Whitespace))
        {
            _position++;
            skipped = true;
        }

        return skipped;
    }

    private bool TryConsumeHash(out string value)
    {
        value = string.Empty;
        if (!IsToken(CssTokenType.Hash))
            return false;

        value = TokenAt(0).Value;
        _position++;
        return true;
    }

    private bool TryConsumeIdent(out string value)
    {
        value = string.Empty;
        if (!IsToken(CssTokenType.Ident))
            return false;

        value = TokenAt(0).Value;
        _position++;
        return true;
    }

    private bool ConsumeToken(CssTokenType type)
    {
        if (!IsToken(type))
            return false;

        _position++;
        return true;
    }

    private bool TryConsumeDelimiter(char delimiter)
    {
        if (!IsDelimiter(delimiter))
            return false;

        _position++;
        return true;
    }

    private bool IsDelimiter(char delimiter, int offset = 0)
        => IsToken(CssTokenType.Delim, offset) && TokenAt(offset).Delimiter == delimiter;

    private bool IsToken(CssTokenType type, int offset = 0)
        => CurrentAt(offset) is CssTokenValue { Token.Type: var tokenType } && tokenType == type;

    private CssToken TokenAt(int offset)
        => ((CssTokenValue)CurrentAt(offset)).Token;

    private CssComponentValue Current => CurrentAt(0);

    private CssComponentValue CurrentAt(int offset)
        => _position + offset < _values.Count
            ? _values[_position + offset]
            : new CssTokenValue(new CssToken(CssTokenType.Eof));

    private bool IsEnd => _position >= _values.Count;

    private static string? FirstValueText(IReadOnlyList<CssComponentValue> values)
        => values.OfType<CssTokenValue>()
            .FirstOrDefault(v => v.Token.Type != CssTokenType.Whitespace)
            ?.Token.Value;

    private static string ComponentValuesText(IReadOnlyList<CssComponentValue> values)
        => string.Concat(values.Select(ComponentValueText));

    private static string ComponentValueText(CssComponentValue value)
        => value switch
        {
            CssTokenValue token => TokenText(token.Token),
            CssFunction function => $"{function.Name}({ComponentValuesText(function.Values)})",
            CssSimpleBlock block => $"{BlockStart(block.StartToken)}{ComponentValuesText(block.Values)}{BlockEnd(block.StartToken)}",
            _ => string.Empty,
        };

    private static string TokenText(CssToken token)
        => token.Type switch
        {
            CssTokenType.Ident or CssTokenType.String or CssTokenType.Hash => token.Value,
            CssTokenType.Number => token.Number.ToString(CultureInfo.InvariantCulture),
            CssTokenType.Percentage => token.Number.ToString(CultureInfo.InvariantCulture) + "%",
            CssTokenType.Dimension => token.Number.ToString(CultureInfo.InvariantCulture) + token.Unit,
            CssTokenType.Delim => token.Delimiter.ToString(),
            CssTokenType.Whitespace => " ",
            _ => token.Value,
        };

    private static string BlockStart(CssTokenType type) => type switch
    {
        CssTokenType.LeftParen => "(",
        CssTokenType.LeftSquare => "[",
        CssTokenType.LeftBrace => "{",
        _ => string.Empty,
    };

    private static string BlockEnd(CssTokenType type) => type switch
    {
        CssTokenType.LeftParen => ")",
        CssTokenType.LeftSquare => "]",
        CssTokenType.LeftBrace => "}",
        _ => string.Empty,
    };
}
