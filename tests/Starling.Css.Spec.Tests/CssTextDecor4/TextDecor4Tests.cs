using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using CssColorValue = Starling.Css.Values.CssColor;

namespace Starling.Css.Spec.Tests.CssTextDecor4;

/// <summary>
/// Property + cascade conformance for the additions and extensions introduced
/// by <see href="https://www.w3.org/TR/css-text-decor-4/">CSS Text Decoration 4</see>.
/// CSS Text Decoration 3 is covered separately under <c>CssTextDecor3/</c>; this
/// file only exercises the Level 4 surface (thickness, underline offset/position,
/// the spelling/grammar lines, and the thickness-aware shorthand).
/// </summary>
[TestClass]
[Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/")]
public sealed class TextDecor4Tests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ---- text-decoration-line (§3.1) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-line-property", section: "3.1")]
    [SpecFact]
    public void Text_decoration_line_none()
        => Expand("text-decoration-line: none;")
            .Single(d => d.Id == PropertyId.TextDecorationLine).Value
            .Should().Be(new CssKeyword("none"));

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-line-property", section: "3.1")]
    [SpecFact]
    public void Text_decoration_line_single_keywords()
    {
        foreach (var kw in new[] { "underline", "overline", "line-through", "blink" })
        {
            Expand($"text-decoration-line: {kw};")
                .Single(d => d.Id == PropertyId.TextDecorationLine).Value
                .Should().Be(new CssKeyword(kw));
        }
    }

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-line-property", section: "3.1")]
    [SpecFact]
    public void Text_decoration_line_multiple_keywords()
    {
        var value = Expand("text-decoration-line: underline overline line-through;")
            .Single(d => d.Id == PropertyId.TextDecorationLine).Value;
        var list = value.Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().Equal(
            new CssKeyword("underline"),
            new CssKeyword("overline"),
            new CssKeyword("line-through"));
    }

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-line-property", section: "3.1")]
    [SpecFact]
    public void Text_decoration_line_spelling_and_grammar_error_parse_as_keywords()
    {
        // Level 4 adds the `spelling-error` and `grammar-error` line keywords.
        // The longhand has no value-level validation, so they round-trip as
        // plain keywords today.
        Expand("text-decoration-line: spelling-error;")
            .Single(d => d.Id == PropertyId.TextDecorationLine).Value
            .Should().Be(new CssKeyword("spelling-error"));
        Expand("text-decoration-line: grammar-error;")
            .Single(d => d.Id == PropertyId.TextDecorationLine).Value
            .Should().Be(new CssKeyword("grammar-error"));
    }

    // ---- text-decoration-style (§3.2) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-style-property", section: "3.2")]
    [SpecFact]
    public void Text_decoration_style_keywords()
    {
        foreach (var kw in new[] { "solid", "double", "dotted", "dashed", "wavy" })
        {
            Expand($"text-decoration-style: {kw};")
                .Single(d => d.Id == PropertyId.TextDecorationStyle).Value
                .Should().Be(new CssKeyword(kw));
        }
    }

    // ---- text-decoration-color (§3.3) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-color-property", section: "3.3")]
    [SpecFact]
    public void Text_decoration_color_named_and_currentcolor()
    {
        Expand("text-decoration-color: red;")
            .Single(d => d.Id == PropertyId.TextDecorationColor).Value
            .Should().BeOfType<CssColorValue>();
        // The parser normalizes keyword case, so `currentColor` round-trips
        // as the lowercased `currentcolor`.
        Expand("text-decoration-color: currentColor;")
            .Single(d => d.Id == PropertyId.TextDecorationColor).Value
            .Should().Be(new CssKeyword("currentcolor"));
    }

    // ---- text-decoration-thickness (§3.4, new in L4) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-width-property", section: "3.4")]
    [SpecFact]
    public void Text_decoration_thickness_auto_and_from_font()
    {
        Expand("text-decoration-thickness: auto;")
            .Single(d => d.Id == PropertyId.TextDecorationThickness).Value
            .Should().Be(new CssKeyword("auto"));
        Expand("text-decoration-thickness: from-font;")
            .Single(d => d.Id == PropertyId.TextDecorationThickness).Value
            .Should().Be(new CssKeyword("from-font"));
    }

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-width-property", section: "3.4")]
    [SpecFact]
    public void Text_decoration_thickness_length_and_percentage()
    {
        Expand("text-decoration-thickness: 2px;")
            .Single(d => d.Id == PropertyId.TextDecorationThickness).Value
            .Should().Be(new CssLength(2, CssLengthUnit.Px));
        Expand("text-decoration-thickness: 10%;")
            .Single(d => d.Id == PropertyId.TextDecorationThickness).Value
            .Should().Be(new CssPercentage(10));
    }

    // ---- text-underline-position (§4.4) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property", section: "4.4")]
    [SpecFact]
    public void Text_underline_position_single_keywords()
    {
        foreach (var kw in new[] { "auto", "from-font", "under", "left", "right" })
        {
            Expand($"text-underline-position: {kw};")
                .Single(d => d.Id == PropertyId.TextUnderlinePosition).Value
                .Should().Be(new CssKeyword(kw));
        }
    }

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-underline-position-property", section: "4.4")]
    [SpecFact]
    public void Text_underline_position_under_left_combo()
    {
        var value = Expand("text-underline-position: under left;")
            .Single(d => d.Id == PropertyId.TextUnderlinePosition).Value;
        var list = value.Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().Equal(new CssKeyword("under"), new CssKeyword("left"));
    }

    // ---- text-underline-offset (§4.5, new in L4) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#underline-offset", section: "4.5")]
    [SpecFact]
    public void Text_underline_offset_auto_length_percentage()
    {
        Expand("text-underline-offset: auto;")
            .Single(d => d.Id == PropertyId.TextUnderlineOffset).Value
            .Should().Be(new CssKeyword("auto"));
        Expand("text-underline-offset: 0.1em;")
            .Single(d => d.Id == PropertyId.TextUnderlineOffset).Value
            .Should().Be(new CssLength(0.1, CssLengthUnit.Em));
        Expand("text-underline-offset: 20%;")
            .Single(d => d.Id == PropertyId.TextUnderlineOffset).Value
            .Should().Be(new CssPercentage(20));
    }

    // ---- text-decoration shorthand (§2.1) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-property", section: "2.1")]
    [SpecFact]
    public void Text_decoration_shorthand_sets_line_style_color()
    {
        var decls = Expand("text-decoration: underline wavy red;");
        decls.Single(d => d.Id == PropertyId.TextDecorationLine).Value
            .Should().Be(new CssKeyword("underline"));
        decls.Single(d => d.Id == PropertyId.TextDecorationStyle).Value
            .Should().Be(new CssKeyword("wavy"));
        decls.Single(d => d.Id == PropertyId.TextDecorationColor).Value
            .Should().BeOfType<CssColorValue>();
    }

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-property", section: "2.1")]
    [SpecFact]
    public void Text_decoration_shorthand_includes_thickness()
    {
        // The Level 4 shorthand grammar adds <'text-decoration-thickness'>.
        var decls = Expand("text-decoration: underline 2px;");
        decls.Single(d => d.Id == PropertyId.TextDecorationLine).Value
            .Should().Be(new CssKeyword("underline"));
        decls.Single(d => d.Id == PropertyId.TextDecorationThickness).Value
            .Should().Be(new CssLength(2, CssLengthUnit.Px));
    }

    // ---- Initial values (§2.1, §3.x, §4.x) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-line-property", section: "3.1")]
    [SpecFact]
    public void Initial_values_match_spec()
    {
        PropertyRegistry.InitialValue(PropertyId.TextDecorationLine).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.TextDecorationStyle).Should().Be(new CssKeyword("solid"));
        PropertyRegistry.InitialValue(PropertyId.TextDecorationColor).Should().Be(new CssKeyword("currentColor"));
        PropertyRegistry.InitialValue(PropertyId.TextDecorationThickness).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.TextUnderlineOffset).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.InitialValue(PropertyId.TextUnderlinePosition).Should().Be(new CssKeyword("auto"));
    }

    // ---- Inheritance (§3.x, §4.x) ----

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-decoration-line-property", section: "3.1")]
    [SpecFact]
    public void Decoration_longhands_are_not_inherited()
    {
        // Per the property tables, none of text-decoration-line/style/color/
        // thickness nor text-underline-offset/position inherit.
        PropertyRegistry.Inherits(PropertyId.TextDecorationLine).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.TextDecorationStyle).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.TextDecorationColor).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.TextDecorationThickness).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.TextUnderlineOffset).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.TextUnderlinePosition).Should().BeFalse();
    }

    [Spec("css-text-decor-4", "https://www.w3.org/TR/css-text-decor-4/#text-shadow-property", section: "5.1")]
    [SpecFact]
    public void Text_shadow_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.TextShadow).Should().BeTrue();
}
