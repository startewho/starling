using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;

namespace Starling.Css.Spec.Tests.CssPropertiesValues1;

/// <summary>
/// Engine-integration conformance for <c>@property</c> registration
/// (<see href="https://www.w3.org/TR/css-properties-values-api-1/">CSS Properties and Values API 1</see> §2):
/// the style engine collects registered custom properties from attached
/// stylesheets and exposes their parsed descriptors.
/// </summary>
[TestClass]
[Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/", section: "2")]
public sealed class RegisteredPropertyEngineTests
{
    private static StyleEngine EngineWith(string css)
    {
        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return engine;
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#registering-custom-properties", section: "2")]
    [SpecFact]
    public void Engine_collects_registered_property_from_attached_sheet()
    {
        var engine = EngineWith("@property --gap { syntax: \"<length>\"; inherits: true; initial-value: 8px; }");
        engine.RegisteredProperties.Should().ContainKey("--gap");
        var p = engine.RegisteredProperties["--gap"];
        p.Syntax.Should().Be("<length>");
        p.Inherits.Should().BeTrue();
        p.InitialValue.Should().Be("8px");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#registering-custom-properties", section: "2")]
    [SpecFact]
    public void Later_registration_of_same_name_wins()
    {
        var engine = EngineWith(
            "@property --c { syntax: \"<color>\"; inherits: false; initial-value: red; }" +
            "@property --c { syntax: \"<color>\"; inherits: false; initial-value: blue; }");
        engine.RegisteredProperties["--c"].InitialValue.Should().Be("blue");
    }

    [Spec("css-properties-values-api-1", "https://www.w3.org/TR/css-properties-values-api-1/#registering-custom-properties", section: "2")]
    [SpecFact]
    public void Invalid_registration_is_not_collected()
    {
        // Missing initial-value for a non-universal syntax → invalid → dropped.
        var engine = EngineWith("@property --x { syntax: \"<length>\"; inherits: false; }");
        engine.RegisteredProperties.Should().NotContainKey("--x");
    }
}
