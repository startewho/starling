using AwesomeAssertions;
using Starling.Css.Animations;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

/// <summary>
/// CSS animation/transition DOM events on the Starling JS engine: the typed
/// AnimationEvent / TransitionEvent constructors, and the full engine-to-JS
/// chain — TransitionEngine / AnimationEngine queue event facts, the
/// AnimationEventDispatcher dispatches them on the host elements, and the
/// bridged JS listeners observe the typed properties with bubbling.
/// </summary>
[TestClass]
public sealed class AnimationEventBindingTests
{
    private static (JsRuntime Runtime, Document Doc, Element Child) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var child = doc.CreateElement("div");
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(child);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc);
        return (runtime, doc, child);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }

    private static Func<PropertyId, CssValue?> TransitionProps(double durationMs) => id => id switch
    {
        PropertyId.TransitionProperty => new CssKeyword("all"),
        PropertyId.TransitionDuration => new CssTime(durationMs, CssTimeUnit.Milliseconds),
        PropertyId.TransitionDelay => new CssTime(0, CssTimeUnit.Seconds),
        PropertyId.TransitionTimingFunction => new CssKeyword("linear"),
        _ => null,
    };

    [TestMethod]
    public void Typed_event_constructors_expose_spec_properties()
    {
        var (runtime, _, _) = BuildEnv();
        Eval(runtime, """
            var a = new AnimationEvent('animationstart', {
                animationName: 'spin', elapsedTime: 1.5, bubbles: true });
            var t = new TransitionEvent('transitionend', {
                propertyName: 'opacity', elapsedTime: 0.25, pseudoElement: '' });
            result = [
                a.animationName, a.elapsedTime, a.pseudoElement === '', a.bubbles,
                a instanceof AnimationEvent, a instanceof Event,
                t.propertyName, t.elapsedTime, t.cancelable,
                t instanceof TransitionEvent, t instanceof Event,
            ].join('|');
        """).AsString.Should().Be("spin|1.5|true|true|true|true|opacity|0.25|false|true|true");
    }

    [TestMethod]
    public void Engine_fired_transition_events_reach_js_listeners_and_bubble()
    {
        var (runtime, _, child) = BuildEnv();
        Eval(runtime, """
            globalThis.log = [];
            var el = document.querySelector('div');
            // All listeners sit on an ancestor — receipt proves bubbling.
            document.body.addEventListener('transitionrun', function (e) {
                log.push('run:' + e.propertyName + ':' + e.elapsedTime);
            });
            document.body.addEventListener('transitionstart', function (e) {
                log.push('start:' + e.propertyName);
            });
            document.body.addEventListener('transitionend', function (e) {
                log.push('end:' + e.propertyName + ':' + e.elapsedTime
                    + ':' + (e.target === el)
                    + ':' + (e instanceof TransitionEvent)
                    + ':' + e.isTrusted);
            });
            document.body.addEventListener('transitioncancel', function (e) {
                log.push('cancel:' + e.propertyName);
            });
            result = 'ready';
        """);

        var animations = new AnimationEngine();
        var transitions = new TransitionEngine();
        transitions.OnComputedValueChanged(child, PropertyId.Opacity, new CssNumber(0), TransitionProps(200));
        transitions.OnComputedValueChanged(child, PropertyId.Opacity, new CssNumber(1), TransitionProps(200));
        AnimationEventDispatcher.DispatchPending(animations, transitions); // transitionrun

        transitions.Tick(100);
        AnimationEventDispatcher.DispatchPending(animations, transitions); // transitionstart

        transitions.Tick(200);
        AnimationEventDispatcher.DispatchPending(animations, transitions); // transitionend

        Eval(runtime, "result = log.join('|');").AsString
            .Should().Be("run:opacity:0|start:opacity|end:opacity:0.2:true:true:true");
    }

    [TestMethod]
    public void Replaced_transition_fires_transitioncancel_into_js()
    {
        var (runtime, _, child) = BuildEnv();
        Eval(runtime, """
            globalThis.log = [];
            document.body.addEventListener('transitioncancel', function (e) {
                log.push('cancel:' + e.propertyName + ':' + e.elapsedTime);
            });
            result = 'ready';
        """);

        var animations = new AnimationEngine();
        var transitions = new TransitionEngine();
        transitions.OnComputedValueChanged(child, PropertyId.Opacity, new CssNumber(0), TransitionProps(200));
        transitions.OnComputedValueChanged(child, PropertyId.Opacity, new CssNumber(1), TransitionProps(200));
        transitions.Tick(100);
        // Reverse mid-flight: the in-flight transition cancels.
        transitions.OnComputedValueChanged(child, PropertyId.Opacity, new CssNumber(0), TransitionProps(200));
        AnimationEventDispatcher.DispatchPending(animations, transitions);

        Eval(runtime, "result = log.join('|');").AsString
            .Should().Be("cancel:opacity:0.1");
    }

    [TestMethod]
    public void Engine_fired_animation_events_reach_js_listeners_in_order()
    {
        var (runtime, _, child) = BuildEnv();
        Eval(runtime, """
            globalThis.log = [];
            var el = document.querySelector('div');
            el.addEventListener('animationstart', function (e) {
                log.push('start:' + e.animationName + ':' + e.elapsedTime);
            });
            // The on* handler attribute path must work too.
            el.onanimationiteration = function (e) {
                log.push('iter:' + e.animationName + ':' + e.elapsedTime);
            };
            el.addEventListener('animationend', function (e) {
                log.push('end:' + e.animationName + ':' + e.elapsedTime
                    + ':' + (e instanceof AnimationEvent));
            });
            result = 'ready';
        """);

        var animations = new AnimationEngine();
        var transitions = new TransitionEngine();
        animations.RegisterKeyframes(new KeyframesRule("fade", new[]
        {
            new Keyframe(0.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(0)) }),
            new Keyframe(1.0, new[] { new KeyframeDeclaration("opacity", new CssNumber(1)) }),
        }));
        animations.OnAnimationsCascaded(child, new[]
        {
            new AnimationDeclaration("fade", 100, 0, TimingFunction.Linear, 2,
                AnimationDirection.Normal, AnimationFillMode.None, AnimationPlayState.Running),
        });

        animations.Tick(0);
        AnimationEventDispatcher.DispatchPending(animations, transitions);
        animations.Tick(120);
        AnimationEventDispatcher.DispatchPending(animations, transitions);
        animations.Tick(250);
        AnimationEventDispatcher.DispatchPending(animations, transitions);

        Eval(runtime, "result = log.join('|');").AsString
            .Should().Be("start:fade:0|iter:fade:0.1|end:fade:0.2:true");
    }
}
