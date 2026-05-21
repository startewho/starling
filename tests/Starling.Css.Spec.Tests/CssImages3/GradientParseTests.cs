using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;
using Color = Starling.Css.Values.CssColor;

namespace Starling.Css.Spec.Tests.CssImages3;

/// <summary>
/// Conformance for <see href="https://www.w3.org/TR/css-images-3/#gradients">CSS Images 3 §3 — Gradients</see>:
/// the <c>linear-gradient()</c> / <c>radial-gradient()</c> functions, their
/// gradient line / shape / size syntax, and color stops.
/// </summary>
[TestClass]
[Spec("css-images-3", "https://www.w3.org/TR/css-images-3/", section: "3")]
public sealed class GradientParseTests
{
    private static CssGradient Parse(string value)
    {
        var sheet = new CssParser("a{background-image:" + value + "}").ParseStyleSheet();
        var decl = ((StyleRule)sheet.Rules.Single()).Declarations.Single();
        var parsed = CssValueParser.Parse(decl.Value);
        CssGradientParser.TryParse(parsed, out var g).Should().BeTrue($"`{value}` should parse to a gradient");
        return g;
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-images-3/#linear-gradients"/>
    /// — <c>linear-gradient([ &lt;angle&gt; | to &lt;side-or-corner&gt; ]?, &lt;color-stop-list&gt;)</c>.</summary>
    [Spec("css-images-3", "https://www.w3.org/TR/css-images-3/#linear-gradients", section: "3.1")]
    [SpecFact]
    public void Linear_gradient_with_angle_and_two_stops()
    {
        var g = Parse("linear-gradient(90deg, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Linear);
        g.Line!.ToDegrees(100, 100).Should().Be(90);
        g.Stops.Should().HaveCount(2);
        g.Stops[0].Color.Should().Be(new Color(255, 0, 0));
        g.Stops[^1].Color.Should().Be(new Color(0, 0, 255));
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-images-3/#linear-gradient-syntax"/>
    /// — <c>to right</c> is equivalent to a 90deg gradient line.</summary>
    [Spec("css-images-3", "https://www.w3.org/TR/css-images-3/#linear-gradient-syntax", section: "3.1")]
    [SpecFact]
    public void Linear_gradient_to_side_keyword()
    {
        var g = Parse("linear-gradient(to right, red, blue)");
        g.Line!.SideX.Should().Be(CssGradientSideX.Right);
        g.Line.ToDegrees(100, 100).Should().Be(90);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-images-3/#color-stop-syntax"/>
    /// — color stops carry an optional <c>&lt;length-percentage&gt;</c> position.</summary>
    [Spec("css-images-3", "https://www.w3.org/TR/css-images-3/#color-stop-syntax", section: "3.4")]
    [SpecFact]
    public void Color_stops_with_explicit_positions()
    {
        var g = Parse("linear-gradient(red 0%, lime 50%, blue 100%)");
        g.Stops.Should().HaveCount(3);
        g.Stops[0].Position!.Value.Value.Should().Be(0);
        g.Stops[1].Position!.Value.Value.Should().Be(50);
        g.Stops[2].Position!.Value.Value.Should().Be(100);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-images-3/#radial-gradients"/>
    /// — <c>radial-gradient([&lt;ending-shape&gt; || &lt;size&gt;]? [at &lt;position&gt;]?, &lt;color-stop-list&gt;)</c>.</summary>
    [Spec("css-images-3", "https://www.w3.org/TR/css-images-3/#radial-gradients", section: "3.2")]
    [SpecFact]
    public void Radial_gradient_circle_with_size_and_position()
    {
        var g = Parse("radial-gradient(circle closest-side at center, red, blue)");
        g.Kind.Should().Be(CssGradientKind.Radial);
        g.Shape.Should().Be(CssRadialShape.Circle);
        g.Size.Should().Be(CssRadialSize.ClosestSide);
        g.Position!.Value.FractionX.Should().Be(0.5);
        g.Position!.Value.FractionY.Should().Be(0.5);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-images-3/#repeating-gradients"/>
    /// — the <c>repeating-</c> prefix marks the gradient as tiling.</summary>
    [Spec("css-images-3", "https://www.w3.org/TR/css-images-3/#repeating-gradients", section: "3.5")]
    [SpecFact]
    public void Repeating_radial_gradient_flagged()
    {
        var g = Parse("repeating-radial-gradient(red, blue 30px)");
        g.Kind.Should().Be(CssGradientKind.Radial);
        g.Repeating.Should().BeTrue();
    }
}
