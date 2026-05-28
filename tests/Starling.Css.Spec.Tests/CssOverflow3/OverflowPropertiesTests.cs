using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssOverflow3;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-overflow-3/">CSS Overflow Module Level 3</see>:
/// the <c>overflow</c> shorthand, <c>overflow-x/y</c>, <c>overflow-clip-margin</c>,
/// and <c>scrollbar-gutter</c>.
/// </summary>
[TestClass]
[Spec("css-overflow-3", "https://www.w3.org/TR/css-overflow-3/")]
public sealed class OverflowPropertiesTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("css-overflow-3", "https://www.w3.org/TR/css-overflow-3/#propdef-overflow", section: "3.1")]
    [SpecFact]
    public void Overflow_shorthand_one_value_sets_both_axes()
    {
        var decls = Expand("overflow: hidden");
        decls.Single(d => d.Id == PropertyId.OverflowX).Value.Should().Be(new CssKeyword("hidden"));
        decls.Single(d => d.Id == PropertyId.OverflowY).Value.Should().Be(new CssKeyword("hidden"));
    }

    [Spec("css-overflow-3", "https://www.w3.org/TR/css-overflow-3/#propdef-overflow", section: "3.1")]
    [SpecFact]
    public void Overflow_shorthand_two_values_set_x_then_y()
    {
        var decls = Expand("overflow: scroll hidden");
        decls.Single(d => d.Id == PropertyId.OverflowX).Value.Should().Be(new CssKeyword("scroll"));
        decls.Single(d => d.Id == PropertyId.OverflowY).Value.Should().Be(new CssKeyword("hidden"));
    }

    [Spec("css-overflow-3", "https://www.w3.org/TR/css-overflow-3/#overflow-properties", section: "3.1")]
    [SpecFact]
    public void Overflow_x_accepts_visible_hidden_clip_scroll_auto()
    {
        ValueOf("overflow-x: visible", PropertyId.OverflowX).Should().Be(new CssKeyword("visible"));
        ValueOf("overflow-x: clip", PropertyId.OverflowX).Should().Be(new CssKeyword("clip"));
        ValueOf("overflow-x: auto", PropertyId.OverflowX).Should().Be(new CssKeyword("auto"));
    }

    [Spec("css-overflow-3", "https://www.w3.org/TR/css-overflow-3/#overflow-clip-margin", section: "3.2")]
    [SpecFact]
    public void Overflow_clip_margin_parses_length()
        => ValueOf("overflow-clip-margin: 5px", PropertyId.OverflowClipMargin).Should().Be(new CssLength(5, CssLengthUnit.Px));

    [Spec("css-overflow-3", "https://www.w3.org/TR/css-overflow-3/#scrollbar-gutter-property", section: "3.3")]
    [SpecFact]
    public void Scrollbar_gutter_parses_auto_and_stable()
    {
        ValueOf("scrollbar-gutter: auto", PropertyId.ScrollbarGutter).Should().Be(new CssKeyword("auto"));
        ValueOf("scrollbar-gutter: stable", PropertyId.ScrollbarGutter).Should().Be(new CssKeyword("stable"));
    }

    [Spec("css-overflow-3", "https://www.w3.org/TR/css-overflow-3/#scrollbar-gutter-property", section: "3.3")]
    [SpecFact]
    public void Scrollbar_gutter_initial_is_auto_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.ScrollbarGutter).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.Inherits(PropertyId.ScrollbarGutter).Should().BeFalse();
    }
}
