using AwesomeAssertions;

namespace Starling.Dom.Tests;

// DOM §4.10 CharacterData manipulation algorithms: length, substringData,
// appendData, insertData, deleteData, replaceData.
// https://dom.spec.whatwg.org/#interface-characterdata
[TestClass]
public sealed class CharacterDataTests
{
    private static Text NewText(string data = "hello world") => new(data);

    [TestMethod]
    public void Length_counts_utf16_code_units()
    {
        NewText("hello").Length.Should().Be(5);
        NewText("").Length.Should().Be(0);
        // A surrogate pair (U+1F600) is two UTF-16 code units, matching the spec.
        NewText("\U0001F600").Length.Should().Be(2);
    }

    [TestMethod]
    public void SubstringData_returns_the_requested_range()
    {
        var t = NewText("hello world");
        t.SubstringData(0, 5).Should().Be("hello");
        t.SubstringData(6, 5).Should().Be("world");
        t.SubstringData(6, 0).Should().Be("");
    }

    [TestMethod]
    public void SubstringData_clamps_count_to_the_end()
    {
        var t = NewText("hello");
        t.SubstringData(2, 100).Should().Be("llo");
        t.SubstringData(5, 100).Should().Be("");
    }

    [TestMethod]
    public void SubstringData_offset_past_end_throws_index_size_error()
    {
        var t = NewText("hello");
        var act = () => t.SubstringData(6, 1);
        act.Should().Throw<DomException>().Which.Name.Should().Be("IndexSizeError");
    }

    [TestMethod]
    public void AppendData_appends_to_the_end()
    {
        var t = NewText("hello");
        t.AppendData(" world");
        t.Data.Should().Be("hello world");
    }

    [TestMethod]
    public void InsertData_inserts_at_offset()
    {
        var t = NewText("hello");
        t.InsertData(0, ">>");
        t.Data.Should().Be(">>hello");
        t.InsertData((uint)t.Length, "<<");
        t.Data.Should().Be(">>hello<<");
    }

    [TestMethod]
    public void InsertData_offset_past_end_throws_index_size_error()
    {
        var t = NewText("hi");
        var act = () => t.InsertData(3, "x");
        act.Should().Throw<DomException>().Which.Name.Should().Be("IndexSizeError");
    }

    [TestMethod]
    public void DeleteData_removes_the_range()
    {
        var t = NewText("hello world");
        t.DeleteData(5, 6);
        t.Data.Should().Be("hello");
    }

    [TestMethod]
    public void DeleteData_clamps_count_to_the_end()
    {
        var t = NewText("hello");
        t.DeleteData(2, 100);
        t.Data.Should().Be("he");
    }

    [TestMethod]
    public void ReplaceData_replaces_the_range_in_place()
    {
        var t = NewText("hello world");
        t.ReplaceData(0, 5, "goodbye");
        t.Data.Should().Be("goodbye world");
    }

    [TestMethod]
    public void ReplaceData_with_zero_count_is_an_insert()
    {
        var t = NewText("ac");
        t.ReplaceData(1, 0, "b");
        t.Data.Should().Be("abc");
    }

    [TestMethod]
    public void ReplaceData_offset_past_end_throws_index_size_error()
    {
        var t = NewText("hi");
        var act = () => t.ReplaceData(3, 0, "x");
        act.Should().Throw<DomException>().Which.Name.Should().Be("IndexSizeError");
    }

    [TestMethod]
    public void WholeText_concatenates_contiguous_text_siblings()
    {
        var doc = new Document();
        var root = doc.CreateElement("root");
        var t1 = doc.CreateTextNode("foo");
        var t2 = doc.CreateTextNode("bar");
        var t3 = doc.CreateTextNode("baz");
        root.AppendChild(t1);
        root.AppendChild(t2);
        root.AppendChild(doc.CreateElement("br"));   // breaks the run
        root.AppendChild(t3);

        t1.WholeText.Should().Be("foobar");
        t2.WholeText.Should().Be("foobar");
        t3.WholeText.Should().Be("baz");
    }

    [TestMethod]
    public void SplitText_moves_the_tail_into_a_new_next_sibling()
    {
        var doc = new Document();
        var root = doc.CreateElement("root");
        var t = doc.CreateTextNode("hello world");
        root.AppendChild(t);

        var tail = t.SplitText(5);
        t.Data.Should().Be("hello");
        tail.Data.Should().Be(" world");
        t.NextSibling.Should().BeSameAs(tail);
        tail.ParentNode.Should().BeSameAs(root);
    }

    [TestMethod]
    public void SplitText_offset_past_end_throws_index_size_error()
    {
        var t = NewText("hi");
        var act = () => t.SplitText(3);
        act.Should().Throw<DomException>().Which.Name.Should().Be("IndexSizeError");
    }

    [TestMethod]
    public void Mutating_data_on_an_attached_node_updates_the_text()
    {
        // Goes through the Data setter while OwnerDocument is set, exercising the
        // mutation-notification path. The change is observable as NodeValue.
        var doc = new Document();
        var t = doc.CreateTextNode("hello");
        doc.AppendChild(t);

        t.AppendData(" world");
        t.Data.Should().Be("hello world");
        t.NodeValue.Should().Be("hello world");
    }
}
