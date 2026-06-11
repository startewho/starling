using AwesomeAssertions;
using Starling.Gui.Controls;
using Xunit;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// Promotion hysteresis at the transition end (issue #82 item 4). When an
/// element's last animation/transition sample passes, the compositor keeps it
/// promoted for a few more rendered frames instead of demoting it on the very
/// next frame — so the base layer's slice is not re-keyed (and its tiles not
/// re-rastered) at the exact moment a transition settles. These tests pin the
/// countdown-window semantics <c>WebviewPanel</c> wires into
/// <c>IsElementAnimatingLayerRoot</c>.
/// </summary>
public sealed class PromotionHysteresisTests
{
    [Fact]
    public void Member_stays_promoted_for_the_window_after_its_last_note()
    {
        var set = new FrameCountdownSet<string>(frames: 3);
        set.Note("card");

        // The transition ended: no more notes, just per-frame decay.
        set.Decay();
        set.Contains("card").Should().BeTrue("one frame after the last sample the element is still promoted");
        set.Decay();
        set.Contains("card").Should().BeTrue("two frames after the last sample the element is still promoted");
        set.Decay();
        set.Contains("card").Should().BeFalse("the hysteresis window has elapsed — the element demotes");
    }

    [Fact]
    public void Renoting_mid_window_resets_the_countdown()
    {
        var set = new FrameCountdownSet<string>(frames: 3);
        set.Note("card");
        set.Decay();
        set.Decay();

        // A new transition starts on the same element before the window
        // elapsed — promotion must not flap.
        set.Note("card");
        set.Decay();
        set.Decay();
        set.Contains("card").Should().BeTrue("the re-note restarted the full window");
        set.Decay();
        set.Contains("card").Should().BeFalse();
    }

    [Fact]
    public void Members_decay_independently()
    {
        var set = new FrameCountdownSet<string>(frames: 2);
        set.Note("a");
        set.Decay();
        set.Note("b");
        set.Decay();

        set.Contains("a").Should().BeFalse("a's window elapsed");
        set.Contains("b").Should().BeTrue("b was noted a frame later");
        set.Count.Should().Be(1);

        set.Decay();
        set.Count.Should().Be(0, "every member eventually decays — nothing stays promoted forever");
    }

    [Fact]
    public void Clear_empties_the_window()
    {
        var set = new FrameCountdownSet<string>(frames: 3);
        set.Note("a");
        set.Note("b");
        set.Clear();
        set.Count.Should().Be(0);
        set.Contains("a").Should().BeFalse();
    }
}
