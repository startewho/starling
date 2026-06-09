using AwesomeAssertions;

namespace Starling.Dom.Tests;

// DOM tree-traversal members on Node: parentElement, isConnected, hasChildNodes,
// and the ParentNode / NonDocumentTypeChildNode element accessors.
[TestClass]
public sealed class NodeTraversalTests
{
    // <root><t>a</t><a/><!--c--><b/><t>z</t></root>, all attached to a document.
    private static (Document doc, Element root, Element a, Element b) BuildTree()
    {
        var doc = new Document();
        var root = doc.CreateElement("root");
        doc.AppendChild(root);

        var a = doc.CreateElement("a");
        var b = doc.CreateElement("b");
        root.AppendChild(doc.CreateTextNode("a"));
        root.AppendChild(a);
        root.AppendChild(doc.CreateComment("c"));
        root.AppendChild(b);
        root.AppendChild(doc.CreateTextNode("z"));
        return (doc, root, a, b);
    }

    [TestMethod]
    public void ParentElement_is_the_element_parent_or_null()
    {
        var (doc, root, a, _) = BuildTree();
        a.ParentElement.Should().BeSameAs(root);
        // root's parent is the Document, which is not an Element.
        root.ParentElement.Should().BeNull();
    }

    [TestMethod]
    public void IsConnected_reflects_document_attachment()
    {
        var (_, root, a, _) = BuildTree();
        root.IsConnected.Should().BeTrue();
        a.IsConnected.Should().BeTrue();

        var detached = new Document().CreateElement("div");
        detached.IsConnected.Should().BeFalse();
    }

    [TestMethod]
    public void HasChildNodes_is_true_only_with_children()
    {
        var (_, root, a, _) = BuildTree();
        root.HasChildNodes().Should().BeTrue();
        a.HasChildNodes().Should().BeFalse();
    }

    [TestMethod]
    public void First_and_last_element_child_skip_non_elements()
    {
        var (_, root, a, b) = BuildTree();
        root.FirstElementChild.Should().BeSameAs(a);
        root.LastElementChild.Should().BeSameAs(b);

        a.FirstElementChild.Should().BeNull();
    }

    [TestMethod]
    public void ChildElementCount_counts_only_elements()
    {
        var (_, root, _, _) = BuildTree();
        // Five children total (two text, one comment, two elements).
        root.ChildElementCount.Should().Be(2);
    }

    [TestMethod]
    public void Next_and_previous_element_sibling_skip_non_elements()
    {
        var (_, _, a, b) = BuildTree();
        // a and b are separated by a comment node.
        a.NextElementSibling.Should().BeSameAs(b);
        b.PreviousElementSibling.Should().BeSameAs(a);

        b.NextElementSibling.Should().BeNull();
        a.PreviousElementSibling.Should().BeNull();
    }
}
