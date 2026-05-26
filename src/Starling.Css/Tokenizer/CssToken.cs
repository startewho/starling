namespace Starling.Css.Tokenizer;

public readonly record struct CssToken(
    CssTokenType Type,
    string Value = "",
    double Number = 0,
    string Unit = "",
    char Delimiter = '\0',
    bool HasSign = false,
    bool IsInteger = false)
{
    public override string ToString() => Type switch
    {
        CssTokenType.Delim => $"Delim({Delimiter})",
        CssTokenType.Number or CssTokenType.Percentage => $"{Type}({Number})",
        CssTokenType.Dimension => $"Dimension({Number}{Unit})",
        CssTokenType.Eof => "Eof",
        _ when Value.Length > 0 => $"{Type}({Value})",
        _ => Type.ToString(),
    };
}
