using AwesomeAssertions;
using Starling.Css.Cssom;
using Starling.Css.Selectors;
using Starling.Spec;

namespace Starling.Css.Tests;

/// <summary>Selector serialization (CSSOM §6.7.2) and selectorText round-trip.</summary>
[Spec("cssom-1", "https://drafts.csswg.org/cssom/#serialize-a-group-of-selectors", "§6.7.2")]
[TestClass]
public sealed class SelectorSerializerTests
{
    private static string RoundTrip(string selector)
    {
        var list = SelectorParser.ParseSelectorList(selector);
        return SelectorSerializer.Serialize(list);
    }

    [TestMethod]
    [DataRow("foo", "foo")]
    [DataRow(".bar", ".bar")]
    [DataRow("#baz", "#baz")]
    [DataRow("a b", "a b")]
    [DataRow("a > b", "a > b")]
    [DataRow("a + b", "a + b")]
    [DataRow("a ~ b", "a ~ b")]
    [DataRow("a.b.c", "a.b.c")]
    [DataRow("a, b, c", "a, b, c")]
    [DataRow("div:hover", "div:hover")]
    [DataRow("div::before", "div::before")]
    [DataRow("[type=text]", "[type=\"text\"]")]
    [DataRow(":nth-child(2n+1)", ":nth-child(2n+1)")]
    [DataRow(":nth-child(odd)", ":nth-child(2n+1)")]
    public void Serializes_selector(string input, string expected)
        => RoundTrip(input).Should().Be(expected);

    [TestMethod]
    public void SelectorText_setter_round_trips()
    {
        var rule = new CssomStyleRule("foo", new CssomDeclarationBlock());
        rule.TrySetSelectorText(":nth-child(odd)").Should().BeTrue();
        rule.SelectorTextRaw.Should().Be(":nth-child(2n+1)");
    }

    [TestMethod]
    public void SelectorText_setter_rejects_invalid_anb_leaving_previous()
    {
        var rule = new CssomStyleRule("foo", new CssomDeclarationBlock());
        rule.TrySetSelectorText("foo").Should().BeTrue();
        rule.TrySetSelectorText(":nth-child(+ n)").Should().BeFalse();
        rule.SelectorTextRaw.Should().Be("foo");
    }
}
