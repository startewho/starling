using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout.Box;
namespace Starling.Layout.Tests;

/// <summary>
/// Verifies the editable-input layout hooks: the live <c>Element.InputValue</c>
/// (typed text / scripted assignment) renders in place of the <c>value</c>
/// content attribute, and a focused empty field drops its placeholder so the
/// caret sits alone — both prerequisites for click-to-type.
/// </summary>
[TestClass]
public sealed class FocusedInputLayoutTests
{
    private static BlockBox Layout(Starling.Dom.Document doc, Size viewport)
        => new LayoutEngine(new StyleEngine()).LayoutDocument(doc, viewport);

    [TestMethod]
    public void Live_input_value_renders_over_the_value_attribute()
    {
        var doc = HtmlParser.Parse("<body><input type=\"text\" value=\"old\"></body>");
        var input = doc.GetElementsByTagName("input")[0];
        input.InputValue = "typed";

        var labels = Texts(FindBox(Layout(doc, new Size(800, 600)), "input")!);
        labels.Should().Contain("typed");
        labels.Should().NotContain("old");
    }

    [TestMethod]
    public void Focused_empty_input_drops_its_placeholder()
    {
        var doc = HtmlParser.Parse("<body><input type=\"text\" placeholder=\"search\"></body>");
        doc.FocusedElement = doc.GetElementsByTagName("input")[0];

        Texts(FindBox(Layout(doc, new Size(800, 600)), "input")!)
            .Should().NotContain("search");
    }

    [TestMethod]
    public void Unfocused_empty_input_keeps_its_placeholder()
    {
        var doc = HtmlParser.Parse("<body><input type=\"text\" placeholder=\"search\"></body>");

        Texts(FindBox(Layout(doc, new Size(800, 600)), "input")!)
            .Should().Contain("search");
    }

    // ---------------------------------------------------------------- helpers

    private static List<string> Texts(Box.Box box) => FlattenTextBoxes(box).Select(tb => tb.Text).ToList();

    private static Box.Box? FindBox(Box.Box root, string localName)
    {
        if (root.Element?.LocalName == localName) return root;
        foreach (var child in root.Children)
        {
            var hit = FindBox(child, localName);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static IEnumerable<TextBox> FlattenTextBoxes(Box.Box box)
    {
        if (box is TextBox tb) { yield return tb; yield break; }
        foreach (var child in box.Children)
            foreach (var inner in FlattenTextBoxes(child))
                yield return inner;
    }
}
