// Hand-written conformance for CSS Text Decoration Module Level 3 (wp:M5-css-15).

using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssTextDecor3;

/// <summary>
/// Property conformance for
/// <see href="https://www.w3.org/TR/css-text-decor-3/">CSS Text Decoration Module Level 3</see>.
/// </summary>
[TestClass]
[Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/")]
public sealed class PropertyTests
{
    private static List<PropertyDeclaration> Expand(string property, string value)
    {
        var sheet = CssParser.ParseStyleSheet($"a{{{property}:{value}}}");
        var rule = (StyleRule)sheet.Rules.Single();
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue Single(string property, string value, PropertyId id)
        => Expand(property, value).Single(d => d.Id == id).Value;

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-decor-3/#text-decoration-line-property"/>
    /// <para><c>text-decoration-line</c> accepts <c>none | [ underline || overline ||
    /// line-through || blink ]</c>; each keyword is preserved.</para>
    /// </summary>
    [Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/#text-decoration-line-property", "2.1")]
    [SpecFact]
    public void Parses_text_decoration_line()
    {
        Single("text-decoration-line", "underline", PropertyId.TextDecorationLine).Should().Be(new CssKeyword("underline"));
        Single("text-decoration-line", "overline", PropertyId.TextDecorationLine).Should().Be(new CssKeyword("overline"));
        Single("text-decoration-line", "line-through", PropertyId.TextDecorationLine).Should().Be(new CssKeyword("line-through"));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-decor-3/#text-decoration-style-property"/>
    /// <para><c>text-decoration-style</c> accepts <c>solid | double | dotted | dashed | wavy</c>.</para>
    /// </summary>
    [Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/#text-decoration-style-property", "2.2")]
    [SpecFact]
    public void Parses_text_decoration_style()
    {
        Single("text-decoration-style", "wavy", PropertyId.TextDecorationStyle).Should().Be(new CssKeyword("wavy"));
        Single("text-decoration-style", "dashed", PropertyId.TextDecorationStyle).Should().Be(new CssKeyword("dashed"));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-decor-3/#text-decoration-color-property"/>
    /// <para><c>text-decoration-color</c> accepts a <c>&lt;color&gt;</c> (initial
    /// <c>currentColor</c>); a named color resolves to a typed
    /// <see cref="Starling.Css.Values.CssColor"/>.</para>
    /// </summary>
    [Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/#text-decoration-color-property", "2.3")]
    [SpecFact]
    public void Parses_text_decoration_color()
    {
        Single("text-decoration-color", "red", PropertyId.TextDecorationColor)
            .Should().BeOfType<Starling.Css.Values.CssColor>().Which.Should().BeEquivalentTo(new { R = (byte)255, G = (byte)0, B = (byte)0 });
        // The value parser lowercases idents, so the keyword round-trips as
        // "currentcolor"; it stays an unresolved keyword (substituted at paint).
        Single("text-decoration-color", "currentColor", PropertyId.TextDecorationColor)
            .Should().Be(new CssKeyword("currentcolor"));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-decor-3/#text-decoration-property"/>
    /// <para>The <c>text-decoration</c> shorthand sets line, style, and color in any order.</para>
    /// </summary>
    [Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/#text-decoration-property", "2.5")]
    [SpecFact]
    public void Shorthand_expands_to_longhands()
    {
        var decls = Expand("text-decoration", "underline dotted blue");
        decls.Single(d => d.Id == PropertyId.TextDecorationLine).Value.Should().Be(new CssKeyword("underline"));
        decls.Single(d => d.Id == PropertyId.TextDecorationStyle).Value.Should().Be(new CssKeyword("dotted"));
        decls.Single(d => d.Id == PropertyId.TextDecorationColor).Value
            .Should().BeOfType<Starling.Css.Values.CssColor>().Which.Should().BeEquivalentTo(new { R = (byte)0, G = (byte)0, B = (byte)255 });
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-text-decor-3/#text-shadow-property"/>
    /// <para><c>text-shadow</c> accepts <c>none | [ &lt;color&gt;? &amp;&amp;
    /// &lt;length&gt;{2,3} ]#</c> — an offset, optional blur, optional color, and
    /// comma-separated layers. The keyword <c>none</c> yields no layers.</para>
    /// </summary>
    [Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/#text-shadow-property", "5")]
    [SpecFact]
    public void Parses_text_shadow_layers()
    {
        Single("text-shadow", "none", PropertyId.TextShadow)
            .Should().BeOfType<CssTextShadow>().Which.Layers.Should().BeEmpty();

        var multi = Single("text-shadow", "1px 1px red, 2px 3px 4px blue", PropertyId.TextShadow)
            .Should().BeOfType<CssTextShadow>().Subject;
        multi.Layers.Should().HaveCount(2);
        multi.Layers[0].OffsetX.Should().Be(1);
        multi.Layers[0].OffsetY.Should().Be(1);
        multi.Layers[0].Blur.Should().Be(0);
        multi.Layers[0].Color!.R.Should().Be(255);
        multi.Layers[1].Blur.Should().Be(4);
        multi.Layers[1].Color!.B.Should().Be(255);
    }
}
