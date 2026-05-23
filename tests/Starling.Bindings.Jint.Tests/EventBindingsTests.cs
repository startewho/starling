using AwesomeAssertions;
using Jint;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// J2c — EventTarget / Event bindings over the Jint backend. Mirrors the J2a
/// spike's <c>NewContext</c> setup, then installs the EventTarget + Event surface
/// (<see cref="EventTargetBinding.Install(JintBackendContext)"/>) and exercises it
/// from JS against the real Starling.Dom dispatch (capture/bubble/once via
/// <c>EventDispatcher</c>). Each test wraps a real DOM element so JS
/// <c>addEventListener</c> routes to the same host <c>EventTarget</c> the
/// document fires events on.
/// </summary>
[TestClass]
public sealed class EventBindingsTests
{
    [TestMethod]
    public void AddEventListener_then_dispatchEvent_fires_the_listener()
    {
        var (ctx, div) = Setup("<div id='x'>hi</div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.hits = 0;
            globalThis.seenType = '';
            globalThis.sameTarget = false;
            el.addEventListener('ping', function (e) {
                hits++;
                seenType = e.type;
                sameTarget = (e.target === el) && (e.currentTarget === el);
            });
            globalThis.ret = el.dispatchEvent(new Event('ping'));
        """);

        ctx.Engine.Evaluate("hits").AsNumber().Should().Be(1);
        ctx.Engine.Evaluate("seenType").AsString().Should().Be("ping");
        ctx.Engine.Evaluate("sameTarget").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("ret").AsBoolean().Should().BeTrue("an uncanceled dispatch returns true");
    }

    [TestMethod]
    public void Listener_added_with_handleEvent_object_is_invoked()
    {
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.hits = 0;
            globalThis.thisIsListener = false;
            const listener = { handleEvent(e) { hits++; thisIsListener = (this === listener); } };
            el.addEventListener('go', listener);
            el.dispatchEvent(new Event('go'));
        """);

        ctx.Engine.Evaluate("hits").AsNumber().Should().Be(1);
        ctx.Engine.Evaluate("thisIsListener").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void PreventDefault_makes_dispatchEvent_return_false_for_cancelable_event()
    {
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            el.addEventListener('go', e => e.preventDefault());
            globalThis.canceled = el.dispatchEvent(new Event('go', { cancelable: true }));
            globalThis.dp = false;
            el.addEventListener('go2', e => { e.preventDefault(); dp = e.defaultPrevented; });
            el.dispatchEvent(new Event('go2', { cancelable: true }));
        """);

        // dispatchEvent returns false (i.e. canceled) once a cancelable event is preventDefault'd.
        ctx.Engine.Evaluate("canceled").AsBoolean().Should().BeFalse();
        ctx.Engine.Evaluate("dp").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void StopPropagation_prevents_ancestor_bubble_listeners()
    {
        // parent > child; a bubbling event fired at child must reach the child
        // listener but be stopped before the parent's bubble listener.
        var (ctx, _) = Setup("<div id='parent'><span id='child'></span></div>");
        var parent = ctx.Document.GetElementById("parent")!;
        var child = ctx.Document.GetElementById("child")!;
        ctx.Engine.SetValue("parent", ctx.Wrappers.Wrap(parent));
        ctx.Engine.SetValue("child", ctx.Wrappers.Wrap(child));

        ctx.Engine.Execute("""
            globalThis.parentHit = false;
            globalThis.childHit = false;
            parent.addEventListener('bub', () => { parentHit = true; });
            child.addEventListener('bub', e => { childHit = true; e.stopPropagation(); });
            child.dispatchEvent(new Event('bub', { bubbles: true }));
        """);

        ctx.Engine.Evaluate("childHit").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("parentHit").AsBoolean().Should().BeFalse("stopPropagation blocks the bubble to the parent");
    }

    [TestMethod]
    public void StopImmediatePropagation_blocks_later_listeners_on_same_target()
    {
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.first = false;
            globalThis.second = false;
            el.addEventListener('go', e => { first = true; e.stopImmediatePropagation(); });
            el.addEventListener('go', () => { second = true; });
            el.dispatchEvent(new Event('go'));
        """);

        ctx.Engine.Evaluate("first").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("second").AsBoolean().Should().BeFalse();
    }

    [TestMethod]
    public void Once_listener_fires_exactly_once()
    {
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.hits = 0;
            el.addEventListener('go', () => { hits++; }, { once: true });
            el.dispatchEvent(new Event('go'));
            el.dispatchEvent(new Event('go'));
            el.dispatchEvent(new Event('go'));
        """);

        ctx.Engine.Evaluate("hits").AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void RemoveEventListener_with_same_function_and_capture_detaches()
    {
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.hits = 0;
            const fn = () => { hits++; };
            el.addEventListener('go', fn);
            el.dispatchEvent(new Event('go'));   // hits -> 1
            el.removeEventListener('go', fn);
            el.dispatchEvent(new Event('go'));   // no change
        """);

