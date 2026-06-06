// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Spec;

namespace Starling.Paint.Tests.Svg;

/// <summary>
/// SVG 1.1 chapter 11 — Painting: fill, stroke, dashing, caps, joins, opacity.
/// Modelled on the resvg test-suite <c>tests/painting/</c> cases. Features
/// Starling does not implement yet (display, visibility, markers,
/// mix-blend-mode, paint-order) are recorded as <c>[PendingFact]</c>.
/// </summary>
[TestClass]
[Spec("svg11", SvgRaster.Spec11Url, section: "painting.html")]
public sealed class SvgConformancePaintingTests
{
    private const string U = SvgRaster.Spec11Url;

    // --- fill ---------------------------------------------------------------

    [Spec("svg11", U, section: "painting.html#FillProperty")]
    [SpecFact]
    public void Fill_defaults_to_black()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><rect width='10' height='10'/></svg>");
        var p = SvgRaster.At(img, 5, 5);
        p.A.Should().Be(255);
        p.R.Should().BeLessThan(40);
        p.G.Should().BeLessThan(40);
        p.B.Should().BeLessThan(40);
    }

    [Spec("svg11", U, section: "painting.html#FillProperty")]
    [SpecFact]
    public void Fill_none_paints_nothing()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><rect width='10' height='10' fill='none'/></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse();
    }

    [Spec("svg11", U, section: "painting.html#FillRuleProperty")]
    [SpecFact]
    public void Fill_rule_evenodd_leaves_inner_hole()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<path fill-rule='evenodd' fill='black' d='M0 0 H20 V20 H0 Z M6 4 H14 V16 H6 Z'/></svg>");
        SvgRaster.At(img, 2, 10).A.Should().BeGreaterThan(0, "outer ring");
        SvgRaster.At(img, 10, 10).A.Should().Be(0, "even-odd punches the inner hole");
    }

    [Spec("svg11", U, section: "painting.html#FillRuleProperty")]
    [SpecFact]
    public void Fill_rule_nonzero_fills_inner_hole()
    {
        // Same two same-wound subpaths, but nonzero fills the overlap.
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<path fill-rule='nonzero' fill='black' d='M0 0 H20 V20 H0 Z M6 4 H14 V16 H6 Z'/></svg>");
        SvgRaster.At(img, 10, 10).A.Should().BeGreaterThan(0, "nonzero keeps the interior filled");
    }

    [Spec("svg11", U, section: "painting.html#FillOpacityProperty")]
    [SpecFact]
    public void Fill_opacity_scales_alpha()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><rect width='10' height='10' fill='black' fill-opacity='0.5'/></svg>");
        SvgRaster.At(img, 5, 5).A.Should().BeInRange(100, 160);
    }

    // --- color / currentColor ----------------------------------------------

    [Spec("svg11", U, section: "color.html#ColorProperty")]
    [SpecFact]
    public void CurrentColor_resolves_against_supplied_color()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><rect width='10' height='10' fill='currentColor'/></svg>",
            currentColor: SvgRaster.Rgb(0, 200, 0));
        SvgRaster.IsGreen(SvgRaster.At(img, 5, 5)).Should().BeTrue();
    }

    [Spec("svg11", U, section: "color.html#ColorProperty")]
    [SpecFact]
    public void Color_property_sets_currentColor_for_descendants()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'>" +
            "<g color='rgb(0,0,255)'><rect width='10' height='10' fill='currentColor'/></g></svg>");
        SvgRaster.IsBlue(SvgRaster.At(img, 5, 5)).Should().BeTrue();
    }

    // --- stroke -------------------------------------------------------------

    [Spec("svg11", U, section: "painting.html#StrokeProperty")]
    [SpecFact]
    public void Stroke_outlines_a_filled_shape()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<rect x='5' y='5' width='10' height='10' fill='none' stroke='red' stroke-width='2'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 5, 10)).Should().BeTrue("left edge is stroked");
        SvgRaster.At(img, 10, 10).A.Should().Be(0, "interior has no fill");
    }

    [Spec("svg11", U, section: "painting.html#StrokeWidthProperty")]
    [SpecFact]
    public void Stroke_width_zero_paints_no_stroke()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<rect x='5' y='5' width='10' height='10' fill='none' stroke='red' stroke-width='0'/></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse();
    }

    [Spec("svg11", U, section: "painting.html#StrokeOpacityProperty")]
    [SpecFact]
    public void Stroke_opacity_scales_alpha()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<line x1='0' y1='10' x2='20' y2='10' stroke='black' stroke-width='6' stroke-opacity='0.5'/></svg>");
        SvgRaster.At(img, 10, 10).A.Should().BeInRange(90, 170);
    }

    [Spec("svg11", U, section: "painting.html#StrokeLinecapProperty")]
    [SpecFact]
    public void Stroke_linecap_round_extends_past_the_endpoint()
    {
        // Line endpoint at x=12, half-width 3 → butt ends at x=12; round adds a
        // semicircle reaching x=9. Pixel (10,20) is 2px before the endpoint.
        const string Tmpl =
            "<svg width='40' height='40' viewBox='0 0 40 40'>" +
            "<line x1='12' y1='20' x2='28' y2='20' stroke='black' stroke-width='6' stroke-linecap='{0}'/></svg>";
        using var round = SvgRaster.Decode(string.Format(Tmpl, "round"));
        using var butt = SvgRaster.Decode(string.Format(Tmpl, "butt"));
        SvgRaster.At(round, 10, 20).A.Should().BeGreaterThan(120, "round cap covers x=10");
        SvgRaster.At(butt, 10, 20).A.Should().BeLessThan(60, "butt cap stops at x=12");
    }

    [Spec("svg11", U, section: "painting.html#StrokeLinejoinProperty")]
    [SpecFact]
    public void Stroke_linejoin_variants_all_render()
    {
        foreach (var join in new[] { "miter", "round", "bevel" })
        {
            using var img = SvgRaster.Decode(
                "<svg width='40' height='40' viewBox='0 0 40 40'>" +
                $"<path d='M6 34 L20 6 L34 34' fill='none' stroke='black' stroke-width='6' stroke-linejoin='{join}'/></svg>");
            SvgRaster.At(img, 20, 12).A.Should().BeGreaterThan(0, $"the {join} join near the apex paints");
        }
    }

    // --- stroke-dasharray ---------------------------------------------------

    [Spec("svg11", U, section: "painting.html#StrokeDasharrayProperty")]
    [SpecFact]
    public void Stroke_dasharray_creates_gaps()
    {
        using var solid = SvgRaster.Decode(
            "<svg width='40' height='8' viewBox='0 0 40 8'>" +
            "<line x1='0' y1='4' x2='40' y2='4' stroke='black' stroke-width='4'/></svg>");
        using var dashed = SvgRaster.Decode(
            "<svg width='40' height='8' viewBox='0 0 40 8'>" +
            "<line x1='0' y1='4' x2='40' y2='4' stroke='black' stroke-width='4' stroke-dasharray='4 4'/></svg>");
        SvgRaster.OpaqueInRow(dashed, 4).Should()
            .BeLessThan(SvgRaster.OpaqueInRow(solid, 4), "dashing removes covered pixels");
        SvgRaster.OpaqueInRow(dashed, 4).Should().BeGreaterThan(0, "but some dashes remain");
    }

    [Spec("svg11", U, section: "painting.html#StrokeDasharrayProperty")]
    [SpecFact]
    public void Stroke_dasharray_none_is_solid()
    {
        using var img = SvgRaster.Decode(
            "<svg width='40' height='8' viewBox='0 0 40 8'>" +
            "<line x1='0' y1='4' x2='40' y2='4' stroke='black' stroke-width='4' stroke-dasharray='none'/></svg>");
        SvgRaster.OpaqueInRow(img, 4).Should().BeGreaterThan(30, "no dashing → the whole line is drawn");
    }

    [Spec("svg11", U, section: "painting.html#StrokeDasharrayProperty")]
    [SpecFact]
    public void Stroke_dasharray_odd_count_is_doubled()
    {
        // "5" → "5 5" (SVG 1.1 §11.4). A single odd value still produces gaps.
        using var img = SvgRaster.Decode(
            "<svg width='40' height='8' viewBox='0 0 40 8'>" +
            "<line x1='0' y1='4' x2='40' y2='4' stroke='black' stroke-width='4' stroke-dasharray='5'/></svg>");
        SvgRaster.OpaqueInRow(img, 4).Should().BeInRange(1, 35, "an odd dash list is repeated, giving gaps");
    }

    // --- opacity ------------------------------------------------------------

    [Spec("svg11", U, section: "masking.html#OpacityProperty")]
    [SpecFact]
    public void Element_opacity_scales_alpha()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><rect width='10' height='10' fill='black' opacity='0.5'/></svg>");
        SvgRaster.At(img, 5, 5).A.Should().BeInRange(100, 160);
    }

    // --- documented gaps (resvg painting/* cases not yet implemented) -------

    [Spec("svg11", U, section: "painting.html#DisplayProperty")]
    [SpecFact]
    public void Display_none_hides_the_element()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><rect width='10' height='10' fill='red' display='none'/></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse("display:none must suppress rendering");
    }

    [Spec("svg11", U, section: "painting.html#VisibilityProperty")]
    [SpecFact]
    public void Visibility_hidden_hides_the_element()
    {
        using var img = SvgRaster.Decode(
            "<svg width='10' height='10' viewBox='0 0 10 10'><rect width='10' height='10' fill='red' visibility='hidden'/></svg>");
        SvgRaster.AnyOpaque(img).Should().BeFalse("visibility:hidden must suppress rendering");
    }

    [Spec("svg11", U, section: "painting.html#MarkerProperties")]
    [SpecFact]
    public void Marker_end_draws_a_marker_glyph()
    {
        using var img = SvgRaster.Decode(
            "<svg width='40' height='40' viewBox='0 0 40 40'>" +
            "<defs><marker id='m' markerWidth='6' markerHeight='6' refX='3' refY='3'>" +
            "<circle cx='3' cy='3' r='3' fill='red'/></marker></defs>" +
            "<path d='M4 20 H30' stroke='black' stroke-width='2' marker-end='url(#m)'/></svg>");
        // The red marker glyph should appear at the path end (~x=30).
        SvgRaster.Count(img, SvgRaster.IsRed).Should().BeGreaterThan(0);
    }

    [Spec("svg11", U, section: "painting.html#PaintOrderProperty")]
    [SpecFact]
    public void Paint_order_stroke_first_puts_fill_on_top()
    {
        // The rect edge is at x=5; a width-6 stroke straddles it over x[2,8].
        // Default order draws fill then stroke, so the inner band (x=7) is blue.
        // paint-order:stroke draws the stroke first, then fill over it → x=7 is red.
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<rect x='5' y='5' width='10' height='10' fill='red' stroke='blue' stroke-width='6' paint-order='stroke'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 7, 10)).Should().BeTrue("fill paints over the stroke in the inner band");
    }

    [Spec("svg11", U, section: "painting.html#MixBlendMode")]
    [SpecFact]
    public void Mix_blend_mode_multiply_darkens_overlap()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<rect width='20' height='20' fill='#ff0000'/>" +
            "<rect width='20' height='20' fill='#00ffff' style='mix-blend-mode:multiply'/></svg>");
        // red * cyan = black.
        var p = SvgRaster.At(img, 10, 10);
        (p.R + p.G + p.B).Should().BeLessThan(60, "multiply of red and cyan is black");
    }
}
