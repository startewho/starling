using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: Web Animations — element.animate → Animation + KeyframeEffect.
/// Mirrors canonical against Jint (inert no-host controls).
/// </summary>
[TestClass]
public sealed class WebAnimationsBindingsTests
{
    private const string Html = "<!doctype html><html><body><div id='d'>x</div></body></html>";

    [TestMethod]
    public void animate_returns_animation_with_controls()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var a = document.getElementById('d').animate([{opacity:0},{opacity:1}], 500);
              return [typeof a.play, typeof a.pause, typeof a.cancel, typeof a.finish,
                      typeof a.reverse, a.playState].join('|');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("function|function|function|function|function|idle");
    }

    [TestMethod]
    public void keyframe_effect_getKeyframes_array_form()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var a = document.getElementById('d').animate([{opacity:0, offset:0},{opacity:1}], 500);
              var kf = a.effect.getKeyframes();
              return kf.length + '|' + kf[0].offset + '|' + kf[1].offset + '|' + kf[0].opacity + '|' + kf[1].opacity;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("2|0|1|0|1");
    }

    [TestMethod]
    public void keyframe_effect_property_indexed_form()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var a = document.getElementById('d').animate({opacity:[0,1], transform:['none','scale(2)']}, 300);
              var kf = a.effect.getKeyframes();
              return kf.length + '|' + kf[0].opacity + '|' + kf[1].transform;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("2|0|scale(2)");
    }

    [TestMethod]
    public void getTiming_reads_options()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var a = document.getElementById('d').animate([{opacity:0}], {duration:250, delay:10, iterations:3, easing:'ease-in'});
              var t = a.effect.getTiming();
              return t.duration + '|' + t.delay + '|' + t.iterations + '|' + t.easing;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("250|10|3|ease-in");
    }

    [TestMethod]
    public void getComputedTiming_adds_activeDuration()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var a = document.getElementById('d').animate([{opacity:0}], {duration:100, iterations:2, delay:5});
              var t = a.effect.getComputedTiming();
              return t.activeDuration + '|' + t.endTime;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("200|205");
    }

    [TestMethod]
    public void finished_and_ready_are_promises()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("typeof document.getElementById('d').animate([{opacity:0}],100).finished.then")
            .AsString().Should().Be("function");
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
