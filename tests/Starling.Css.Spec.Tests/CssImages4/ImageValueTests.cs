using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssImages4;

/// <summary>
/// Value parsing conformance for
/// <see href="https://www.w3.org/TR/css-images-4/">CSS Images Module Level 4</see>:
/// the <c>image-set()</c> and <c>cross-fade()</c> image functions.
/// Parse level only — resolution selection + cross-fade compositing are not yet implemented.
/// </summary>
[TestClass]
[Spec("css-images-4", "https://www.w3.org/TR/css-images-4/")]
public sealed class ImageValueTests
{
    private static CssValue BackgroundImageValue(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ background-image: {css}; }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse)
            .Single(d => d.Id == PropertyId.BackgroundImage).Value;
    }

    [Spec("css-images-4", "https://www.w3.org/TR/css-images-4/#image-set-notation", section: "3")]
    [SpecFact]
    public void Image_set_parses_as_a_function_value()
    {
        var value = BackgroundImageValue("image-set(\"a.png\" 1x, \"b.png\" 2x)");
        value.Should().BeOfType<CssFunctionValue>().Which.Name.Should().Be("image-set");
    }

    [Spec("css-images-4", "https://www.w3.org/TR/css-images-4/#cross-fade-function", section: "4")]
    [SpecFact]
    public void Cross_fade_parses_as_a_function_value()
    {
        var value = BackgroundImageValue("cross-fade(url(a.png), url(b.png), 50%)");
        value.Should().BeOfType<CssFunctionValue>().Which.Name.Should().Be("cross-fade");
    }

    [Spec("css-images-4", "https://www.w3.org/TR/css-images-4/#image-set-notation", section: "3")]
    [SpecFact]
    public void Image_set_with_type_descriptor_parses()
    {
        var value = BackgroundImageValue("image-set(\"a.avif\" type(\"image/avif\"), \"a.png\" type(\"image/png\"))");
        value.Should().BeOfType<CssFunctionValue>().Which.Name.Should().Be("image-set");
    }
}
