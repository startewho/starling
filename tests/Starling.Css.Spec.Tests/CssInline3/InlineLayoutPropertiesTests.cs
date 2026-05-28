using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssInline3;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-inline-3/">CSS Inline Layout Module Level 3</see>:
/// the <c>vertical-align</c> / <c>baseline-source</c> alignment properties.
/// Parse + cascade level only — baseline shifting layout is not yet implemented.
/// </summary>
[TestClass]
[Spec("css-inline-3", "https://www.w3.org/TR/css-inline-3/")]
public sealed class InlineLayoutPropertiesTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("css-inline-3", "https://www.w3.org/TR/css-inline-3/#propdef-vertical-align", section: "3")]
    [SpecFact]
    public void Vertical_align_parses_baseline_keywords()
    {
        ValueOf("vertical-align: baseline", PropertyId.VerticalAlign).Should().Be(new CssKeyword("baseline"));
        ValueOf("vertical-align: middle", PropertyId.VerticalAlign).Should().Be(new CssKeyword("middle"));
        ValueOf("vertical-align: top", PropertyId.VerticalAlign).Should().Be(new CssKeyword("top"));
        ValueOf("vertical-align: text-bottom", PropertyId.VerticalAlign).Should().Be(new CssKeyword("text-bottom"));
        ValueOf("vertical-align: super", PropertyId.VerticalAlign).Should().Be(new CssKeyword("super"));
    }

    [Spec("css-inline-3", "https://www.w3.org/TR/css-inline-3/#propdef-vertical-align", section: "3")]
    [SpecFact]
    public void Vertical_align_parses_length_and_percentage()
    {
        ValueOf("vertical-align: 4px", PropertyId.VerticalAlign).Should().Be(new CssLength(4, CssLengthUnit.Px));
        ValueOf("vertical-align: 20%", PropertyId.VerticalAlign).Should().Be(new CssPercentage(20));
    }

    [Spec("css-inline-3", "https://www.w3.org/TR/css-inline-3/#propdef-vertical-align", section: "3")]
    [SpecFact]
    public void Vertical_align_initial_is_baseline_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.VerticalAlign).Should().Be(new CssKeyword("baseline"));
        PropertyRegistry.Inherits(PropertyId.VerticalAlign).Should().BeFalse();
    }

    [Spec("css-inline-3", "https://www.w3.org/TR/css-inline-3/#baseline-source", section: "4")]
    [SpecFact]
    public void Baseline_source_parses_auto_first_last()
    {
        ValueOf("baseline-source: auto", PropertyId.BaselineSource).Should().Be(new CssKeyword("auto"));
        ValueOf("baseline-source: first", PropertyId.BaselineSource).Should().Be(new CssKeyword("first"));
        ValueOf("baseline-source: last", PropertyId.BaselineSource).Should().Be(new CssKeyword("last"));
    }
}
