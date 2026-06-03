// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Html;

namespace Starling.Engine.Tests;

/// <summary>
/// Phase 4 of the AngleSharp backend plan: the runtime selector. Mirrors the
/// JS-engine selector — default Starling, opt-in AngleSharp, loud on a typo —
/// plus a seam-swap smoke proving the facade routes through
/// <see cref="HtmlParsing.Backend"/> and the AngleSharp backend yields a working
/// Starling DOM.
/// </summary>
[TestClass]
public sealed class HtmlBackendSelectorTests
{
    [TestMethod]
    public void Parse_defaults_to_starling_for_blank_values()
    {
        HtmlBackendSelector.Parse(null).Should().Be(HtmlBackendKind.Starling);
        HtmlBackendSelector.Parse("").Should().Be(HtmlBackendKind.Starling);
        HtmlBackendSelector.Parse("   ").Should().Be(HtmlBackendKind.Starling);
        HtmlBackendSelector.Parse("starling").Should().Be(HtmlBackendKind.Starling);
    }

    [TestMethod]
    public void Parse_selects_anglesharp_case_insensitively()
    {
        HtmlBackendSelector.Parse("anglesharp").Should().Be(HtmlBackendKind.AngleSharp);
        HtmlBackendSelector.Parse("AngleSharp").Should().Be(HtmlBackendKind.AngleSharp);
        HtmlBackendSelector.Parse("  ANGLESHARP ").Should().Be(HtmlBackendKind.AngleSharp);
    }

    [TestMethod]
    public void Parse_rejects_an_unknown_value_loudly()
    {
        var act = () => HtmlBackendSelector.Parse("webkit");
        act.Should().Throw<InvalidOperationException>().WithMessage("*STARLING_HTML_PARSER*");
    }

    [TestMethod]
    public void AngleSharp_backend_swaps_in_through_the_seam()
    {
        var previous = HtmlParsing.Backend;
        try
        {
            HtmlParsing.Backend = new Starling.Html.AngleSharp.AngleSharpHtmlBackend();
            // The HtmlParser facade reads the holder, so a swap takes effect here.
            var doc = HtmlParser.Parse("<body><p>hi</p></body>");
            doc.DescendantElements().Single(e => e.LocalName == "p").TextContent.Should().Be("hi");
            HtmlParsing.Backend.Name.Should().Be("anglesharp");
        }
        finally
        {
            HtmlParsing.Backend = previous;
        }
    }
}
