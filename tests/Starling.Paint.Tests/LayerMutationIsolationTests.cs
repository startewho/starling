using AwesomeAssertions;
using Starling.Dom;
using Starling.Html;

namespace Starling.Paint.Tests;

/// <summary>
/// The Document-side recently-mutated tracker and its hysteresis decay, which
/// drives promoting a script-mutated subtree to its own layer (LTF-06).
/// </summary>
[TestClass]
public sealed class LayerMutationIsolationTests
{
    [TestMethod]
    public void Document_tracks_recently_mutated_element_and_decays_after_window()
    {
        var doc = HtmlParser.Parse("<body><span id=s>a</span></body>");
        doc.RecordLayoutMutations = true;
        var status = doc.GetElementById("s")!;
        var text = FirstText(status)!;

        text.Data = "b"; // a connected text mutation marks the parent element
        doc.WasRecentlyMutated(status).Should().BeTrue("a fresh text mutation promotes the element");

        // Hysteresis window is a few frames (RecentMutationFrames = 3).
        doc.DecayRecentMutations();
        doc.WasRecentlyMutated(status).Should().BeTrue("still inside the hysteresis window");
        doc.DecayRecentMutations();
        doc.WasRecentlyMutated(status).Should().BeTrue();
        doc.DecayRecentMutations();
        doc.WasRecentlyMutated(status).Should().BeFalse("the promotion window has elapsed");
    }

    private static Text? FirstText(Element element)
    {
        for (var child = element.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Text t)
            {
                return t;
            }
        }
        return null;
    }
}
