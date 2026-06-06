// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Spec;

namespace Starling.Paint.Tests.Svg;

/// <summary>
/// SVG 1.1 chapters 14 (Clipping/Masking), 10 (Text) and 15 (Filters) —
/// modelled on the resvg test-suite <c>tests/masking/</c>, <c>tests/text/</c>
/// and <c>tests/filters/</c> chapters. The rasterizer treats these as
/// out-of-scope today, so every case is a <c>[PendingFact]</c>: it carries a
/// real, spec-correct assertion that documents the requirement and flips to
/// <c>[SpecFact]</c> when the feature lands. The <c>&lt;clipPath&gt;</c>,
/// <c>&lt;mask&gt;</c>, <c>&lt;text&gt;</c> and <c>&lt;filter&gt;</c> elements
/// are skipped as non-rendered containers today, so these run green by default.
/// </summary>
[TestClass]
[Spec("svg11", SvgRaster.Spec11Url, section: "masking-text-filters")]
public sealed class SvgConformanceMaskingTextFiltersTests
{
    private const string U = SvgRaster.Spec11Url;

    // --- chapter 14: clipping / masking -------------------------------------

    [Spec("svg11", U, section: "masking.html#ClipPathElement")]
    [SpecFact]
    public void ClipPath_clips_to_the_referenced_geometry()
    {
        // The rect is clipped to a circle at (10,10) r=6 → corners fall outside
        // the clip and stay transparent.
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><clipPath id='c'><circle cx='10' cy='10' r='6'/></clipPath></defs>" +
            "<rect width='20' height='20' fill='red' clip-path='url(#c)'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 10, 10)).Should().BeTrue("inside the clip circle");
        SvgRaster.At(img, 1, 1).A.Should().Be(0, "the corner is clipped away");
    }

    [Spec("svg11", U, section: "masking.html#ClipRuleProperty")]
    [SpecFact]
    public void Clip_rule_evenodd_affects_the_clip_region()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><clipPath id='c'><path clip-rule='evenodd' d='M0 0 H20 V20 H0 Z M6 4 H14 V16 H6 Z'/></clipPath></defs>" +
            "<rect width='20' height='20' fill='red' clip-path='url(#c)'/></svg>");
        SvgRaster.At(img, 2, 10).A.Should().BeGreaterThan(0, "outer ring of the clip");
        SvgRaster.At(img, 10, 10).A.Should().Be(0, "even-odd clip leaves the inner hole unpainted");
    }

    [Spec("svg11", U, section: "masking.html#MaskElement")]
    [SpecFact]
    public void Mask_modulates_alpha_by_luminance()
    {
        // A mask whose left half is white (visible) and right half black (hidden).
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='20' height='20'>" +
            "<rect width='10' height='20' fill='white'/></mask></defs>" +
            "<rect width='20' height='20' fill='red' mask='url(#m)'/></svg>");
        SvgRaster.At(img, 4, 10).A.Should().BeGreaterThan(150, "left half is unmasked");
        SvgRaster.At(img, 16, 10).A.Should().Be(0, "right half is masked out");
    }

    // --- chapter 10: text ---------------------------------------------------

    [Spec("svg11", U, section: "text.html#TextElement")]
    [SpecFact]
    public void Text_element_rasterizes_glyphs()
    {
        using var img = SvgRaster.Decode(
            "<svg width='80' height='30' viewBox='0 0 80 30'>" +
            "<text x='4' y='22' font-size='20' fill='black'>Hi</text></svg>");
        SvgRaster.AnyOpaque(img).Should().BeTrue("the text must produce glyph pixels");
    }

    [Spec("svg11", U, section: "text.html#TSpanElement")]
    [SpecFact]
    public void Tspan_positions_a_run_within_text()
    {
        using var img = SvgRaster.Decode(
            "<svg width='80' height='30' viewBox='0 0 80 30'>" +
            "<text x='4' y='22' font-size='16' fill='black'>A<tspan dx='10' fill='red'>B</tspan></text></svg>");
        SvgRaster.Count(img, SvgRaster.IsRed).Should().BeGreaterThan(0, "the coloured tspan run must paint");
    }

    // --- chapter 15: filters ------------------------------------------------

    [Spec("svg11", U, section: "filters.html#feGaussianBlurElement")]
    [SpecFact]
    public void FeGaussianBlur_softens_edges_outside_the_shape()
    {
        // A blurred black square bleeds a soft halo past its hard 6..14 bounds.
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><filter id='f'><feGaussianBlur stdDeviation='2'/></filter></defs>" +
            "<rect x='6' y='6' width='8' height='8' fill='black' filter='url(#f)'/></svg>");
        SvgRaster.At(img, 3, 10).A.Should().BeGreaterThan(0, "blur spreads alpha past the sharp edge");
    }

    [Spec("svg11", U, section: "filters.html#feFloodElement")]
    [SpecFact]
    public void FeFlood_fills_the_filter_region()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><filter id='f' x='0' y='0' width='1' height='1'>" +
            "<feFlood flood-color='red'/></filter></defs>" +
            "<rect width='20' height='20' fill='black' filter='url(#f)'/></svg>");
        SvgRaster.IsRed(SvgRaster.At(img, 10, 10)).Should().BeTrue("feFlood replaces the source with a flood colour");
    }

    [Spec("svg11", U, section: "filters.html#feOffsetElement")]
    [SpecFact]
    public void FeOffset_shifts_the_source()
    {
        using var img = SvgRaster.Decode(
            "<svg width='20' height='20' viewBox='0 0 20 20'>" +
            "<defs><filter id='f'><feOffset dx='6' dy='0'/></filter></defs>" +
            "<rect x='2' y='8' width='4' height='4' fill='black' filter='url(#f)'/></svg>");
        SvgRaster.At(img, 10, 10).A.Should().BeGreaterThan(0, "the source is shifted right by dx=6");
        SvgRaster.At(img, 3, 10).A.Should().Be(0, "and vacates its original position");
    }
}
