using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-images-3", "https://www.w3.org/TR/css-images-3/")]
[TestClass]
public sealed class CssGradientParserTests
{
    private static CssValue ParseValue(string source)
    {
        var sheet = new CssParser("a{background-image:" + source + "}").ParseStyleSheet();
        var decl = ((StyleRule)sheet.Rules.Single()).Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    private static CssGradient Parse(string source)
    {
        CssGradientParser.TryParse(ParseValue(source), out var g).Should().BeTrue();
        return g;
    }

    private static bool TryParse(string source) => CssGradientParser.TryParse(ParseValue(source), out _);

    [TestMethod]
    public void Linear_angle_two_stops()
    {
        var g = Parse("linear-gradient(90deg, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Linear);
        g.Repeating.Should().BeFalse();
        g.Line!.IsAngle.Should().BeTrue();
        g.Line.ToDegrees(100, 100).Should().Be(90);
        g.Stops.Should().HaveCount(2);
        g.Stops[0].Color.Should().Be(new CssColor(255, 0, 0));
        g.Stops[1].Color.Should().Be(new CssColor(0, 0, 255));
        g.Stops[0].Position.Should().BeNull();
    }

    [TestMethod]
    public void Linear_default_line_when_omitted()
    {
        var g = Parse("linear-gradient(red, blue)");
        g.Line.Should().BeNull();
        g.Stops.Should().HaveCount(2);
    }

    [TestMethod]
    public void Linear_to_right_resolves_to_90deg()
    {
        var g = Parse("linear-gradient(to right, red, blue)");
        g.Line!.IsAngle.Should().BeFalse();
        g.Line.SideX.Should().Be(CssGradientSideX.Right);
        g.Line.ToDegrees(100, 100).Should().Be(90);
    }

    [TestMethod]
    public void Linear_to_bottom_right_corner()
    {
        var g = Parse("linear-gradient(to bottom right, red, blue)");
        g.Line!.SideX.Should().Be(CssGradientSideX.Right);
        g.Line.SideY.Should().Be(CssGradientSideY.Bottom);
        // Square box → 45deg toward bottom-right corner (180 - 45).
        g.Line.ToDegrees(100, 100).Should().BeApproximately(135, 0.01);
    }

    [TestMethod]
    public void Explicit_stop_positions()
    {
        var g = Parse("linear-gradient(red 10%, blue 90%)");
        g.Stops.Should().HaveCount(2);
        g.Stops[0].Position.Should().NotBeNull();
        g.Stops[0].Position!.Value.IsPercent.Should().BeTrue();
        g.Stops[0].Position!.Value.Value.Should().Be(10);
        g.Stops[1].Position!.Value.Value.Should().Be(90);
    }

    [TestMethod]
    public void Three_stops()
    {
        var g = Parse("linear-gradient(red, green, blue)");
        g.Stops.Should().HaveCount(3);
        g.Stops[1].Color.Should().Be(new CssColor(0, 128, 0));
    }

    [TestMethod]
    public void Length_stop_position_resolves_to_px()
    {
        var g = Parse("linear-gradient(red 20px, blue)");
        g.Stops[0].Position!.Value.IsPercent.Should().BeFalse();
        g.Stops[0].Position!.Value.Value.Should().Be(20);
    }

    [TestMethod]
    public void Repeating_linear()
    {
        var g = Parse("repeating-linear-gradient(red, blue 20px)");
        g.Kind.Should().Be(CssGradientKind.Linear);
        g.Repeating.Should().BeTrue();
    }

    [TestMethod]
    public void Radial_circle()
    {
        var g = Parse("radial-gradient(circle, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Radial);
        g.Shape.Should().Be(CssRadialShape.Circle);
        g.Stops.Should().HaveCount(2);
    }

    [TestMethod]
    public void Radial_default_is_ellipse_farthest_corner()
    {
        var g = Parse("radial-gradient(red, blue)");
        g.Kind.Should().Be(CssGradientKind.Radial);
        g.Shape.Should().Be(CssRadialShape.Ellipse);
        g.Size.Should().Be(CssRadialSize.FarthestCorner);
    }

    [TestMethod]
    public void Radial_circle_closest_side_at_position()
    {
        var g = Parse("radial-gradient(circle closest-side at 25% 75%, red, blue)");
        g.Shape.Should().Be(CssRadialShape.Circle);
        g.Size.Should().Be(CssRadialSize.ClosestSide);
        g.Position.Should().NotBeNull();
        g.Position!.Value.FractionX.Should().Be(0.25);
        g.Position.Value.FractionY.Should().Be(0.75);
    }

    [TestMethod]
    public void Conic_parses_and_is_paintable()
    {
        var g = Parse("conic-gradient(red, blue)");
        g.Kind.Should().Be(CssGradientKind.Conic);
        g.IsPaintable.Should().BeTrue();
        g.Stops.Should().HaveCount(2);
        // No prelude: default from-angle and center.
        g.Line.Should().BeNull();
        g.Position.Should().BeNull();
    }

    [TestMethod]
    public void Conic_parses_from_angle_and_at_position()
    {
        var g = Parse("conic-gradient(from 122deg at 50% 50%, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Conic);
        g.Line.Should().NotBeNull();
        g.Line!.AngleDegrees.Should().Be(122);
        g.Position.Should().NotBeNull();
        g.Position!.Value.FractionX.Should().Be(0.5);
        g.Position.Value.FractionY.Should().Be(0.5);
    }

    [TestMethod]
    public void Conic_parses_angle_color_stops()
    {
        // A `90deg` stop sits a quarter turn around (0.25 of the gradient).
        var g = Parse("conic-gradient(red 0deg, blue 90deg)");
        g.Kind.Should().Be(CssGradientKind.Conic);
        g.Stops.Should().HaveCount(2);
        g.Stops[0].Position!.Value.ResolveFraction(360).Should().Be(0.0);
        g.Stops[1].Position!.Value.ResolveFraction(360).Should().BeApproximately(0.25, 1e-9);
    }

    [TestMethod]
    public void Single_stop_fails_soft()
    {
        // A gradient needs at least two stops; one stop is invalid.
        TryParse("linear-gradient(red)").Should().BeFalse();
    }

    [TestMethod]
    public void Non_gradient_function_rejected()
    {
        TryParse("url(foo.png)").Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CSS Images 4 §3.4 — transition hints
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Transition_hint_percentage_between_stops_is_parsed()
    {
        // `red, 30%, blue` — the bare 30% between two color stops is a transition
        // hint, not a color stop. It must be preserved in the stop list.
        var g = Parse("linear-gradient(red, 30%, blue)");
        g.Kind.Should().Be(CssGradientKind.Linear);
        g.Stops.Should().HaveCount(3, "hint + two real stops = 3 entries");
        g.Stops[0].IsHint.Should().BeFalse("first is a real stop");
        g.Stops[1].IsHint.Should().BeTrue("middle entry is a transition hint");
        g.Stops[1].Position.Should().NotBeNull("hint must carry its position");
        g.Stops[1].Position!.Value.Value.Should().Be(30, "hint position is 30%");
        g.Stops[2].IsHint.Should().BeFalse("last is a real stop");
    }

    [TestMethod]
    public void Transition_hint_absolute_px_between_stops_is_parsed()
    {
        var g = Parse("linear-gradient(red 0px, 40px, blue 100px)");
        g.Stops.Should().HaveCount(3);
        g.Stops[1].IsHint.Should().BeTrue("40px between stops is a hint");
        g.Stops[1].Position!.Value.IsPercent.Should().BeFalse("px hint is not a percent");
        g.Stops[1].Position!.Value.Value.Should().Be(40);
    }

    [TestMethod]
    public void Transition_hint_does_not_count_toward_two_stop_minimum()
    {
        // A gradient with only one real stop (plus hints) is invalid.
        TryParse("linear-gradient(red, 50%)").Should().BeFalse(
            "one real stop + one hint is not a valid gradient");
    }

    [TestMethod]
    public void Conic_angle_hint_between_stops_is_parsed()
    {
        // In a conic gradient, `90deg` by itself between stops is an angle hint.
        var g = Parse("conic-gradient(red, 90deg, blue)");
        g.Kind.Should().Be(CssGradientKind.Conic);
        g.Stops.Should().HaveCount(3);
        g.Stops[1].IsHint.Should().BeTrue("bare 90deg between stops is an angle hint");
        // 90deg = 25% of a full turn.
        g.Stops[1].Position!.Value.ResolveFraction(360).Should().BeApproximately(0.25, 1e-9);
    }

    // -----------------------------------------------------------------------
    // CSS Color 4 §12.3 — `in <colorspace>` interpolation prelude
    // -----------------------------------------------------------------------

    [TestMethod]
    public void Linear_gradient_in_oklch_is_parsed()
    {
        var g = Parse("linear-gradient(in oklch, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Linear);
        g.Interpolation.Should().NotBeNull("in oklch prelude must be stored");
        g.Interpolation!.ColorSpace.Should().Be(GradientColorSpace.Oklch);
        g.Interpolation.HueMethod.Should().Be(HueInterpolationMethod.Shorter, "default hue method");
    }

    [TestMethod]
    public void Linear_gradient_in_hsl_longer_hue_is_parsed()
    {
        var g = Parse("linear-gradient(in hsl longer hue, red, blue)");
        g.Interpolation.Should().NotBeNull();
        g.Interpolation!.ColorSpace.Should().Be(GradientColorSpace.Hsl);
        g.Interpolation.HueMethod.Should().Be(HueInterpolationMethod.Longer);
    }

    [TestMethod]
    public void Radial_gradient_in_oklab_is_parsed()
    {
        var g = Parse("radial-gradient(in oklab, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Radial);
        g.Interpolation!.ColorSpace.Should().Be(GradientColorSpace.Oklab);
    }

    [TestMethod]
    public void Conic_gradient_in_srgb_linear_is_parsed()
    {
        var g = Parse("conic-gradient(in srgb-linear, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Conic);
        g.Interpolation!.ColorSpace.Should().Be(GradientColorSpace.SrgbLinear);
    }

    [TestMethod]
    public void Conic_gradient_in_oklch_with_from_angle_is_parsed()
    {
        var g = Parse("conic-gradient(in oklch, from 45deg, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Conic);
        g.Interpolation!.ColorSpace.Should().Be(GradientColorSpace.Oklch);
        g.Line.Should().NotBeNull();
        g.Line!.AngleDegrees.Should().Be(45);
    }

    [TestMethod]
    public void Gradient_without_interpolation_prelude_has_null_interpolation()
    {
        var g = Parse("linear-gradient(red, blue)");
        g.Interpolation.Should().BeNull("no prelude → null interpolation");
    }

    [TestMethod]
    public void All_supported_color_spaces_are_parsed()
    {
        var spaces = new[]
        {
            ("srgb", GradientColorSpace.Srgb),
            ("srgb-linear", GradientColorSpace.SrgbLinear),
            ("oklab", GradientColorSpace.Oklab),
            ("oklch", GradientColorSpace.Oklch),
            ("hsl", GradientColorSpace.Hsl),
            ("hwb", GradientColorSpace.Hwb),
            ("lab", GradientColorSpace.Lab),
            ("lch", GradientColorSpace.Lch),
            ("display-p3", GradientColorSpace.DisplayP3),
            ("a98-rgb", GradientColorSpace.A98Rgb),
            ("prophoto-rgb", GradientColorSpace.ProphotoRgb),
            ("rec2020", GradientColorSpace.Rec2020),
            ("xyz-d50", GradientColorSpace.XyzD50),
            ("xyz-d65", GradientColorSpace.XyzD65),
        };
        foreach (var (name, expected) in spaces)
        {
            var g = Parse($"linear-gradient(in {name}, red, blue)");
            g.Interpolation!.ColorSpace.Should().Be(expected, $"color space '{name}' must parse");
        }
    }
}