        ctx.Engine.Evaluate("hits").AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void RemoveEventListener_capture_mismatch_does_not_detach()
    {
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.hits = 0;
            const fn = () => { hits++; };
            el.addEventListener('go', fn, true);     // capture
            el.removeEventListener('go', fn, false); // wrong capture flag -> no-op
            el.dispatchEvent(new Event('go'));
        """);

        ctx.Engine.Evaluate("hits").AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void Capture_listener_runs_during_capture_phase_before_target()
    {
        var (ctx, _) = Setup("<div id='parent'><span id='child'></span></div>");
        var parent = ctx.Document.GetElementById("parent")!;
        var child = ctx.Document.GetElementById("child")!;
        ctx.Engine.SetValue("parent", ctx.Wrappers.Wrap(parent));
        ctx.Engine.SetValue("child", ctx.Wrappers.Wrap(child));

        ctx.Engine.Execute("""
            globalThis.order = [];
            parent.addEventListener('go', () => order.push('parent-capture'), { capture: true });
            child.addEventListener('go', () => order.push('child-target'));
            child.dispatchEvent(new Event('go'));
            globalThis.joined = order.join(',');
        """);

        ctx.Engine.Evaluate("joined").AsString().Should().Be("parent-capture,child-target");
    }

    [TestMethod]
    public void CustomEvent_exposes_detail()
    {
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.seenDetail = null;
            globalThis.isCustom = false;
            el.addEventListener('data', e => { seenDetail = e.detail; isCustom = (e instanceof CustomEvent); });
            el.dispatchEvent(new CustomEvent('data', { detail: { n: 42 } }));
            globalThis.directDetail = new CustomEvent('z', { detail: 'hi' }).detail;
        """);

        ctx.Engine.Evaluate("seenDetail.n").AsNumber().Should().Be(42);
        ctx.Engine.Evaluate("isCustom").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("directDetail").AsString().Should().Be("hi");
        ctx.Engine.Evaluate("(new CustomEvent('a')).detail").IsNull().Should().BeTrue("default detail is null");
    }

    [TestMethod]
    public void Event_constructor_reflects_init_dict_and_throws_without_type()
    {
        var (ctx, _) = Setup("<div></div>");

        ctx.Engine.Execute("""
            const e = new Event('go', { bubbles: true, cancelable: true, composed: true });
            globalThis.b = e.bubbles; globalThis.c = e.cancelable; globalThis.k = e.composed;
            globalThis.phase0 = e.eventPhase;          // None before dispatch
            globalThis.threw = false;
            try { new Event(); } catch (err) { globalThis.threw = (err instanceof TypeError); }
        """);

        ctx.Engine.Evaluate("b").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("c").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("k").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("phase0").AsNumber().Should().Be(0);
        ctx.Engine.Evaluate("threw").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void New_EventTarget_can_register_and_dispatch()
    {
        var (ctx, _) = Setup("<div></div>");

        ctx.Engine.Execute("""
            globalThis.hits = 0;
            const t = new EventTarget();
            t.addEventListener('x', () => hits++);
            t.dispatchEvent(new Event('x'));
        """);

        ctx.Engine.Evaluate("hits").AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void Native_DOM_dispatch_invokes_JS_listener_with_wrapped_event()
    {
        // Events fired by the host (not via JS dispatchEvent) must still reach JS
        // listeners with a correctly wrapped Event whose target is the JS wrapper.
        var (ctx, div) = Setup("<div id='x'></div>");
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));

        ctx.Engine.Execute("""
            globalThis.seen = '';
            globalThis.targetMatches = false;
            el.addEventListener('native', e => { seen = e.type; targetMatches = (e.target === el); });
        """);

        // Fire from the C# side, the way the DOM/layout would.
        div.DispatchEvent(new Starling.Dom.Events.Event("native"));

        ctx.Engine.Evaluate("seen").AsString().Should().Be("native");
        ctx.Engine.Evaluate("targetMatches").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void EventTarget_prototype_is_installed_as_chain_root()
    {
        var (ctx, _) = Setup("<div></div>");
        ctx.Wrappers.EventTargetPrototype.Should().NotBeNull();
        ctx.Wrappers.EventPrototype.Should().NotBeNull();

        // A node wrapped before any Node/Element prototype slot is set still
        // inherits EventTarget.prototype, so addEventListener resolves.
        var div = ctx.Document.GetElementsByTagName("div").Single();
        ctx.Engine.SetValue("el", ctx.Wrappers.Wrap(div));
        ctx.Engine.Evaluate("typeof el.addEventListener").AsString().Should().Be("function");
        ctx.Engine.Evaluate("typeof el.dispatchEvent").AsString().Should().Be("function");
    }

    // ---- shared setup (mirrors JintWrapperSpikeTests.NewContext) -------------

    private static (JintBackendContext Ctx, Element Div) Setup(string bodyHtml)
    {
        var doc = HtmlParser.Parse($"<!doctype html><html><body>{bodyHtml}</body></html>");
        var ctx = NewContext(doc);
        EventTargetBinding.Install(ctx);
        var div = doc.GetElementsByTagName("div").FirstOrDefault();
        return (ctx, div!);
    }

    private static JintBackendContext NewContext(Document doc)
    {
        var baseUrl = global::Starling.Url.UrlParser.Parse("about:blank").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        return new JintBackendContext(
            engine: engine,
            document: doc,
            baseUrl: baseUrl,
            http: http,
            diag: NoopDiagnostics.Instance,
            loop: new WebEventLoop(),
            layoutHost: null,
            fetch: (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
    }
}
