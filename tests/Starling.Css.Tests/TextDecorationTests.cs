using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/")]
[TestClass]
public sealed class TextDecorationTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue Single(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ---- text-decoration shorthand expansion (CSS Text Decoration 3 §2.5) ----

    [TestMethod]
    public void Underline_shorthand_sets_line()
    {
        Single("text-decoration: underline;", PropertyId.TextDecorationLine)
            .Should().Be(new CssKeyword("underline"));
    }

    [TestMethod]
    public void Shorthand_expands_line_style_color()
    {
        var decls = Expand("text-decoration: underline wavy red;");

        decls.Single(d => d.Id == PropertyId.TextDecorationLine).Value.Should().Be(new CssKeyword("underline"));
        decls.Single(d => d.Id == PropertyId.TextDecorationStyle).Value.Should().Be(new CssKeyword("wavy"));
        decls.Single(d => d.Id == PropertyId.TextDecorationColor).Value
            .Should().BeOfType<CssColor>().Which.Should().BeEquivalentTo(new { R = (byte)255, G = (byte)0, B = (byte)0 });
    }

    [TestMethod]
    public void Line_through_overline_keywords_parse()
    {
        Single("text-decoration: line-through;", PropertyId.TextDecorationLine).Should().Be(new CssKeyword("line-through"));
        Single("text-decoration: overline;", PropertyId.TextDecorationLine).Should().Be(new CssKeyword("overline"));
    }

    // ---- longhand parsing (CSS Text Decoration 3 §2) ----

    [TestMethod]
    public void Thickness_length_parses()
    {
        Single("text-decoration-thickness: 4px;", PropertyId.TextDecorationThickness)
            .Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

    [TestMethod]
    public void Underline_offset_length_parses()
    {
        Single("text-underline-offset: 2px;", PropertyId.TextUnderlineOffset)
            .Should().Be(new CssLength(2, CssLengthUnit.Px));
    }

    // ---- text-shadow parsing (CSS Text Decoration 3 §5) ----

    [TestMethod]
    public void Text_shadow_none_parses_to_empty()
    {
        Single("text-shadow: none;", PropertyId.TextShadow)
            .Should().BeOfType<CssTextShadow>().Which.Layers.Should().BeEmpty();
    }

    [TestMethod]
    public void Text_shadow_offset_blur_color_parses()
    {
        var value = Single("text-shadow: 1px 2px 3px gray;", PropertyId.TextShadow);
        var shadow = value.Should().BeOfType<CssTextShadow>().Subject;

        shadow.Layers.Should().ContainSingle();
        var layer = shadow.Layers[0];
        layer.OffsetX.Should().Be(1);
        layer.OffsetY.Should().Be(2);
        layer.Blur.Should().Be(3);
        layer.Color.Should().NotBeNull();
        layer.Color!.R.Should().Be(128);
    }

    [TestMethod]
    public void Text_shadow_color_may_lead_or_trail()
    {
        var leading = Single("text-shadow: red 1px 1px;", PropertyId.TextShadow).Should().BeOfType<CssTextShadow>().Subject;
        leading.Layers[0].Color!.R.Should().Be(255);
        leading.Layers[0].Blur.Should().Be(0); // no blur supplied → 0

        var trailing = Single("text-shadow: 1px 1px blue;", PropertyId.TextShadow).Should().BeOfType<CssTextShadow>().Subject;
        trailing.Layers[0].Color!.B.Should().Be(255);
    }

    [TestMethod]
    public void Text_shadow_defaults_color_to_current_color()
    {
        // No color → currentColor, signalled by a null Color on the layer.
        var shadow = Single("text-shadow: 2px 2px 2px;", PropertyId.TextShadow).Should().BeOfType<CssTextShadow>().Subject;
        shadow.Layers.Should().ContainSingle();
        shadow.Layers[0].Color.Should().BeNull();
    }

    [TestMethod]
    public void Text_shadow_multiple_layers_split_on_commas()
    {
        var shadow = Single("text-shadow: 1px 1px red, 2px 2px 3px blue;", PropertyId.TextShadow)
            .Should().BeOfType<CssTextShadow>().Subject;

        shadow.Layers.Should().HaveCount(2);
        shadow.Layers[0].OffsetX.Should().Be(1);
        shadow.Layers[0].Color!.R.Should().Be(255);
        shadow.Layers[1].OffsetX.Should().Be(2);
        shadow.Layers[1].Blur.Should().Be(3);
        shadow.Layers[1].Color!.B.Should().Be(255);
    }
}
