using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssVariables1;

/// <summary>
/// <see href="https://www.w3.org/TR/css-variables-1/#defining-variables">CSS Variables L1 §2</see>:
/// custom-property declaration syntax (<c>--*</c>).
/// </summary>
[TestClass]
[Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/", section: "2")]
public sealed class CustomPropertyParsingTests
{
    private static CssDeclaration Declaration(string css)
    {
        var sheet = CssParser.ParseStyleSheet("x { " + css + " }");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.Single();
    }

    private static ComputedStyle ComputeWith(string css)
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        doc.AppendChild(el);
        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet("div { " + css + " }"));
        return engine.Compute(el);
    }

    [SpecFact]
    public void Declaration_with_double_dash_prefix_is_a_custom_property()
    {
        // GIVEN  --brand: #036;
        // THEN   the declaration is preserved as a custom property whose value is
        //        the token stream "#036" (whitespace-trimmed per §2).
        var decl = Declaration("--brand: #036;");
        decl.Name.Should().Be("--brand");

        // §2 / CSSOM §6.7.4: the stored token stream serializes back to "#036".
        ComputeWith("--brand: #036;").GetPropertyValue("--brand").Should().Be("#036");
    }

    [SpecFact]
    public void Custom_property_preserves_arbitrary_token_stream()
    {
        // --x: 1px solid red !important;  →  value is "1px solid red", important = true.
        var decl = Declaration("--x: 1px solid red !important;");
        decl.Name.Should().Be("--x");
        decl.Important.Should().BeTrue();

        ComputeWith("--x: 1px solid red !important;").GetPropertyValue("--x")
            .Should().Be("1px solid red");
    }
}
