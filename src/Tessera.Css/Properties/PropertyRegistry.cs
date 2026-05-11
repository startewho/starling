using Tessera.Css.Parser;
using Tessera.Css.Values;

namespace Tessera.Css.Properties;

public static class PropertyRegistry
{
    private static readonly Dictionary<string, PropertyId> Names =
        Enum.GetValues<PropertyId>().ToDictionary(ToCssName, id => id, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<PropertyId> Inherited =
    [
        PropertyId.Color,
        PropertyId.FontFamily,
        PropertyId.FontSize,
        PropertyId.FontStyle,
        PropertyId.FontWeight,
        PropertyId.LineHeight,
        PropertyId.TextAlign,
        PropertyId.TextDecoration,
        PropertyId.TextTransform,
        PropertyId.WhiteSpace,
        PropertyId.Direction,
        PropertyId.Visibility,
    ];

    public static IReadOnlyList<PropertyId> All { get; } = Enum.GetValues<PropertyId>();

    public static bool TryGetPropertyId(string name, out PropertyId id)
        => Names.TryGetValue(name, out id);

    public static string Name(PropertyId id) => ToCssName(id);

    public static bool Inherits(PropertyId id) => Inherited.Contains(id);

    public static IEnumerable<PropertyDeclaration> Parse(CssDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        if (declaration.Name.StartsWith("--", StringComparison.Ordinal))
            yield break;

        var name = declaration.Name.ToLowerInvariant();
        var values = CssValueParser.ParseList(declaration.Value).ToList();
        foreach (var parsed in Expand(name, values, declaration.Important))
            yield return parsed;
    }

    public static CssValue InitialValue(PropertyId id)
        => id switch
        {
            PropertyId.Display => new CssKeyword("inline"),
            PropertyId.Position => new CssKeyword("static"),
            PropertyId.Top or PropertyId.Right or PropertyId.Bottom or PropertyId.Left => new CssKeyword("auto"),
            PropertyId.ZIndex => new CssKeyword("auto"),
            PropertyId.Float or PropertyId.Clear => new CssKeyword("none"),
            PropertyId.Width or PropertyId.Height => new CssKeyword("auto"),
            PropertyId.MinWidth or PropertyId.MinHeight => CssLength.Zero,
            PropertyId.MaxWidth or PropertyId.MaxHeight => new CssKeyword("none"),
            PropertyId.MarginTop or PropertyId.MarginRight or PropertyId.MarginBottom or PropertyId.MarginLeft => CssLength.Zero,
            PropertyId.PaddingTop or PropertyId.PaddingRight or PropertyId.PaddingBottom or PropertyId.PaddingLeft => CssLength.Zero,
            PropertyId.BoxSizing => new CssKeyword("content-box"),
            PropertyId.OverflowX or PropertyId.OverflowY => new CssKeyword("visible"),
            PropertyId.BorderTopWidth or PropertyId.BorderRightWidth or PropertyId.BorderBottomWidth or PropertyId.BorderLeftWidth => new CssLength(3, CssLengthUnit.Px),
            PropertyId.BorderTopStyle or PropertyId.BorderRightStyle or PropertyId.BorderBottomStyle or PropertyId.BorderLeftStyle => new CssKeyword("none"),
            PropertyId.BorderTopColor or PropertyId.BorderRightColor or PropertyId.BorderBottomColor or PropertyId.BorderLeftColor => new CssKeyword("currentColor"),
            PropertyId.BorderTopLeftRadius or PropertyId.BorderTopRightRadius or PropertyId.BorderBottomRightRadius or PropertyId.BorderBottomLeftRadius => CssLength.Zero,
            PropertyId.Color => CssColor.Black,
            PropertyId.BackgroundColor => CssColor.Transparent,
            PropertyId.Opacity => new CssNumber(1),
            PropertyId.Visibility => new CssKeyword("visible"),
            PropertyId.FontFamily => new CssKeyword("serif"),
            PropertyId.FontSize => new CssLength(16, CssLengthUnit.Px),
            PropertyId.FontStyle => new CssKeyword("normal"),
            PropertyId.FontWeight => new CssNumber(400),
            PropertyId.LineHeight => new CssKeyword("normal"),
            PropertyId.TextAlign => new CssKeyword("start"),
            PropertyId.TextDecoration => new CssKeyword("none"),
            PropertyId.TextTransform => new CssKeyword("none"),
            PropertyId.WhiteSpace => new CssKeyword("normal"),
            PropertyId.Direction => new CssKeyword("ltr"),
            _ => new CssKeyword("initial"),
        };

    private static IEnumerable<PropertyDeclaration> Expand(
        string name,
        List<CssValue> values,
        bool important)
    {
        if (values.Count == 0)
            yield break;

        switch (name)
        {
            case "margin":
                foreach (var item in Box(PropertyId.MarginTop, PropertyId.MarginRight, PropertyId.MarginBottom, PropertyId.MarginLeft, values, important))
                    yield return item;
                break;
            case "padding":
                foreach (var item in Box(PropertyId.PaddingTop, PropertyId.PaddingRight, PropertyId.PaddingBottom, PropertyId.PaddingLeft, values, important))
                    yield return item;
                break;
            case "border-width":
                foreach (var item in Box(PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth, values, important))
                    yield return item;
                break;
            case "border-style":
                foreach (var item in Box(PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle, values, important))
                    yield return item;
                break;
            case "border-color":
                foreach (var item in Box(PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor, values, important))
                    yield return item;
                break;
            case "border-radius":
                foreach (var item in Box(PropertyId.BorderTopLeftRadius, PropertyId.BorderTopRightRadius, PropertyId.BorderBottomRightRadius, PropertyId.BorderBottomLeftRadius, values, important))
                    yield return item;
                break;
            case "overflow":
                yield return new PropertyDeclaration(PropertyId.OverflowX, values[0], important);
                yield return new PropertyDeclaration(PropertyId.OverflowY, values.Count > 1 ? values[1] : values[0], important);
                break;
            case "background":
                if (values.FirstOrDefault(IsColorLike) is { } color)
                    yield return new PropertyDeclaration(PropertyId.BackgroundColor, color, important);
                break;
            case "border":
                foreach (var value in values)
                {
                    if (IsBorderStyle(value))
                    {
                        foreach (var item in Box(PropertyId.BorderTopStyle, PropertyId.BorderRightStyle, PropertyId.BorderBottomStyle, PropertyId.BorderLeftStyle, [value], important))
                            yield return item;
                    }
                    else if (IsColorLike(value))
                    {
                        foreach (var item in Box(PropertyId.BorderTopColor, PropertyId.BorderRightColor, PropertyId.BorderBottomColor, PropertyId.BorderLeftColor, [value], important))
                            yield return item;
                    }
                    else
                    {
                        foreach (var item in Box(PropertyId.BorderTopWidth, PropertyId.BorderRightWidth, PropertyId.BorderBottomWidth, PropertyId.BorderLeftWidth, [value], important))
                            yield return item;
                    }
                }
                break;
            default:
                if (TryGetPropertyId(name, out var id))
                    yield return new PropertyDeclaration(id, values.Count == 1 ? values[0] : new CssValueList(values), important);
                break;
        }
    }

    private static IEnumerable<PropertyDeclaration> Box(
        PropertyId top,
        PropertyId right,
        PropertyId bottom,
        PropertyId left,
        List<CssValue> values,
        bool important)
    {
        CssValue[] actual = values.Count switch
        {
            1 => [values[0], values[0], values[0], values[0]],
            2 => [values[0], values[1], values[0], values[1]],
            3 => [values[0], values[1], values[2], values[1]],
            _ => [values[0], values[1], values[2], values[3]],
        };

        yield return new PropertyDeclaration(top, actual[0], important);
        yield return new PropertyDeclaration(right, actual[1], important);
        yield return new PropertyDeclaration(bottom, actual[2], important);
        yield return new PropertyDeclaration(left, actual[3], important);
    }

    private static bool IsColorLike(CssValue value)
        => value is CssColor or CssKeyword { Name: "currentColor" or "transparent" };

    private static bool IsBorderStyle(CssValue value)
        => value is CssKeyword { Name: "none" or "hidden" or "dotted" or "dashed" or "solid" or "double" or "groove" or "ridge" or "inset" or "outset" };

    private static string ToCssName(PropertyId id)
    {
        var name = id.ToString();
        var chars = new List<char>(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                chars.Add('-');
            chars.Add(char.ToLowerInvariant(name[i]));
        }

        return new string(chars.ToArray());
    }
}

public sealed record PropertyDeclaration(PropertyId Id, CssValue Value, bool Important);
