// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Spec;

namespace Starling.Paint.Tests.Svg;

/// <summary>
/// SVG 1.1 chapter 13 — Gradients and Patterns. Modelled on the resvg
/// test-suite <c>tests/paint-servers/</c> cases: linearGradient,
/// radialGradient, pattern, stop-color, stop-opacity. Forms Starling does not
/// support (objectBoundingBox patterns, geometry-attribute inheritance via
/// href) are <c>[PendingFact]</c>.
/// </summary>
[TestClass]
[Spec("svg11", SvgRaster.Spec11Url, section: "pservers.html")]
public sealed class SvgConformancePaintServersTests
{
    private const string U = SvgRaster.Spec11Url;

    // --- linearGradient -----------------------------------------------------

    [Spec("svg11", U, section: "pservers.html#LinearGradients")]
    [SpecFact]
    public void LinearGradient_default_runs_left_to_right()
    {
        // Default objectBoundingBox + x1=0 x2=1 → horizontal red→blue.
        using var img = SvgRaster.Decode(
            "<svg width='40' height='10' viewBox='0 0 40 10'>" +
            "<defs><linearGradient id='g'>" +
            "<stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/>" +
            "</linearGradient></defs>" +
            "<rect width='40' height='10' fill='url(#g)'/></svg>");
        SvgRaster.At(img, 1, 5).R.Should().BeGreaterThan(SvgRaster.At(img, 1, 5).B, "red end on the left");
        SvgRaster.At(img, 38, 5).B.Should().BeGreaterThan(SvgRaster.At(img, 38, 5).R, "blue end on the right");
    }

    [Spec("svg11", U, section: "pservers.html#LinearGradientElementGradientUnitsAttribute")]
    [SpecFact]
    public void LinearGradient_userSpaceOnUse_uses_absolute_coordinates()
    {
        using var img = SvgRaster.Decode(
            "<svg width='40' height='10' viewBox='0 0 40 10'>" +
            "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='40' y2='0'>" +
            "<stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/>" +
            "</linearGradient></defs>" +
            "<rect width='40' height='10' fill='url(#g)'/></svg>");
        SvgRaster.At(img, 1, 5).R.Should().BeGreaterThan(SvgRaster.At(img, 1, 5).B);
        SvgRaster.At(img, 38, 5).B.Should().BeGreaterThan(SvgRaster.At(img, 38, 5).R);
    }

    [Spec("svg11", U, section: "pservers.html#LinearGradientElementGradientTransformAttribute")]
    [SpecFact]
    public void LinearGradient_with_gradientTransform_renders()
    {
        using var img = SvgRaster.Decode(
            "<svg width='40' height='40' viewBox='0 0 40 40'>" +
            "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='40' y2='0' " +
            "gradientTransform='rotate(90 20 20)'>" +
            "<stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/>" +
            "</linearGradient></defs>" +
            "<rect width='40' height='40' fill='url(#g)'/></svg>");
        // Rotated 90° → now vertical: red at top, blue at bottom.
        SvgRaster.At(img, 20, 2).R.Should().BeGreaterThan(SvgRaster.At(img, 20, 2).B);
        SvgRaster.At(img, 20, 38).B.Should().BeGreaterThan(SvgRaster.At(img, 20, 38).R);
    }

    [Spec("svg11", U, section: "pservers.html#LinearGradientElementSpreadMethodAttribute")]
    [SpecFact]
    public void LinearGradient_spreadMethod_repeat_tiles_the_ramp()
    {
        // Gradient spans only the first 10 user units; repeat tiles it across 40.
        using var img = SvgRaster.Decode(
            "<svg width='40' height='10' viewBox='0 0 40 10'>" +
            "<defs><linearGradient id='g' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='10' y2='0' spreadMethod='repeat'>" +
            "<stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/>" +
            "</linearGradient></defs>" +
            "<rect width='40' height='10' fill='url(#g)'/></svg>");
        // Each 10-unit tile restarts at red, so x≈11 is red again (low blue).
        SvgRaster.At(img, 11, 5).R.Should().BeGreaterThan(SvgRaster.At(img, 9, 5).R,
            "the ramp restarts at the next tile");
    }

    [Spec("svg11", U, section: "pservers.html#GradientStops")]
    [SpecFact]
    public void LinearGradient_inherits_stops_via_href()
    {
        using var img = SvgRaster.Decode(
            "<svg width='40' height='10' viewBox='0 0 40 10'>" +
            "<defs>" +
            "<linearGradient id='base'><stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>" +
            "<linearGradient id='ref' href='#base'/>" +
            "</defs>" +
            "<rect width='40' height='10' fill='url(#ref)'/></svg>");
        SvgRaster.At(img, 1, 5).R.Should().BeGreaterThan(SvgRaster.At(img, 1, 5).B, "stops inherited from #base");
        SvgRaster.At(img, 38, 5).B.Should().BeGreaterThan(SvgRaster.At(img, 38, 5).R);
    }

