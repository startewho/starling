// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Spec;

namespace Starling.Paint.Tests.Svg;

/// <summary>
/// SVG 1.1 chapters 5/7 — Document structure and coordinate systems: groups,
/// defs, use/symbol, transforms, styling cascade. Modelled on the resvg
/// test-suite <c>tests/structure/</c> cases. Unsupported constructs (switch,
/// systemLanguage, transform-origin, nested-svg viewports) are
/// <c>[PendingFact]</c>.
/// </summary>
[TestClass]
[Spec("svg11", SvgRaster.Spec11Url, section: "struct.html")]
public sealed class SvgConformanceStructureTests
{
    private const string U = SvgRaster.Spec11Url;

    // --- g ------------------------------------------------------------------

    [Spec("svg11", U, section: "struct.html#Groups")]
    [SpecFact]
    public void Group_inherits_paint_to_children()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'>" +
            "<g fill='red'><rect width='10' height='10'/></g></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 5, 5)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "masking.html#OpacityProperty")]
    [SpecFact]
    public void Group_opacity_composites_as_one_layer()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'>" +
            "<g opacity='0.5'><rect width='10' height='10' fill='black'/></g></svg>");
        SvgRaster.At(img, 5, 5).A.Should().BeInRange(100, 160);
    }

    // --- transform ----------------------------------------------------------

    [Spec("svg11", U, section: "coords.html#TransformAttribute")]
    [SpecFact]
    public void Transform_translate_moves_geometry()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<g transform='translate(10,10)'><rect width='4' height='4' fill='red'/></g></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 12, 12)).Should().BeTrue();
        SvgRaster.At(img, 2, 2).A.Should().Be(0);
    }

    [Spec("svg11", U, section: "coords.html#TransformAttribute")]
    [SpecFact]
    public void Transform_scale_enlarges_geometry()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<g transform='scale(2)'><rect width='5' height='5' fill='red'/></g></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 9, 9)).Should().BeTrue("a 5x5 rect scaled 2x covers 0..10");
        SvgRaster.At(img, 12, 12).A.Should().Be(0);
    }

    [Spec("svg11", U, section: "coords.html#TransformAttribute")]
    [SpecFact]
    public void Transform_matrix_equals_translate()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<rect width='4' height='4' fill='red' transform='matrix(1 0 0 1 10 10)'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 12, 12)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "coords.html#TransformAttribute")]
    [SpecFact]
    public void Transform_rotate_moves_geometry_off_origin()
    {
        // rotate(90) about the viewBox centre (20,20) maps (x,y) to (40-y, x).
        // A rect at x[16,22] y[2,8] lands at x[32,38] y[16,22] - still on canvas.
        using var img = SvgRaster.Decode(
            "<svg width='40' height='40' viewBox='0 0 40 40'>" +
            "<rect x='16' y='2' width='6' height='6' fill='red' transform='rotate(90 20 20)'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 35, 19)).Should().BeTrue("the rect rotated to the right side");
        SvgRaster.At(img, 18, 5).A.Should().Be(0, "and vacated its un-rotated position");
    }

    // --- defs / use / symbol ------------------------------------------------

    [Spec("svg11", U, section: "struct.html#DefsElement")]
    [SpecFact]
    public void Defs_content_is_not_rendered_directly()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'>" +
            "<defs><rect width='10' height='10' fill='red'/></defs></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse("<defs> children only paint when referenced");
    }

    [Spec("svg11", U, section: "struct.html#UseElement")]
    [SpecFact]
    public void Use_renders_a_referenced_shape()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><rect id='r' width='8' height='8' fill='red'/></defs>" +
            "<use href='#r' x='6' y='6'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 10, 10)).Should().BeTrue("the rect is reused at the (6,6) offset");
        SvgRaster.At(img, 2, 2).A.Should().Be(0);
    }

    [Spec("svg11", U, section: "struct.html#UseElement")]
    [SpecFact]
    public void Use_accepts_plain_href_without_xlink_namespace()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><circle id='c' cx='4' cy='4' r='4' fill='red'/></defs>" +
            "<use href='#c' x='6' y='6'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 10, 10)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "struct.html#SymbolElement")]
    [SpecFact]
    public void Use_of_symbol_renders_its_children()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<symbol id='s'><rect width='8' height='8' fill='red'/></symbol>" +
            "<use href='#s' x='6' y='6'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 10, 10)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "struct.html#UseElement")]
    [SpecFact]
    public void Use_with_missing_reference_paints_nothing()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'><use href='#nope' x='4' y='4'/></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse();
    }

    // --- styling cascade ----------------------------------------------------

    [Spec("svg11", U, section: "styling.html#StyleAttribute")]
    [SpecFact]
    public void Style_attribute_beats_presentation_attribute()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'>" +
            "<rect width='10' height='10' fill='red' style='fill:blue'/></svg>");
        SvgRaster.IsBlue(SvgRaster.At(img, 5, 5)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "styling.html#StyleElement")]
    [SpecFact]
    public void Style_element_class_rule_applies()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'>" +
            "<style>.c{fill:green}</style><rect class='c' width='10' height='10'/></svg>");
        SvgRaster.IsGreen(SvgRaster.At(img, 5, 5)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "linking.html#AElement")]
    [SpecFact]
    public void Anchor_element_renders_its_children()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'>" +
            "<a><rect width='10' height='10' fill='red'/></a></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 5, 5)).Should().BeTrue();
    }

    // --- documented gaps ----------------------------------------------------

    [Spec("svg11", U, section: "struct.html#SwitchElement")]
    [SpecFact]
    public void Switch_renders_only_the_first_matching_child()
    {
        // <switch> renders only the first child whose test attributes pass. The
        // first rect has none → it renders and the green one must be skipped.
        // Today every child renders, so green (drawn last) wrongly covers red.
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><switch>" +
            "<rect width='10' height='10' fill='red'/>" +
            "<rect width='10' height='10' fill='green'/></switch></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 5, 5)).Should().BeTrue("only the first matching child of <switch> renders");
    }

    [Spec("svg11", U, section: "coords.html#TransformOriginProperty")]
    [SpecFact]
    public void Transform_origin_pivots_about_the_box_center()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<rect x='6' y='6' width='8' height='8' fill='red' " +
            "transform='rotate(45)' style='transform-origin:10px 10px'/></svg>");
        // Rotated about its own centre (10,10) → still covers the centre.
        SvgRaster.IsRed(SvgRaster.At(img, 10, 10)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "struct.html#NestedSVGElements")]
    [SpecFact]
    public void Nested_svg_establishes_a_clipped_viewport()
    {
        // The inner <svg> at (10,10) sized 6x6 should clip its oversized child.
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<svg x='10' y='10' width='6' height='6'>" +
            "<rect width='100' height='100' fill='red'/></svg></svg>");
        SvgRaster.At(img, 2, 2).A.Should().Be(0, "content is clipped to the inner viewport at (10,10)");
        SvgRaster.IsRed(SvgRaster.At(img, 12, 12)).Should().BeTrue();
    }
}
