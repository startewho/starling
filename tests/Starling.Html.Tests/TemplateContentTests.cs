// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Dom;

namespace Starling.Html.Tests;

/// <summary>
/// HTML §4.12.3 / DOM §4.13.3 — <c>&lt;template&gt;</c> content fragment.
/// Both parser backends must agree that template children land in the content
/// fragment, not the normal tree, so these pin the Starling parser's behavior.
/// </summary>
[TestClass]
public sealed class TemplateContentTests
{
    private static HtmlTemplateElement FirstTemplate(Document doc)
        => doc.DescendantElements().OfType<HtmlTemplateElement>().First();

    [TestMethod]
    public void CreateElement_makes_a_specialized_template_element()
    {
        var doc = new Document();
        doc.CreateElement("template").Should().BeOfType<HtmlTemplateElement>();
        doc.CreateElement("TEMPLATE").Should().BeOfType<HtmlTemplateElement>();
        doc.CreateElementNS(Element.HtmlNamespace, "template").Should().BeOfType<HtmlTemplateElement>();
        doc.CreateElement("div").Should().NotBeOfType<HtmlTemplateElement>();
    }

    [TestMethod]
    public void Template_children_go_into_content_not_the_tree()
    {
        var doc = HtmlParser.Parse("<body><template><div>x</div></template></body>");
        var tpl = FirstTemplate(doc);

        // No element children in the normal tree.
        tpl.ChildNodes.OfType<Element>().Should().BeEmpty();
        // The parsed <div> lives in the content fragment.
        var div = tpl.Content.FirstChild as Element;
        div!.LocalName.Should().Be("div");
        div.TextContent.Should().Be("x");
    }

    [TestMethod]
    public void Template_content_owner_is_a_separate_inert_document()
    {
        var doc = HtmlParser.Parse("<template><span></span></template>");
        var tpl = FirstTemplate(doc);

        // Spec: content belongs to a distinct "template contents owner" document
        // so its scripts never run and it never participates in layout.
        tpl.Content.OwnerDocument.Should().NotBeNull();
        tpl.Content.OwnerDocument.Should().NotBeSameAs(doc);
    }

    [TestMethod]
    public void Nested_templates_nest_their_content()
    {
        var doc = HtmlParser.Parse(
            "<template id=outer><template id=inner><b>deep</b></template></template>");
        var outer = doc.DescendantElements().OfType<HtmlTemplateElement>()
            .First(t => t.GetAttribute("id") == "outer");

        outer.ChildNodes.OfType<Element>().Should().BeEmpty();
        var inner = outer.Content.ChildNodes.OfType<HtmlTemplateElement>().Single();
        inner.GetAttribute("id").Should().Be("inner");
        inner.Content.TextContent.Should().Be("deep");
    }

    [TestMethod]
    public void Markup_after_a_template_returns_to_normal_flow()
    {
        var doc = HtmlParser.Parse("<body><template><i>in</i></template><p>after</p></body>");
        var tpl = FirstTemplate(doc);

        tpl.Content.TextContent.Should().Be("in");
        // The <p> is a normal sibling in the document tree, not template content.
        var p = doc.DescendantElements().Single(e => e.LocalName == "p");
        p.TextContent.Should().Be("after");
        p.ParentNode.Should().NotBeSameAs(tpl.Content);
    }

    [TestMethod]
    public void Fragment_parsing_into_a_template_context_uses_content_rules()
    {
        // innerHTML on a <template> parses the markup as template content.
        var doc = new Document();
        var tpl = (HtmlTemplateElement)doc.CreateElement("template");
        var fragment = HtmlParsing.Backend.ParseFragment("<tr><td>c</td></tr>", tpl, doc);

        fragment.TextContent.Should().Be("c");
    }
}
