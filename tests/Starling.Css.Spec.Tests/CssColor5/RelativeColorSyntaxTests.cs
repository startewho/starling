using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssColor5;

/// <summary>
/// <see href="https://www.w3.org/TR/css-color-5/#relative-colors">CSS Color L5 §4</see>:
/// Relative color syntax — <c>rgb(from &lt;color&gt; r g b)</c> etc.
/// </summary>
[TestClass]
[Spec("css-color-5", "https://www.w3.org/TR/css-color-5/", section: "4")]
public sealed class RelativeColorSyntaxTests
{
    private static CssValue ParseValue(string value)
    {
        var sheet = CssParser.ParseStyleSheet($"a{{color:{value}}}");
        var rule = (StyleRule)sheet.Rules.Single();
        var decl = rule.Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#relative-colors")]
    [SpecFact]
    public void Rgb_from_resolves_channels_with_literal_replacements()
    {
        // CSS Color 5 §4: #336699 decomposes to r=51 g=102 b=153 in the sRGB
        // 0..255 channel basis. `rgb(from #336699 0 g b)` replaces r with the
        // literal 0 and keeps g and b → rgb(0, 102, 153).
        var color = ParseValue("rgb(from #336699 0 g b)").Should().BeOfType<Starling.Css.Values.CssColor>().Subject;
        color.R.Should().Be(0);
        color.G.Should().Be(102);
        color.B.Should().Be(153);
        color.A.Should().Be(255);
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#relative-colors")]
    [SpecFact]
    public void Rgb_from_allows_calc_on_extracted_channels()
    {
        // CSS Color 5 §4: red decomposes to r=255 g=0 b=0. `calc(r / 2)` yields
        // 127.5. The fractional channel survives in the float sRGB component
        // (C1 = 127.5/255); the 8-bit fallback rounds to 128.
        var color = ParseValue("rgb(from red calc(r / 2) g b)").Should().BeOfType<Starling.Css.Values.CssColor>().Subject;
        color.C1.Should().BeApproximately(127.5 / 255.0, 1e-9);
        color.G.Should().Be(0);
        color.B.Should().Be(0);
        color.R.Should().Be(128); // Math.Round(127.5/255 * 255) == 128
    }

    [Spec("css-color-5", "https://www.w3.org/TR/css-color-5/#relative-colors")]
    [SpecFact]
    public void Oklch_from_can_reuse_lightness_only()
    {
        // CSS Color 5 §4: keep the origin lightness, zero chroma and hue →
        // an achromatic gray of matching luminance. Using a literal origin
        // (var() resolution is out of scope for a value-parser unit test):
        // oklch(0.7 0.2 30) has L = 0.7, so the result is oklch(0.7 0 0).
        var color = ParseValue("oklch(from oklch(0.7 0.2 30) l 0 0)").Should().BeOfType<Starling.Css.Values.CssColor>().Subject;
        color.Space.Should().Be(ColorSpace.Oklch);
        color.C1.Should().BeApproximately(0.7, 1e-9); // lightness preserved
        color.C2.Should().Be(0.0);                    // chroma zeroed → achromatic
        color.C3.Should().Be(0.0);                    // hue zeroed
    }
}