    [Spec("svg11", U, section: "pservers.html#GradientStops")]
    [SpecFact]
    public void Gradient_with_no_stops_paints_nothing()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><linearGradient id='g'/></defs>" +
            "<rect width='20' height='20' fill='url(#g)'/></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse("a gradient with no stops is not painted (SVG 1.1 §13.2.4)");
    }

    [Spec("svg11", U, section: "pservers.html#GradientStops")]
    [SpecFact]
    public void Gradient_with_one_stop_paints_a_solid_color()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><linearGradient id='g'><stop offset='0' stop-color='green'/></linearGradient></defs>" +
            "<rect width='20' height='20' fill='url(#g)'/></svg>");
        SvgRaster.IsGreen(SvgRaster.At(img, 10, 10)).Should().BeTrue("a single stop fills solid with its colour");
    }

    [Spec("svg11", U, section: "pservers.html#StopOpacityProperty")]
    [SpecFact]
    public void Stop_opacity_reduces_the_painted_alpha()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><linearGradient id='g'><stop offset='0' stop-color='black' stop-opacity='0.5'/></linearGradient></defs>" +
            "<rect width='20' height='20' fill='url(#g)'/></svg>");
        SvgRaster.At(img, 10, 10).A.Should().BeInRange(100, 160);
    }

    [Spec("svg11", U, section: "pservers.html#StopColorProperty")]
    [SpecFact]
    public void Stop_color_is_read_from_the_style_attribute()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><linearGradient id='g'><stop offset='0' style='stop-color:green'/></linearGradient></defs>" +
            "<rect width='20' height='20' fill='url(#g)'/></svg>");
        SvgRaster.IsGreen(SvgRaster.At(img, 10, 10)).Should().BeTrue();
    }

    // --- radialGradient -----------------------------------------------------

    [Spec("svg11", U, section: "pservers.html#RadialGradients")]
    [SpecFact]
    public void RadialGradient_is_bright_at_center_dark_at_edge()
    {
        using var img = SvgRaster.Decode(
            "<svg width='40' height='40' viewBox='0 0 40 40'>" +
            "<defs><radialGradient id='g'>" +
            "<stop offset='0' stop-color='white'/><stop offset='1' stop-color='black'/>" +
            "</radialGradient></defs>" +
            "<rect width='40' height='40' fill='url(#g)'/></svg>");
        int center = SvgRaster.At(img, 20, 20).R;
        int edge = SvgRaster.At(img, 1, 20).R;
        center.Should().BeGreaterThan(edge + 40, "the centre stop (white) is brighter than the edge (black)");
    }

    // --- pattern ------------------------------------------------------------

    [Spec("svg11", U, section: "pservers.html#Patterns")]
    [SpecFact]
    public void Pattern_userSpaceOnUse_tiles_across_the_shape()
    {
        using var img = SvgRaster.Decode(
            "<svg width='40' height='40' viewBox='0 0 40 40'>" +
            "<defs><pattern id='p' patternUnits='userSpaceOnUse' width='20' height='20'>" +
            "<rect width='10' height='10' fill='red'/></pattern></defs>" +
            "<rect width='40' height='40' fill='url(#p)'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 5, 5)).Should().BeTrue("first tile");
        SvgRaster.At(img, 15, 15).A.Should().Be(0, "gap inside the first tile");
        SvgRaster.IsRed(SvgRaster.At(img, 25, 25)).Should().BeTrue("the tile repeats");
    }

    [Spec("svg11", U, section: "pservers.html#PaintServerReference")]
    [SpecFact]
    public void Unresolved_paint_reference_paints_nothing()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'><rect width='20' height='20' fill='url(#missing)'/></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse();
    }

    [Spec("svg11", U, section: "pservers.html#PaintServerReference")]
    [SpecFact]
    public void Unresolved_paint_reference_uses_its_fallback_color()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'><rect width='20' height='20' fill='url(#missing) green'/></svg>");
        SvgRaster.IsGreen(SvgRaster.At(img, 10, 10)).Should().BeTrue("the fallback colour paints when the ref is missing");
    }

    // --- documented gaps ----------------------------------------------------

    [Spec("svg11", U, section: "pservers.html#PatternElementPatternUnitsAttribute")]
    [SpecFact]
    public void Pattern_objectBoundingBox_tiles_relative_to_the_shape()
    {
        // width/height are fractions of the 40x40 shape bbox → a 20x20 tile; the
        // userSpaceOnUse content rect fills the tile's top-left 10x10 quadrant.
        using var img = SvgRaster.Decode(
            "<svg width='40' height='40' viewBox='0 0 40 40'>" +
            "<defs><pattern id='p' patternUnits='objectBoundingBox' width='0.5' height='0.5'>" +
            "<rect width='10' height='10' fill='red'/></pattern></defs>" +
            "<rect width='40' height='40' fill='url(#p)'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 5, 5)).Should().BeTrue("first tile quadrant");
        SvgRaster.At(img, 15, 15).A.Should().Be(0, "gap inside the first tile");
        SvgRaster.IsRed(SvgRaster.At(img, 25, 25)).Should().BeTrue("the tile repeats across the shape");
    }

    [Spec("svg11", U, section: "pservers.html#LinearGradientElementHrefAttribute")]
    [SpecFact]
    public void Gradient_inherits_geometry_attributes_via_href()
    {
        // #ref defines its own stops but must inherit gradientUnits + x1/x2 from
        // #base (userSpaceOnUse, ramp over x[0,20]). At x=20 the ramp ends → pure
        // blue. Without inheritance the default ramp spans the box, so x=20 is the
        // mid-point → purple (red ~127). The pure-blue result discriminates.
        using var img = SvgRaster.Decode(
            "<svg width='40' height='10' viewBox='0 0 40 10'>" +
            "<defs>" +
            "<linearGradient id='base' gradientUnits='userSpaceOnUse' x1='0' y1='0' x2='20' y2='0'/>" +
            "<linearGradient id='ref' href='#base'>" +
            "<stop offset='0' stop-color='red'/><stop offset='1' stop-color='blue'/></linearGradient>" +
            "</defs>" +
            "<rect width='40' height='10' fill='url(#ref)'/></svg>");
        SvgRaster.At(img, 20, 5).R.Should().BeLessThan(40, "inherited ramp ends by x=20 → pure blue, not mid-ramp purple");
    }
}
