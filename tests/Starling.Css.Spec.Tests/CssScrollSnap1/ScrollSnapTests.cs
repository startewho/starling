using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssScrollSnap1;

/// <summary>
/// Property parse + cascade conformance for
/// <see href="https://www.w3.org/TR/css-scroll-snap-1/">CSS Scroll Snap 1</see>.
/// </summary>
[TestClass]
[Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/")]
public sealed class ScrollSnapTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue Value(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // CssValueList is a record whose IReadOnlyList field uses reference
    // equality, so .Be() never matches two distinct lists. Assert the
    // ordered keyword names instead.
    private static void ShouldBeKeywordList(CssValue value, params string[] keywords)
    {
        var list = value.Should().BeOfType<CssValueList>().Subject;
        list.Values.Select(v => ((CssKeyword)v).Name).Should().Equal(keywords);
    }

    // ---- scroll-snap-type (§4) ----

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-type", section: "4")]
    [SpecFact]
    public void Scroll_snap_type_none()
        => Value("scroll-snap-type: none;", PropertyId.ScrollSnapType)
            .Should().Be(new CssKeyword("none"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-type", section: "4")]
    [SpecFact]
    public void Scroll_snap_type_x_mandatory()
        => ShouldBeKeywordList(Value("scroll-snap-type: x mandatory;", PropertyId.ScrollSnapType), "x", "mandatory");

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-type", section: "4")]
    [SpecFact]
    public void Scroll_snap_type_y_proximity()
        => ShouldBeKeywordList(Value("scroll-snap-type: y proximity;", PropertyId.ScrollSnapType), "y", "proximity");

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-type", section: "4")]
    [SpecFact]
    public void Scroll_snap_type_both_mandatory()
        => ShouldBeKeywordList(Value("scroll-snap-type: both mandatory;", PropertyId.ScrollSnapType), "both", "mandatory");

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-type", section: "4")]
    [SpecFact]
    public void Scroll_snap_type_inline_proximity()
        => ShouldBeKeywordList(Value("scroll-snap-type: inline proximity;", PropertyId.ScrollSnapType), "inline", "proximity");

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-type", section: "4")]
    [SpecFact]
    public void Scroll_snap_type_initial_is_none()
        => PropertyRegistry.InitialValue(PropertyId.ScrollSnapType)
            .Should().Be(new CssKeyword("none"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-type", section: "4")]
    [SpecFact]
    public void Scroll_snap_type_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.ScrollSnapType).Should().BeFalse();

    // ---- scroll-snap-align (§5.1) ----

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-align", section: "5.1")]
    [SpecFact]
    public void Scroll_snap_align_none()
        => Value("scroll-snap-align: none;", PropertyId.ScrollSnapAlign)
            .Should().Be(new CssKeyword("none"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-align", section: "5.1")]
    [SpecFact]
    public void Scroll_snap_align_start()
        => Value("scroll-snap-align: start;", PropertyId.ScrollSnapAlign)
            .Should().Be(new CssKeyword("start"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-align", section: "5.1")]
    [SpecFact]
    public void Scroll_snap_align_end()
        => Value("scroll-snap-align: end;", PropertyId.ScrollSnapAlign)
            .Should().Be(new CssKeyword("end"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-align", section: "5.1")]
    [SpecFact]
    public void Scroll_snap_align_center()
        => Value("scroll-snap-align: center;", PropertyId.ScrollSnapAlign)
            .Should().Be(new CssKeyword("center"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-align", section: "5.1")]
    [SpecFact]
    public void Scroll_snap_align_two_value_start_end()
        => ShouldBeKeywordList(Value("scroll-snap-align: start end;", PropertyId.ScrollSnapAlign), "start", "end");

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-align", section: "5.1")]
    [SpecFact]
    public void Scroll_snap_align_initial_is_none()
        => PropertyRegistry.InitialValue(PropertyId.ScrollSnapAlign)
            .Should().Be(new CssKeyword("none"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-align", section: "5.1")]
    [SpecFact]
    public void Scroll_snap_align_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.ScrollSnapAlign).Should().BeFalse();

    // ---- scroll-snap-stop (§5.2) ----

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-stop", section: "5.2")]
    [SpecFact]
    public void Scroll_snap_stop_normal()
        => Value("scroll-snap-stop: normal;", PropertyId.ScrollSnapStop)
            .Should().Be(new CssKeyword("normal"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-stop", section: "5.2")]
    [SpecFact]
    public void Scroll_snap_stop_always()
        => Value("scroll-snap-stop: always;", PropertyId.ScrollSnapStop)
            .Should().Be(new CssKeyword("always"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-stop", section: "5.2")]
    [SpecFact]
    public void Scroll_snap_stop_initial_is_normal()
        => PropertyRegistry.InitialValue(PropertyId.ScrollSnapStop)
            .Should().Be(new CssKeyword("normal"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-snap-stop", section: "5.2")]
    [SpecFact]
    public void Scroll_snap_stop_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.ScrollSnapStop).Should().BeFalse();

    // ---- scroll-margin (§6.1) ----

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-margin", section: "6.1")]
    [SpecFact]
    public void Scroll_margin_shorthand_one_value_fills_four_sides()
    {
        var decls = Expand("scroll-margin: 10px;");
        var ten = new CssLength(10, CssLengthUnit.Px);
        decls.Single(d => d.Id == PropertyId.ScrollMarginTop).Value.Should().Be(ten);
        decls.Single(d => d.Id == PropertyId.ScrollMarginRight).Value.Should().Be(ten);
        decls.Single(d => d.Id == PropertyId.ScrollMarginBottom).Value.Should().Be(ten);
        decls.Single(d => d.Id == PropertyId.ScrollMarginLeft).Value.Should().Be(ten);
    }

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-margin", section: "6.1")]
    [SpecFact]
    public void Scroll_margin_shorthand_four_values_map_clockwise()
    {
        var decls = Expand("scroll-margin: 1px 2px 3px 4px;");
        decls.Single(d => d.Id == PropertyId.ScrollMarginTop).Value.Should().Be(new CssLength(1, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ScrollMarginRight).Value.Should().Be(new CssLength(2, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ScrollMarginBottom).Value.Should().Be(new CssLength(3, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ScrollMarginLeft).Value.Should().Be(new CssLength(4, CssLengthUnit.Px));
    }

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#propdef-scroll-margin-top", section: "6.1")]
    [SpecFact]
    public void Scroll_margin_top_longhand_length()
        => Value("scroll-margin-top: 12px;", PropertyId.ScrollMarginTop)
            .Should().Be(new CssLength(12, CssLengthUnit.Px));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-margin", section: "6.1")]
    [SpecFact]
    public void Scroll_margin_top_initial_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.ScrollMarginTop)
            .Should().Be(CssLength.Zero);

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-margin", section: "6.1")]
    [SpecFact]
    public void Scroll_margin_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.ScrollMarginTop).Should().BeFalse();

    // ---- scroll-padding (§6.2) ----

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-padding", section: "6.2")]
    [SpecFact]
    public void Scroll_padding_shorthand_two_values_map_to_four_sides()
    {
        var decls = Expand("scroll-padding: 5px 10px;");
        decls.Single(d => d.Id == PropertyId.ScrollPaddingTop).Value.Should().Be(new CssLength(5, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ScrollPaddingRight).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ScrollPaddingBottom).Value.Should().Be(new CssLength(5, CssLengthUnit.Px));
        decls.Single(d => d.Id == PropertyId.ScrollPaddingLeft).Value.Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#propdef-scroll-padding-left", section: "6.2")]
    [SpecFact]
    public void Scroll_padding_left_longhand_length()
        => Value("scroll-padding-left: 8px;", PropertyId.ScrollPaddingLeft)
            .Should().Be(new CssLength(8, CssLengthUnit.Px));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#propdef-scroll-padding-top", section: "6.2")]
    [SpecFact]
    public void Scroll_padding_top_auto_keyword()
        => Value("scroll-padding-top: auto;", PropertyId.ScrollPaddingTop)
            .Should().Be(new CssKeyword("auto"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-padding", section: "6.2")]
    [SpecFact]
    public void Scroll_padding_top_initial_is_auto()
        => PropertyRegistry.InitialValue(PropertyId.ScrollPaddingTop)
            .Should().Be(new CssKeyword("auto"));

    [Spec("css-scroll-snap-1", "https://www.w3.org/TR/css-scroll-snap-1/#scroll-padding", section: "6.2")]
    [SpecFact]
    public void Scroll_padding_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.ScrollPaddingTop).Should().BeFalse();
}
