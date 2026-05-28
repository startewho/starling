using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.ViewTransitions;

namespace Starling.Css.Spec.Tests.CssViewTransitions1;

/// <summary>
/// Conformance for the <c>@view-transition</c> at-rule of
/// <see href="https://www.w3.org/TR/css-view-transitions-1/">CSS View Transitions Level 1</see> §2.1.
/// Parse level — the actual cross-document transition capture is not implemented.
/// </summary>
[TestClass]
[Spec("css-view-transitions-1", "https://www.w3.org/TR/css-view-transitions-1/", section: "2.1")]
public sealed class ViewTransitionRuleTests
{
    private static List<ViewTransitionRule> Parse(string css)
        => ViewTransitionParser.ParseAll(CssParser.ParseStyleSheet(css)).ToList();

    [Spec("css-view-transitions-1", "https://www.w3.org/TR/css-view-transitions-1/#view-transition-navigation-descriptor", section: "2.2")]
    [SpecFact]
    public void Navigation_auto_is_parsed()
    {
        var rules = Parse("@view-transition { navigation: auto; }");
        rules.Should().HaveCount(1);
        rules[0].Navigation.Should().Be("auto");
    }

    [Spec("css-view-transitions-1", "https://www.w3.org/TR/css-view-transitions-1/#view-transition-navigation-descriptor", section: "2.2")]
    [SpecFact]
    public void Navigation_initial_is_none_when_omitted()
    {
        var rules = Parse("@view-transition { }");
        rules.Single().Navigation.Should().Be("none");
    }

    [Spec("css-view-transitions-1", "https://www.w3.org/TR/css-view-transitions-1/#view-transition-types-descriptor", section: "2.3")]
    [SpecFact]
    public void Types_descriptor_collects_type_names()
    {
        var rules = Parse("@view-transition { navigation: auto; types: slide fade; }");
        rules.Single().Navigation.Should().Be("auto");
        rules.Single().Types.Should().Equal("slide", "fade");
    }
}
