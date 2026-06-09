using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: DOM §4.6 Range + StaticRange. Mirrors the canonical backend
/// against Jint.
/// </summary>
[TestClass]
public sealed class RangeBindingsTests
{
    private const string Html =
        "<!doctype html><html><body><p id='p'>hello world</p><div id='d'><span>a</span><span>b</span></div></body></html>";

    [TestMethod]
    public void Range_constructor_and_createRange_and_instanceof()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("new Range() instanceof Range").AsBoolean().Should().BeTrue();
        e.Evaluate("document.createRange() instanceof Range").AsBoolean().Should().BeTrue();
        e.Evaluate("new Range().collapsed").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Range_constants_present()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("Range.START_TO_START").AsNumber().Should().Be(0);
        e.Evaluate("Range.START_TO_END").AsNumber().Should().Be(1);
        e.Evaluate("Range.END_TO_END").AsNumber().Should().Be(2);
        e.Evaluate("Range.END_TO_START").AsNumber().Should().Be(3);
        e.Evaluate("new Range().END_TO_START").AsNumber().Should().Be(3);
    }

    [TestMethod]
    public void Range_setStartEnd_and_stringify()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('p').firstChild;
              var r = document.createRange();
              r.setStart(t, 0); r.setEnd(t, 5);
              return r.toString() + '|' + r.startOffset + '|' + r.endOffset + '|' + r.collapsed;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("hello|0|5|false");
    }

    [TestMethod]
    public void Range_selectNodeContents_and_commonAncestor()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var d = document.getElementById('d');
              var r = document.createRange();
              r.selectNodeContents(d);
              return (r.commonAncestorContainer === d) + '|' + r.startOffset + '|' + r.endOffset;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|0|2");
    }

    [TestMethod]
    public void Range_cloneRange_is_independent()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('p').firstChild;
              var r = document.createRange(); r.setStart(t,0); r.setEnd(t,5);
              var c = r.cloneRange();
              c.setEnd(t, 11);
              return (c instanceof Range) + '|' + r.endOffset + '|' + c.endOffset;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|5|11");
    }

    [TestMethod]
    public void StaticRange_constructs_from_init()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('p').firstChild;
              var sr = new StaticRange({startContainer: t, startOffset: 1, endContainer: t, endOffset: 4});
              return sr.startOffset + '|' + sr.endOffset + '|' + sr.collapsed + '|' + (sr.startContainer === t);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("1|4|false|true");
    }

    [TestMethod]
    public void Range_compareBoundaryPoints()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('p').firstChild;
              var a = document.createRange(); a.setStart(t,0); a.setEnd(t,5);
              var b = document.createRange(); b.setStart(t,2); b.setEnd(t,5);
              return a.compareBoundaryPoints(Range.START_TO_START, b);
            })()
            """;
        // a.start (0) is before b.start (2) → -1
        e.Evaluate(js).AsNumber().Should().Be(-1);
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
