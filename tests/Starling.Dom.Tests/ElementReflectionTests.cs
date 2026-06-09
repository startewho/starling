using AwesomeAssertions;

namespace Starling.Dom.Tests;

// Members added so the bindings generator can emit them: Element.className,
// Document.doctype, DocumentFragment.getElementById.
[TestClass]
public sealed class ElementReflectionTests
{
    [TestMethod]
    public void ClassName_reflects_the_class_attribute_both_ways()
    {
        var doc = new Document();
        var e = doc.CreateElement("div");

        e.ClassName.Should().Be("");
        e.ClassName = "a b";
        e.GetAttribute("class").Should().Be("a b");

        e.SetAttribute("class", "c");
        e.ClassName.Should().Be("c");
    }

    [TestMethod]
    public void Document_doctype_is_the_doctype_child_or_null()
    {
        var doc = new Document();
        doc.Doctype.Should().BeNull();

        var dt = doc.CreateDocumentType("html", "", "");
        doc.AppendChild(dt);
        doc.AppendChild(doc.CreateElement("html"));

        doc.Doctype.Should().BeSameAs(dt);
    }

    [TestMethod]
    public void DocumentFragment_getElementById_finds_descendants()
    {
        var doc = new Document();
        var frag = doc.CreateDocumentFragment();
        var outer = doc.CreateElement("div");
        var inner = doc.CreateElement("span");
        inner.Id = "target";
        outer.AppendChild(inner);
        frag.AppendChild(outer);

        frag.GetElementById("target").Should().BeSameAs(inner);
        frag.GetElementById("missing").Should().BeNull();
    }
}
