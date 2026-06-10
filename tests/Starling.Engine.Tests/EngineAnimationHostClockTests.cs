using AwesomeAssertions;
using Starling.Bindings;
using Starling.Css.Animations;
using Starling.Dom;

namespace Starling.Engine.Tests;

/// <summary>
/// The live Engine constructs <see cref="EngineAnimationHost"/> without a
/// clock. The host must then follow the animation engine's own timeline —
/// a zero clock would pin every <c>element.animate()</c> start at t=0 while
/// sampling runs on the GUI stopwatch, so an animation issued at page time T
/// would be born with its playback head already at T and "finish" instantly.
/// </summary>
[TestClass]
public sealed class EngineAnimationHostClockTests
{
    [TestMethod]
    public void Animate_starts_on_the_engine_timeline_not_at_zero()
    {
        var engine = new AnimationEngine();
        engine.Tick(5000);

        var host = new EngineAnimationHost(engine);
        host.TimelineNow.Should().Be(5000, "the default clock is the engine timeline");

        var id = host.Animate(
            new Element("div"),
            new[]
            {
                new AnimationKeyframeSpec(0, new[] { new KeyValuePair<string, string>("opacity", "0") }),
                new AnimationKeyframeSpec(1, new[] { new KeyValuePair<string, string>("opacity", "1") }),
            },
            new AnimationEffectTimingSpec(
                DurationMs: 1000, DelayMs: 0, Iterations: 1,
                Direction: "normal", Fill: "none", Easing: "linear"));

        host.CurrentTime(id).Should().Be(0, "a freshly issued animation starts now, not at timeline zero");

        engine.Tick(5400);
        host.CurrentTime(id).Should().Be(400);
    }
}
