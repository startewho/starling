using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssVariables1;

/// <summary>
/// <see href="https://www.w3.org/TR/css-variables-1/#cycles">CSS Variables L1 §3.3</see>:
/// cycle detection in <c>var()</c> chains.
/// </summary>
[TestClass]
[Spec("css-variables-1", "https://www.w3.org/TR/css-variables-1/", section: "3.3")]
public sealed class VarCycleDetectionTests
{
    // Builds parent > child, applies the author sheet, and computes the child.
    // The parent fixes `color: green` so an invalid-at-computed-value-time
    // `color` on the child resolves to the inherited value (green) — proving
    // the cyclic var() did NOT supply a value.
    private static ComputedStyle ComputeChild(string childCss)
    {
        var doc = new Document();
        var parent = doc.CreateElement("section");
        var child = doc.CreateElement("div");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "section { color: green; } div { " + childCss + " }"));
        return engine.Compute(child);
    }

    [SpecFact]
    public void Direct_self_reference_makes_property_invalid()
    {
        // --a: var(--a);   →   --a computes to the guaranteed-invalid value;
        //                       any property using var(--a) falls back / uses unset.
        var style = ComputeChild("--a: var(--a); color: var(--a);");

        // §3.3: the cyclic custom property is guaranteed-invalid → serializes empty.
        style.GetPropertyValue("--a").Should().BeEmpty();
        // §3.2: `color: var(--a)` is invalid-at-computed-value-time → behaves as
        // `unset`; color inherits, so it takes the parent's green.
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0));
    }

    [SpecFact]
    public void Indirect_cycle_through_chain_is_detected()
    {
        // --a: var(--b);  --b: var(--c);  --c: var(--a);   →   all three invalid.
        var style = ComputeChild(
            "--a: var(--b); --b: var(--c); --c: var(--a); color: var(--a);");

        style.GetPropertyValue("--a").Should().BeEmpty();
        style.GetPropertyValue("--b").Should().BeEmpty();
        style.GetPropertyValue("--c").Should().BeEmpty();
        style.GetColor(PropertyId.Color).Should().Be(new Starling.Css.Values.CssColor(0, 128, 0));
    }
}
