using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssBackgrounds3;

/// <summary>
/// <see href="https://www.w3.org/TR/css-backgrounds-3/#background">CSS Backgrounds 3 §3.4</see>:
/// the <c>background</c> shorthand.
/// </summary>
[TestClass]
[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/", section: "3.4")]
public sealed class BackgroundShorthandTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [SpecFact]
    public void Shorthand_sets_color_image_position_size_repeat_origin_clip_attachment()
    {
        // CSS Backgrounds 3 §3.4 — every longhand is set; when two box values
        // appear the first is background-origin, the second background-clip.
        var decls = Expand(
            "background: #fff url(\"a.png\") no-repeat fixed center/cover content-box padding-box;");

        decls.Single(d => d.Id == PropertyId.BackgroundColor).Value
            .Should().Be(new Starling.Css.Values.CssColor(255, 255, 255));
        decls.Single(d => d.Id == PropertyId.BackgroundImage).Value
            .Should().Be(new CssUrl("a.png"));
        decls.Single(d => d.Id == PropertyId.BackgroundRepeat).Value
            .Should().Be(new CssKeyword("no-repeat"));
        decls.Single(d => d.Id == PropertyId.BackgroundAttachment).Value
            .Should().Be(new CssKeyword("fixed"));
        decls.Single(d => d.Id == PropertyId.BackgroundPosition).Value
            .Should().Be(new CssKeyword("center"));
        decls.Single(d => d.Id == PropertyId.BackgroundSize).Value
            .Should().Be(new CssKeyword("cover"));
        decls.Single(d => d.Id == PropertyId.BackgroundOrigin).Value
            .Should().Be(new CssKeyword("content-box"));
        decls.Single(d => d.Id == PropertyId.BackgroundClip).Value
            .Should().Be(new CssKeyword("padding-box"));
    }

    [SpecFact]
    public void Shorthand_supports_multiple_comma_separated_layers()
    {
        // CSS Backgrounds 3 §3.4 — three comma-separated layers. Only the final
        // layer may carry a background-color; earlier layers reset it to the
        // initial transparent.
        var decls = Expand(
            "background: url(a.png) top left, linear-gradient(red, blue) bottom / cover, #eee;");

        // background-image is layered as a comma list of three layers.
        var image = decls.Single(d => d.Id == PropertyId.BackgroundImage).Value;
        image.Should().BeOfType<CssValueList>();
        ((CssValueList)image).Values.Should().HaveCount(3);

        // background-color is not layered: it is only the final layer's color.
        decls.Single(d => d.Id == PropertyId.BackgroundColor).Value
            .Should().Be(new Starling.Css.Values.CssColor(0xEE, 0xEE, 0xEE));

        // The third layer carries only a color (its image resets to none).
        var images = ((CssValueList)image).Values;
        images[0].Should().Be(new CssUrl("a.png"));
        images[2].Should().Be(new CssKeyword("none"));
    }
}
