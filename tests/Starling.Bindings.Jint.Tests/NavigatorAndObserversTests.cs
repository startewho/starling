using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: navigator extras (hardwareConcurrency/maxTouchPoints/webdriver +
/// clipboard/geolocation/serviceWorker sub-APIs) and observer liveness
/// (IntersectionObserver options/accessors + real unobserve/disconnect,
/// ResizeObserver box validation).
/// </summary>
[TestClass]
public sealed class NavigatorAndObserversTests
{
    private const string Html = "<!doctype html><html><body><div id='d'></div></body></html>";

    [TestMethod]
    public void navigator_extras_present()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("navigator.hardwareConcurrency >= 1").AsBoolean().Should().BeTrue();
        e.Evaluate("navigator.maxTouchPoints").AsNumber().Should().Be(0);
        e.Evaluate("navigator.webdriver").AsBoolean().Should().BeFalse();
        e.Evaluate("'clipboard' in navigator && typeof navigator.clipboard.writeText").AsString().Should().Be("function");
        e.Evaluate("'geolocation' in navigator && typeof navigator.geolocation.getCurrentPosition").AsString().Should().Be("function");
        e.Evaluate("'serviceWorker' in navigator && typeof navigator.serviceWorker.register").AsString().Should().Be("function");
    }

    [TestMethod]
    public void intersectionObserver_options_and_accessors()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var io = new IntersectionObserver(function(){}, { rootMargin: '10px 20px 10px 20px', threshold: [0, 0.5, 1] });
              return io.rootMargin + '|' + io.thresholds.join(',') + '|' + (io.root === null);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("10px 20px 10px 20px|0,0.5,1|true");
    }

    [TestMethod]
    public void observer_observe_unobserve_disconnect_track_targets()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var ro = new ResizeObserver(function(){});
              var d = document.getElementById('d');
              ro.observe(d);
              var n1 = ro._targets.length;
              ro.unobserve(d);
              var n2 = ro._targets.length;
              ro.observe(d); ro.observe(d);  // dedup
              var n3 = ro._targets.length;
              ro.disconnect();
              return n1 + '|' + n2 + '|' + n3 + '|' + ro._targets.length;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("1|0|1|0");
    }

    [TestMethod]
    public void resizeObserver_rejects_bad_box()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("(function(){ try { new ResizeObserver(function(){}).observe(document.getElementById('d'), { box: 'nope' }); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("TypeError");
        // valid box options are accepted
        e.Evaluate("(function(){ try { new ResizeObserver(function(){}).observe(document.getElementById('d'), { box: 'border-box' }); return 'ok'; } catch (x) { return 'threw'; } })()")
            .AsString().Should().Be("ok");
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession(string html)
    {
        var doc = HtmlParser.Parse(html);
        var baseUrl = global::Starling.Url.UrlParser.Parse("https://example.com/").Value;
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
