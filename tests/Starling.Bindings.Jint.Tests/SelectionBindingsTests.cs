using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: Selection API — window/document.getSelection, Selection
/// prototype shape and behavior. Mirrors the canonical backend against Jint.
/// </summary>
[TestClass]
public sealed class SelectionBindingsTests
{
    private const string Html =
        "<!doctype html><html><body><p id='p'>hello world</p></body></html>";

    [TestMethod]
    public void getSelection_returns_same_Selection_instance()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("getSelection() instanceof Selection").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getSelection() instanceof Selection").AsBoolean().Should().BeTrue();
        e.Evaluate("getSelection() === document.getSelection()").AsBoolean().Should().BeTrue();
        e.Evaluate("getSelection() === getSelection()").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Selection_not_user_constructible()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("(function(){ try { new Selection(); return 'no-throw'; } catch (x) { return 'threw'; } })()")
            .AsString().Should().Be("threw");
    }

    [TestMethod]
    public void Selection_starts_collapsed_and_empty()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("getSelection().rangeCount").AsNumber().Should().Be(0);
        e.Evaluate("getSelection().isCollapsed").AsBoolean().Should().BeTrue();
        e.Evaluate("getSelection().type").AsString().Should().Be("None");
        e.Evaluate("getSelection().anchorNode").IsNull().Should().BeTrue();
        e.Evaluate("String(getSelection())").AsString().Should().Be("");
    }

    [TestMethod]
    public void Selection_selectAllChildren_and_toString()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var s = getSelection();
              s.selectAllChildren(document.getElementById('p'));
              return s.rangeCount + '|' + s.anchorOffset + '|' + s.toString();
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("1|0|hello world");
    }

    [TestMethod]
    public void Selection_addRange_reflects_boundary_points()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('p').firstChild;
              var r = document.createRange(); r.setStart(t, 0); r.setEnd(t, 5);
              var s = getSelection(); s.removeAllRanges(); s.addRange(r);
              return s.rangeCount + '|' + (s.anchorNode === t) + '|' + s.focusOffset + '|' + s.toString();
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("1|true|5|hello");
    }

    [TestMethod]
    public void Selection_getRangeAt_returns_a_Range()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('p').firstChild;
              var r = document.createRange(); r.setStart(t, 0); r.setEnd(t, 5);
              var s = getSelection(); s.addRange(r);
              return s.getRangeAt(0) instanceof Range;
            })()
            """;
        e.Evaluate(js).AsBoolean().Should().BeTrue();
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
