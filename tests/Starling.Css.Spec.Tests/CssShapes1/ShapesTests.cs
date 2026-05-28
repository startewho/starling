using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssShapes1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-shapes-1/">CSS Shapes Module Level 1</see>.
/// Parse + cascade level only — float-area shaping is not yet implemented.
/// </summary>
[TestClass]
[Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/")]
public sealed class ShapesTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/#shape-outside-property", section: "2.1")]
    [SpecFact]
    public void Shape_outside_parses_none()
        => ValueOf("shape-outside: none", PropertyId.ShapeOutside).Should().Be(new CssKeyword("none"));

    [Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/#shape-outside-property", section: "2.1")]
    [SpecFact]
    public void Shape_outside_parses_basic_shape_function()
    {
        // circle()/inset()/polygon() are <basic-shape> functions (§3).
        ValueOf("shape-outside: circle(50%)", PropertyId.ShapeOutside).Should().BeOfType<CssFunctionValue>()
            .Which.Name.Should().Be("circle");
    }

    [Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/#shape-outside-property", section: "2.1")]
    [SpecFact]
    public void Shape_outside_parses_shape_box_keyword()
        => ValueOf("shape-outside: margin-box", PropertyId.ShapeOutside).Should().Be(new CssKeyword("margin-box"));

    [Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/#shape-margin-property", section: "2.2")]
    [SpecFact]
    public void Shape_margin_parses_length_and_initial_is_zero()
    {
        ValueOf("shape-margin: 10px", PropertyId.ShapeMargin).Should().Be(new CssLength(10, CssLengthUnit.Px));
        PropertyRegistry.InitialValue(PropertyId.ShapeMargin).Should().Be(CssLength.Zero);
    }

    [Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/#shape-image-threshold-property", section: "2.3")]
    [SpecFact]
    public void Shape_image_threshold_parses_number()
        => ValueOf("shape-image-threshold: 0.5", PropertyId.ShapeImageThreshold).Should().Be(new CssNumber(0.5));

    [Spec("css-shapes-1", "https://www.w3.org/TR/css-shapes-1/", section: "2")]
    [SpecFact]
    public void Shape_properties_are_not_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.ShapeOutside).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.ShapeMargin).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.ShapeImageThreshold).Should().BeFalse();
    }
}
