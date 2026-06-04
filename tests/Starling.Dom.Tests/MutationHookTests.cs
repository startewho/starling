using AwesomeAssertions;

namespace Starling.Dom.Tests;

/// <summary>
/// Tests for the internal Document mutation hooks (AttributeMutated /
/// ChildListMutated) that MutationObserverBinding subscribes to. These exercise
/// two correctness details the hooks must get right:
/// <list type="bullet">
/// <item>childList records capture sibling positions at insertion time, even
/// when a re-entrant host hook (e.g. an injected &lt;script&gt;) mutates the tree
/// during connection.</item>
/// <item>attribute records carry the pre-change value on every mutation path,
/// including removals and attribute-node replacement (not just setAttribute).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class MutationHookTests
{
    // --- childList ordering (record fires before re-entrant NodeConnected) ----

    [TestMethod]
    public void ChildList_record_captures_siblings_before_reentrant_connect()
    {
        var doc = new Document();
        var body = doc.CreateElement("body");
        doc.AppendChild(body);
        var a = doc.CreateElement("a");
        body.AppendChild(a);

        var records = new List<(Node Added, Node? Prev, Node? Next)>();
        doc.ChildListMutated = (_, added, _, prev, next) =>
        {
            if (added is not null) records.Add((added, prev, next));
        };

        // Simulate the engine's NodeConnected hook running an injected <script>
        // that re-enters and mutates the tree the moment `b` is connected.
        var b = doc.CreateElement("b");
        var reentered = false;
        doc.NodeConnected = node =>
        {
            if (reentered || !ReferenceEquals(node, b)) return;
            reentered = true;
            body.AppendChild(doc.CreateElement("c")); // changes b.NextSibling: null -> <c>
        };

        body.AppendChild(b); // tree becomes <a><b>, then the hook appends <c>

        // The record for <b> must reflect its siblings at insertion (prev=<a>,
        // next=null) — NOT the post-re-entrancy state where next=<c>.
        var bRecord = records.Single(r => ReferenceEquals(r.Added, b));
        bRecord.Prev.Should().BeSameAs(a);
        bRecord.Next.Should().BeNull();
    }

    // --- attribute oldValue on every mutation path ----------------------------

    [TestMethod]
    public void RemoveAttribute_reports_old_value()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("data-x", "hello");

        var captured = "UNSET";
        doc.AttributeMutated = (_, _, old) => captured = old ?? "<null>";
        el.RemoveAttribute("data-x");

        captured.Should().Be("hello");
    }

    [TestMethod]
    public void RemoveAttributeNS_reports_old_value()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttributeNS("urn:x", "p:foo", "v1");

        var captured = "UNSET";
        doc.AttributeMutated = (_, _, old) => captured = old ?? "<null>";
        el.RemoveAttributeNS("urn:x", "foo");

        captured.Should().Be("v1");
    }

    [TestMethod]
    public void SetNamedItem_replacement_reports_old_value()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttribute("data-x", "first");

        var captured = "UNSET";
        doc.AttributeMutated = (_, _, old) => captured = old ?? "<null>";
        el.Attributes.SetNamedItem(new AttrNode("data-x", "second"));

        captured.Should().Be("first");
    }

    [TestMethod]
    public void SetNamedItemNS_replacement_reports_old_value()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        el.SetAttributeNS("urn:x", "p:foo", "v1");

        var captured = "UNSET";
        doc.AttributeMutated = (_, _, old) => captured = old ?? "<null>";
        el.Attributes.SetNamedItemNS(AttrNode.CreateNamespaced("p:foo", "urn:x", "v2"));

        captured.Should().Be("v1");
    }

    [TestMethod]
    public void Newly_added_attribute_reports_null_old_value()
    {
        var doc = new Document();
        var el = doc.CreateElement("div");

        var captured = "UNSET";
        doc.AttributeMutated = (_, _, old) => captured = old ?? "<null>";
        el.SetAttribute("data-x", "fresh");

        captured.Should().Be("<null>");
    }
}
