using AwesomeAssertions;
using Starling.Css.Cssom;
using Starling.Css.Parser;
using Starling.Spec;

namespace Starling.Css.Tests;

/// <summary>
/// CSSOM value canonicalization + declaration/rule round-trips (CSSOM §6.4,
/// CSS Syntax §8). Mirrors css/css-syntax/decimal-points-in-numbers.html.
/// </summary>
[Spec("css-syntax-3", "https://drafts.csswg.org/css-syntax/#consume-number", "§8")]
[TestClass]
public sealed class CssomValueTests
{
    private static CssomStyleRule FirstRule(string css)
    {
        var sheet = new CssomStyleSheet(CssParser.ParseStyleSheet(css));
        return (CssomStyleRule)sheet.Rules.First(r => r is not null)!;
    }

    [TestMethod]
    [DataRow("1.0", "1")]
    [DataRow(".1", "0.1")]
    [DataRow("1.50", "1.5")]
    [DataRow("10", "10")]
    [DataRow("-0.5", "-0.5")]
    public void Canonicalizes_number_line_height(string input, string expected)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("line-height", "0", null);
        rule.Style.SetProperty("line-height", input, null);
        rule.Style.GetPropertyValue("line-height").Should().Be(expected);
    }

    [TestMethod]
    [DataRow("1.0px", "1px")]
    [DataRow(".1px", "0.1px")]
    [DataRow("12px", "12px")]
    public void Canonicalizes_dimension_width(string input, string expected)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("width", "0px", null);
        rule.Style.SetProperty("width", input, null);
        rule.Style.GetPropertyValue("width").Should().Be(expected);
    }

    [TestMethod]
    [DataRow("1.")]   // trailing decimal point is invalid
    [DataRow("1.px")] // invalid dimension
    public void Rejects_invalid_number(string input)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("line-height", "0", null);
        rule.Style.SetProperty("line-height", input, null);
        // The invalid value must be ignored, leaving the fallback.
        rule.Style.GetPropertyValue("line-height").Should().Be("0");
    }

    [TestMethod]
    public void SetProperty_then_getPropertyValue_round_trips()
    {
        var rule = FirstRule("div { color: red; }");
        rule.Style.GetPropertyValue("color").Should().Be("red");
        rule.Style.SetProperty("color", "blue", null);
        rule.Style.GetPropertyValue("color").Should().Be("blue");
    }

    [TestMethod]
    public void RemoveProperty_clears_value()
    {
        var rule = FirstRule("div { color: red; }");
        rule.Style.RemoveProperty("color").Should().Be("red");
        rule.Style.GetPropertyValue("color").Should().Be("");
    }

    [TestMethod]
    public void Important_priority_round_trips()
    {
        var rule = FirstRule("div { color: red !important; }");
        rule.Style.GetPropertyPriority("color").Should().Be("important");
        rule.Style.SetProperty("color", "blue", "important");
        rule.Style.GetPropertyPriority("color").Should().Be("important");
        rule.Style.CssText.Should().Contain("!important");
    }

    [TestMethod]
    public void CssText_serializes_declarations()
    {
        var rule = FirstRule("div { color: red; width: 10px; }");
        rule.Style.CssText.Should().Be("color: red; width: 10px;");
    }
}
