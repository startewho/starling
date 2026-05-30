using AwesomeAssertions;
using Starling.Gui.Core.Text;

namespace Starling.Gui.Tests;

/// <summary>
/// Unit coverage for <see cref="ImeComposition"/> — the input-method composition
/// model that a native text-input driver feeds. Verifies commit-style typing and
/// the marked-text (preedit) lifecycle a screen-composed character goes through.
/// </summary>
[TestClass]
public sealed class ImeCompositionTests
{
    [TestMethod]
    public void Insert_appends_and_advances_caret()
    {
        var ime = new ImeComposition();
        ime.Insert("ab");
        ime.Insert("c");
        ime.CommittedText.Should().Be("abc");
        ime.Caret.Should().Be(3);
        ime.IsComposing.Should().BeFalse();
    }

    [TestMethod]
    public void Insert_at_caret_in_the_middle()
    {
        var ime = new ImeComposition();
        ime.Reset("ad", 1);
        ime.Insert("bc");
        ime.CommittedText.Should().Be("abcd");
        ime.Caret.Should().Be(3);
    }

    [TestMethod]
    public void Backspace_deletes_before_caret()
    {
        var ime = new ImeComposition();
        ime.Reset("abc", 3);
        ime.Backspace();
        ime.CommittedText.Should().Be("ab");
        ime.Caret.Should().Be(2);
    }

    [TestMethod]
    public void Marked_text_shows_in_display_but_not_committed()
    {
        var ime = new ImeComposition();
        ime.Reset("hi ", 3);
        ime.SetMarkedText("ni"); // composing pinyin
        ime.IsComposing.Should().BeTrue();
        ime.CommittedText.Should().Be("hi ");
        ime.DisplayText.Should().Be("hi ni");
        ime.DisplayCaret.Should().Be(5);
    }

    [TestMethod]
    public void Replacing_marked_text_updates_display()
    {
        var ime = new ImeComposition();
        ime.Reset("", 0);
        ime.SetMarkedText("ni");
        ime.SetMarkedText("nihao");
        ime.DisplayText.Should().Be("nihao");
        ime.CommittedText.Should().Be("");
    }

    [TestMethod]
    public void Commit_preedit_moves_marked_text_into_committed()
    {
        var ime = new ImeComposition();
        ime.Reset("", 0);
        ime.SetMarkedText("ni");
        ime.CommitPreedit();
        ime.CommittedText.Should().Be("ni");
        ime.IsComposing.Should().BeFalse();
        ime.Caret.Should().Be(2);
    }

    [TestMethod]
    public void Insert_replaces_active_preedit()
    {
        // The driver shows preedit "ni", then commits the chosen character "你".
        var ime = new ImeComposition();
        ime.Reset("", 0);
        ime.SetMarkedText("ni");
        ime.Insert("你"); // 你
        ime.CommittedText.Should().Be("你");
        ime.IsComposing.Should().BeFalse();
    }

    [TestMethod]
    public void Backspace_while_composing_shortens_preedit()
    {
        var ime = new ImeComposition();
        ime.Reset("x", 1);
        ime.SetMarkedText("abc");
        ime.Backspace();
        ime.Preedit.Should().Be("ab");
        ime.CommittedText.Should().Be("x");
    }

    [TestMethod]
    public void Cancel_preedit_drops_it()
    {
        var ime = new ImeComposition();
        ime.Reset("x", 1);
        ime.SetMarkedText("abc");
        ime.CancelPreedit();
        ime.IsComposing.Should().BeFalse();
        ime.DisplayText.Should().Be("x");
    }
}
