using System.Globalization;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;

namespace Tessera.Css.Cascade;

public sealed class ComputedStyle
{
    private readonly IReadOnlyDictionary<PropertyId, CssValue> _values;

    internal ComputedStyle(
        IReadOnlyDictionary<PropertyId, CssValue> values,
        IReadOnlyDictionary<string, IReadOnlyList<CssComponentValue>> customProperties)
    {
        _values = values;
        CustomProperties = customProperties;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<CssComponentValue>> CustomProperties { get; }

    public CssValue Get(PropertyId property) => _values[property];

    public CssLength GetLength(PropertyId property)
        => Get(property) as CssLength ?? CssLength.Zero;

    public CssColor GetColor(PropertyId property)
        => Get(property) as CssColor ?? CssColor.Black;

    public string GetPropertyValue(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return PropertyRegistry.TryGetPropertyId(name, out var property)
            ? ToCssText(Get(property))
            : string.Empty;
    }

    public static string ToCssText(CssValue value)
        => value switch
        {
            CssKeyword keyword => keyword.Name,
            CssNumber number => number.Value.ToString(CultureInfo.InvariantCulture),
            CssPercentage percentage => percentage.Value.ToString(CultureInfo.InvariantCulture) + "%",
            CssLength length => length.ToString(),
            CssDimension dimension => dimension.Value.ToString(CultureInfo.InvariantCulture) + dimension.Unit,
            CssColor color => color.ToString(),
            CssString text => text.Value,
            CssUrl url => $"url({url.Value})",
            CssValueList list => string.Join(" ", list.Values.Select(ToCssText)),
            CssFunctionValue function => $"{function.Name}({string.Join(", ", function.Arguments.Select(ToCssText))})",
            CssVarReference var => var.Fallback is null
                ? $"var({var.Name})"
                : $"var({var.Name}, {ToCssText(var.Fallback)})",
            _ => value.ToString() ?? string.Empty,
        };
}
