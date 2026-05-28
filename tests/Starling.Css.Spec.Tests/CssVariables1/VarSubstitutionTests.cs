using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssVariables1;

/// <summary>
/// <see href="https://www.w3.org/TR/css-variables-1/#using-variables">CSS Variables L1 §3</see>:
/// <c>var()</c> substitution.
/// </summary>
[TestClass]
[Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/", section: "3")]
public sealed class VarSubstitutionTests
{
    private static ComputedStyle Compute(string css)
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        doc.AppendChild(el);
        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return engine.Compute(el);
    }

    [SpecFact]
    public void Var_with_fallback_uses_fallback_when_property_undefined()
    {
        // .x { color: var(--missing, red); }   →   computed color = red
        var style = Compute("div { color: var(--missing, red); }");
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(255, 0, 0));
    }

    [SpecFact]
    public void Var_fallback_may_itself_contain_var()
    {
        // .x { color: var(--missing, var(--also-missing, blue)); } → blue
        var style = Compute("div { color: var(--missing, var(--also-missing, blue)); }");
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 0, 255));
    }
}
