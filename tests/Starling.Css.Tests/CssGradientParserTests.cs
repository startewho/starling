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
    public void Conic_parses_but_is_not_paintable()
    {
        var g = Parse("conic-gradient(red, blue)");
        g.Kind.Should().Be(CssGradientKind.Conic);
        g.IsPaintable.Should().BeFalse();
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
}
