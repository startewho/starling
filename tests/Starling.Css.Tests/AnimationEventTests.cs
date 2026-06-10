using AwesomeAssertions;
using Starling.Css.Animations;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Spec;

namespace Starling.Css.Tests;

/// <summary>
/// CSS Animations 1 §5 + CSS Transitions 2 §events: the engines queue
/// animation/transition DOM-event facts at lifecycle boundaries and the
/// <see cref="AnimationEventDispatcher"/> turns them into bubbling,
/// non-cancelable DOM events. All clocks injected — fully deterministic.
/// </summary>
[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/")]
[Spec("css-transitions-1", "https://www.w3.org/TR/css-transitions-1/")]

[TestClass]
public sealed class AnimationEventTests
{
    // ---- harness (mirrors TransitionEngineTests / AnimationEngineTests) ----

    private static Func<PropertyId, CssValue?> Props(
        CssValue? property = null,
        CssValue? duration = null,
        CssValue? delay = null,
        CssValue? timing = null)
    {
        return id => id switch
        {
            PropertyId.TransitionProperty => property ?? new CssKeyword("all"),
            PropertyId.TransitionDuration => duration ?? new CssTime(0.2, CssTimeUnit.Seconds),
            PropertyId.TransitionDelay => delay ?? new CssTime(0, CssTimeUnit.Seconds),
            PropertyId.TransitionTimingFunction => timing ?? new CssKeyword("linear"),
            _ => null,
        };
    }

    private static KeyframesRule SimpleFade()
        => new("fade", new[]
        {
            new Keyframe(0.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(0)) }),
            new Keyframe(1.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(1)) }),
        });

    private static AnimationDeclaration Decl(
        string name = "fade",
        double durationMs = 1000,
        double delayMs = 0,
        double iterationCount = 1)
        => new(name, durationMs, delayMs, TimingFunction.Linear,
            iterationCount, AnimationDirection.Normal, AnimationFillMode.None,
            AnimationPlayState.Running);

    private static List<AnimationEventRecord> Drain(TransitionEngine engine)
    {
        var list = new List<AnimationEventRecord>();
        engine.DrainPendingEvents(list);
        return list;
    }

    private static List<AnimationEventRecord> Drain(AnimationEngine engine)
    {
        var list = new List<AnimationEventRecord>();
        engine.DrainPendingEvents(list);
        return list;
    }

    // ---- transitions --------------------------------------------------------

    [TestMethod]
    public void Transition_fires_run_start_end_with_property_and_elapsed()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));

        // transitionrun at creation — the start of the (zero-length) delay.
        var events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.TransitionRun);
        events[0].Element.Should().BeSameAs(el);
        events[0].Name.Should().Be("opacity");
        events[0].ElapsedSeconds.Should().Be(0);

        // transitionstart on the first tick inside the active phase.
        engine.Tick(100);
        events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.TransitionStart);
        events[0].Name.Should().Be("opacity");
        events[0].ElapsedSeconds.Should().Be(0);

        // transitionend on completion: elapsedTime = duration, delay excluded.
        engine.Tick(200);
        events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.TransitionEnd);
        events[0].Name.Should().Be("opacity");
        events[0].ElapsedSeconds.Should().BeApproximately(0.2, 1e-9);
        engine.HasPendingEvents.Should().BeFalse();
    }

    [TestMethod]
    public void Transition_delay_defers_transitionstart_and_elapsed_excludes_delay()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(100, CssTimeUnit.Milliseconds),
            delay: new CssTime(100, CssTimeUnit.Milliseconds)));

        Drain(engine).Should().ContainSingle(e => e.Kind == AnimationEventKind.TransitionRun);

        engine.Tick(50); // still inside the delay — no transitionstart yet
        engine.HasPendingEvents.Should().BeFalse();

        engine.Tick(120); // active phase entered
        var events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.TransitionStart);
        events[0].ElapsedSeconds.Should().Be(0);

        engine.Tick(250);
        events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.TransitionEnd);
        events[0].ElapsedSeconds.Should().BeApproximately(0.1, 1e-9); // duration only
    }

    [TestMethod]
    public void Replaced_transition_fires_transitioncancel_then_run_for_replacement()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));
        Drain(engine); // discard the first run + start bookkeeping
        engine.Tick(100);
        Drain(engine);

        // Reverse to a new target mid-flight: the old transition cancels.
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));

        var events = Drain(engine);
        events.Should().HaveCount(2);
        events[0].Kind.Should().Be(AnimationEventKind.TransitionCancel);
        events[0].Name.Should().Be("opacity");
        // canceled 100ms into the active phase
        events[0].ElapsedSeconds.Should().BeApproximately(0.1, 1e-9);
        events[1].Kind.Should().Be(AnimationEventKind.TransitionRun);
    }

    [TestMethod]
    public void Forget_cancels_in_flight_transitions()
    {
        var engine = new TransitionEngine();
        var el = new Element("div");
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(0), Props());
        engine.OnComputedValueChanged(el, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));
        Drain(engine);
        engine.Tick(50);
        Drain(engine);

        engine.Forget(el);

        var events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.TransitionCancel);
        events[0].ElapsedSeconds.Should().BeApproximately(0.05, 1e-9);
    }

    // ---- animations ----------------------------------------------------------

    [TestMethod]
    public void Two_iteration_animation_fires_start_one_iteration_then_end()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, iterationCount: 2) });

        engine.Tick(0);
        var events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.AnimationStart);
        events[0].Element.Should().BeSameAs(el);
        events[0].Name.Should().Be("fade");
        events[0].ElapsedSeconds.Should().Be(0);

        engine.Tick(120); // crossed the first iteration boundary
        events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.AnimationIteration);
        events[0].ElapsedSeconds.Should().BeApproximately(0.1, 1e-9); // boundary time

        engine.Tick(250); // past the active interval (2 × 100ms)
        events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.AnimationEnd);
        events[0].ElapsedSeconds.Should().BeApproximately(0.2, 1e-9);

        engine.Tick(400); // settled — nothing refires
        engine.HasPendingEvents.Should().BeFalse();
    }

    [TestMethod]
    public void Tick_that_skips_past_the_end_fires_end_without_iteration()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, iterationCount: 2) });

        engine.Tick(0);
        Drain(engine);

        engine.Tick(500); // one giant frame: straight past the end
        var events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.AnimationEnd);
        events[0].ElapsedSeconds.Should().BeApproximately(0.2, 1e-9);

        engine.Tick(600); // and no late animationiteration afterwards
        engine.HasPendingEvents.Should().BeFalse();
    }

    [TestMethod]
    public void Animation_delay_defers_start_and_elapsed_excludes_delay()
    {
        var engine = new AnimationEngine();
        engine.RegisterKeyframes(SimpleFade());
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(durationMs: 100, delayMs: 200) });

        engine.Tick(100); // inside the delay phase
        engine.HasPendingEvents.Should().BeFalse();

        engine.Tick(210);
        var events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.AnimationStart);
        events[0].ElapsedSeconds.Should().Be(0);

        engine.Tick(320);
        events = Drain(engine);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(AnimationEventKind.AnimationEnd);
        events[0].ElapsedSeconds.Should().BeApproximately(0.1, 1e-9);
    }

    [TestMethod]
    public void Animation_without_registered_keyframes_fires_no_events()
    {
        var engine = new AnimationEngine();
        var el = new Element("div");
        engine.OnAnimationsCascaded(el, new[] { Decl(name: "ghost", durationMs: 100) });
        engine.Tick(50);
        engine.Tick(250);
        engine.HasPendingEvents.Should().BeFalse();
    }

    // ---- dispatcher ----------------------------------------------------------

    [TestMethod]
    public void Dispatched_events_bubble_to_an_ancestor_listener()
    {
        var animations = new AnimationEngine();
        var transitions = new TransitionEngine();
        var parent = new Element("section");
        var child = new Element("div");
        parent.AppendChild(child);

        var received = new List<Event>();
        parent.AddEventListener("transitionend", received.Add);
        parent.AddEventListener("animationend", received.Add);

        transitions.OnComputedValueChanged(child, PropertyId.Opacity, new CssNumber(0), Props());
        transitions.OnComputedValueChanged(child, PropertyId.Opacity, new CssNumber(1), Props(
            duration: new CssTime(200, CssTimeUnit.Milliseconds)));
        animations.RegisterKeyframes(SimpleFade());
        animations.OnAnimationsCascaded(child, new[] { Decl(durationMs: 100) });

        animations.Tick(250);
        transitions.Tick(250);
        var dispatched = AnimationEventDispatcher.DispatchPending(animations, transitions);

        // start+end for the animation, run+start+end for the transition.
        dispatched.Should().Be(5);
        received.Should().HaveCount(2);

        var animEnd = received.OfType<AnimationEvent>().Single();
        animEnd.Type.Should().Be("animationend");
        animEnd.AnimationName.Should().Be("fade");
        animEnd.ElapsedTime.Should().BeApproximately(0.1, 1e-9);
        animEnd.PseudoElement.Should().Be("");
        animEnd.Bubbles.Should().BeTrue();
        animEnd.Cancelable.Should().BeFalse();
        animEnd.IsTrusted.Should().BeTrue();
        animEnd.Target.Should().BeSameAs(child);

        var transEnd = received.OfType<TransitionEvent>().Single();
        transEnd.Type.Should().Be("transitionend");
        transEnd.PropertyName.Should().Be("opacity");
        transEnd.ElapsedTime.Should().BeApproximately(0.2, 1e-9);
        transEnd.PseudoElement.Should().Be("");
        transEnd.Bubbles.Should().BeTrue();
        transEnd.Target.Should().BeSameAs(child);
    }

    [TestMethod]
    public void Dispatcher_is_a_no_op_when_nothing_is_pending()
    {
        AnimationEventDispatcher.DispatchPending(new AnimationEngine(), new TransitionEngine())
            .Should().Be(0);
    }
}
