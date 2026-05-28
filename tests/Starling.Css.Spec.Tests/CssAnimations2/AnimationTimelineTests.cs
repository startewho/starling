using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssAnimations2;

/// <summary>
/// Property + cascade conformance for the Level-2 additions of
/// <see href="https://www.w3.org/TR/css-animations-2/">CSS Animations Level 2</see>:
/// <c>animation-timeline</c> and <c>animation-range-start/end</c>.
/// Parse + cascade level only — scroll/view-timeline driving is not yet implemented.
/// </summary>
[TestClass]
[Spec("css-animations-2", "https://www.w3.org/TR/css-animations-2/")]
public sealed class AnimationTimelineTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("css-animations-2", "https://www.w3.org/TR/css-animations-2/#animation-timeline", section: "3")]
    [SpecFact]
    public void Animation_timeline_parses_auto_none_and_name()
    {
        ValueOf("animation-timeline: auto", PropertyId.AnimationTimeline).Should().Be(new CssKeyword("auto"));
        ValueOf("animation-timeline: none", PropertyId.AnimationTimeline).Should().Be(new CssKeyword("none"));
        ValueOf("animation-timeline: --my-timeline", PropertyId.AnimationTimeline).Should().Be(new CssKeyword("--my-timeline"));
    }

    [Spec("css-animations-2", "https://www.w3.org/TR/css-animations-2/#animation-timeline", section: "3")]
    [SpecFact]
    public void Animation_timeline_initial_is_auto_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationTimeline).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.Inherits(PropertyId.AnimationTimeline).Should().BeFalse();
    }

    [Spec("css-animations-2", "https://www.w3.org/TR/css-animations-2/#animation-range", section: "4")]
    [SpecFact]
    public void Animation_range_start_parses_normal_and_keyword()
    {
        ValueOf("animation-range-start: normal", PropertyId.AnimationRangeStart).Should().Be(new CssKeyword("normal"));
        ValueOf("animation-range-start: cover", PropertyId.AnimationRangeStart).Should().Be(new CssKeyword("cover"));
    }

    [Spec("css-animations-2", "https://www.w3.org/TR/css-animations-2/#animation-range", section: "4")]
    [SpecFact]
    public void Animation_range_end_parses_percentage()
        => ValueOf("animation-range-end: 100%", PropertyId.AnimationRangeEnd).Should().Be(new CssPercentage(100));

    [Spec("css-animations-2", "https://www.w3.org/TR/css-animations-2/#animation-range", section: "4")]
    [SpecFact]
    public void Animation_range_initial_is_normal_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.AnimationRangeStart).Should().Be(new CssKeyword("normal"));
        PropertyRegistry.InitialValue(PropertyId.AnimationRangeEnd).Should().Be(new CssKeyword("normal"));
        PropertyRegistry.Inherits(PropertyId.AnimationRangeStart).Should().BeFalse();
    }
}
