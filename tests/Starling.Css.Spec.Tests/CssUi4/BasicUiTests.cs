using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssUi4;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-ui-4/">CSS Basic User Interface Module Level 4</see>:
/// outline, resize, text-overflow, caret-color, accent-color, appearance,
/// cursor, user-select, pointer-events.
/// </summary>
[TestClass]
[Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/")]
public sealed class BasicUiTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ----- outline shorthand + longhands (§3) -----

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#outline", section: "3.4")]
    [SpecFact]
    public void Outline_shorthand_sets_width_style_color()
    {
        var decls = Expand("outline: 2px solid red");
        decls.Single(d => d.Id == PropertyId.OutlineWidth).Value.Should().Be(new CssLength(2, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.OutlineStyle).Value.Should().Be(new CssKeyword("solid"));
        decls.Single(d => d.Id == PropertyId.OutlineColor).Value.Should().BeOfType<Starling.Css.Values.CssColor>();
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#outline", section: "3.4")]
    [SpecFact]
    public void Outline_shorthand_resets_omitted_longhands_to_initial()
    {
        // Only a style given — width resets to `medium`, color to `auto`.
        var decls = Expand("outline: dashed");
        decls.Single(d => d.Id == PropertyId.OutlineStyle).Value.Should().Be(new CssKeyword("dashed"));
        decls.Single(d => d.Id == PropertyId.OutlineWidth).Value.Should().Be(new CssKeyword("medium"));
        decls.Single(d => d.Id == PropertyId.OutlineColor).Value.Should().Be(new CssKeyword("auto"));
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#outline-style", section: "3.2")]
    [SpecFact]
    public void Outline_style_accepts_auto()
        => ValueOf("outline-style: auto", PropertyId.OutlineStyle).Should().Be(new CssKeyword("auto"));

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#outline-offset", section: "3.5")]
    [SpecFact]
    public void Outline_offset_parses_length_and_initial_is_zero()
    {
        ValueOf("outline-offset: 4px", PropertyId.OutlineOffset).Should().Be(new CssLength(4, CssLengthUnit.Px));
        PropertyRegistry.InitialValue(PropertyId.OutlineOffset).Should().Be(CssLength.Zero);
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#outline", section: "3")]
    [SpecFact]
    public void Outline_longhands_are_not_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.OutlineColor).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.OutlineStyle).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.OutlineWidth).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.OutlineOffset).Should().BeFalse();
    }

    // ----- resize (§6) -----

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#resize", section: "6")]
    [SpecFact]
    public void Resize_parses_keywords()
    {
        ValueOf("resize: none", PropertyId.Resize).Should().Be(new CssKeyword("none"));
        ValueOf("resize: both", PropertyId.Resize).Should().Be(new CssKeyword("both"));
        ValueOf("resize: horizontal", PropertyId.Resize).Should().Be(new CssKeyword("horizontal"));
        ValueOf("resize: vertical", PropertyId.Resize).Should().Be(new CssKeyword("vertical"));
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#resize", section: "6")]
    [SpecFact]
    public void Resize_initial_is_none_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.Resize).Should().Be(new CssKeyword("none"));
        PropertyRegistry.Inherits(PropertyId.Resize).Should().BeFalse();
    }

    // ----- text-overflow (§7) -----

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#text-overflow", section: "7")]
    [SpecFact]
    public void Text_overflow_parses_clip_and_ellipsis()
    {
        ValueOf("text-overflow: clip", PropertyId.TextOverflow).Should().Be(new CssKeyword("clip"));
        ValueOf("text-overflow: ellipsis", PropertyId.TextOverflow).Should().Be(new CssKeyword("ellipsis"));
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#text-overflow", section: "7")]
    [SpecFact]
    public void Text_overflow_initial_is_clip_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.TextOverflow).Should().Be(new CssKeyword("clip"));
        PropertyRegistry.Inherits(PropertyId.TextOverflow).Should().BeFalse();
    }

    // ----- pointing / selection UI props (§4, §5, §8) -----

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#insertion-caret", section: "4.1")]
    [SpecFact]
    public void Caret_color_accepts_auto_and_color_and_is_inherited()
    {
        ValueOf("caret-color: auto", PropertyId.CaretColor).Should().Be(new CssKeyword("auto"));
        ValueOf("caret-color: red", PropertyId.CaretColor).Should().BeOfType<Starling.Css.Values.CssColor>();
        PropertyRegistry.Inherits(PropertyId.CaretColor).Should().BeTrue();
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#widget-accent", section: "5")]
    [SpecFact]
    public void Accent_color_accepts_auto_and_color()
    {
        ValueOf("accent-color: auto", PropertyId.AccentColor).Should().Be(new CssKeyword("auto"));
        ValueOf("accent-color: blue", PropertyId.AccentColor).Should().BeOfType<Starling.Css.Values.CssColor>();
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#appearance-switching", section: "8")]
    [SpecFact]
    public void Appearance_accepts_none_and_auto()
    {
        ValueOf("appearance: none", PropertyId.Appearance).Should().Be(new CssKeyword("none"));
        ValueOf("appearance: auto", PropertyId.Appearance).Should().Be(new CssKeyword("auto"));
    }

    [Spec("css-ui-4", "https://www.w3.org/TR/css-ui-4/#cursor", section: "8")]
    [SpecFact]
    public void Cursor_parses_keyword_and_is_inherited()
    {
        ValueOf("cursor: pointer", PropertyId.Cursor).Should().Be(new CssKeyword("pointer"));
        PropertyRegistry.Inherits(PropertyId.Cursor).Should().BeTrue();
    }
}
