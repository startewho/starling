using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 3/4 parity: real document.createEvent, the legacy Event surface
/// (initEvent/cancelBubble/returnValue, initCustomEvent), and full element.style
/// property coverage.
/// </summary>
[TestClass]
public sealed class LegacyEventAndStyleTests
{
    private const string Html = "<!doctype html><html><body><div id='d'>x</div></body></html>";

    [TestMethod]
    public void createEvent_returns_real_dispatchable_event()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var ev = document.createEvent('Event');
              ev.initEvent('ping', true, false);
              var seen = '';
              document.getElementById('d').addEventListener('ping', function(x){ seen = x.type + ':' + (x instanceof Event); });
              document.getElementById('d').dispatchEvent(ev);
              return seen + '|' + (ev instanceof Event) + '|' + ev.bubbles;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("ping:true|true|true");
    }

    [TestMethod]
    public void createEvent_mouseevent_and_customevent()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.createEvent('MouseEvent') instanceof MouseEvent").AsBoolean().Should().BeTrue();
        e.Evaluate("document.createEvent('UIEvent') instanceof UIEvent").AsBoolean().Should().BeTrue();
        var js = """
            (function(){
              var ev = document.createEvent('CustomEvent');
              ev.initCustomEvent('boom', false, false, { n: 5 });
              return ev.type + '|' + ev.detail.n + '|' + (ev instanceof CustomEvent);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("boom|5|true");
    }

    [TestMethod]
    public void createEvent_unknown_interface_throws_NotSupportedError()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("(function(){ try { document.createEvent('Nope'); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("NotSupportedError");
    }

    [TestMethod]
    public void legacy_cancelBubble_and_returnValue()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var ev = new Event('t', { cancelable: true });
              var rv1 = ev.returnValue;       // true
              ev.returnValue = false;          // preventDefault
              ev.cancelBubble = true;          // stopPropagation
              return rv1 + '|' + ev.returnValue + '|' + ev.defaultPrevented + '|' + ev.cancelBubble;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|false|true|true");
    }

    [TestMethod]
    public void element_style_exposes_uncommon_longhands()
    {
        var (e, _) = NewSession(Html);
        // An uncommon longhand should read "" (not undefined) and round-trip.
        var js = """
            (function(){
              var s = document.getElementById('d').style;
              var t = typeof s.borderTopLeftRadius;
              s.borderTopLeftRadius = '4px';
              return t + '|' + s.borderTopLeftRadius;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("string|4px");
    }

    [TestMethod]
    public void legacy_window_event_is_set_during_dispatch()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var during = 'none';
              document.body.addEventListener('ping', function(){ during = (window.event && window.event.type) || 'undef'; });
              var ev = document.createEvent('Event'); ev.initEvent('ping', false, false);
              document.body.dispatchEvent(ev);
              return during + '|' + (typeof window.event);  // undefined after dispatch
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("ping|undefined");
    }

    [TestMethod]
    public void svg_element_tagName_is_not_force_uppercased()
    {
        var (e, _) = NewSession(Html);
        // HTML elements upper-case; SVG-namespace elements are not force-uppercased
        // by the binding (the DOM stores the SVG localName as authored/normalized).
        e.Evaluate("document.body.tagName").AsString().Should().Be("BODY");
        var svgTag = e.Evaluate("document.createElementNS('http://www.w3.org/2000/svg', 'linearGradient').tagName").AsString();
        svgTag.Should().NotBe("LINEARGRADIENT");
        svgTag.Should().Be(e.Evaluate("document.createElementNS('http://www.w3.org/2000/svg', 'linearGradient').localName").AsString());
    }

    [TestMethod]
    public void dispatchEvent_throws_InvalidStateError_when_uninitialized_or_reentrant()
    {
        var (e, _) = NewSession(Html);
        // A createEvent() event is uninitialized until initEvent → dispatch throws.
        e.Evaluate("""
            (function(){
              var ev = document.createEvent('Event');
              try { document.body.dispatchEvent(ev); return 'no'; } catch (x) { return x.name; }
            })()
            """).AsString().Should().Be("InvalidStateError");
        // Re-entrant dispatch of the same event mid-dispatch throws InvalidStateError.
        e.Evaluate("""
            (function(){
              var ev = new Event('x');
              var caught = '';
              document.body.addEventListener('x', function(){
                try { document.body.dispatchEvent(ev); } catch (e2) { caught = e2.name; }
              });
              document.body.dispatchEvent(ev);
              return caught;
            })()
            """).AsString().Should().Be("InvalidStateError");
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession(string html)
    {
        var doc = HtmlParser.Parse(html);
        var baseUrl = global::Starling.Url.UrlParser.Parse("about:blank").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine, doc, baseUrl, http, NullLoggerFactory.Instance,
            new WebEventLoop(), null,
            (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return (engine, doc);
    }
}
