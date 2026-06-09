using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity (tasks/jint/PARITY_GAPS.md): DOM §6 traversal — NodeFilter,
/// TreeWalker, NodeIterator. Mirrors the canonical
/// <c>Starling.Bindings.Tests/TraversalBindingTests</c> against the Jint backend.
/// </summary>
[TestClass]
public sealed class TraversalBindingsTests
{
    private const string Html =
        "<!doctype html><html><head><title>Hi</title></head>" +
        "<body><p>one<span>x</span></p><!--c--><div>two</div></body></html>";

    [TestMethod]
    public void NodeFilter_constants_are_accessible()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("typeof NodeFilter").AsString().Should().Be("function");
        e.Evaluate("NodeFilter.SHOW_ALL === 0xFFFFFFFF && NodeFilter.SHOW_ELEMENT === 1 && " +
                   "NodeFilter.SHOW_TEXT === 4 && NodeFilter.SHOW_COMMENT === 0x80 && " +
                   "NodeFilter.FILTER_ACCEPT === 1 && NodeFilter.FILTER_REJECT === 2 && NodeFilter.FILTER_SKIP === 3")
            .AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void CreateTreeWalker_root_and_currentNode_and_instanceof()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.createTreeWalker(document.body).root === document.body").AsBoolean().Should().BeTrue();
        e.Evaluate("document.createTreeWalker(document.body).currentNode === document.body").AsBoolean().Should().BeTrue();
        e.Evaluate("document.createTreeWalker(document.body) instanceof TreeWalker").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void TreeWalker_nextNode_preorder()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var w = document.createTreeWalker(document.body);
              var names = []; var n;
              while ((n = w.nextNode()) !== null) names.push(n.nodeName);
              return names.join(',');
            })()
            """;
        // body → p, "one", span, "x", #comment, div, "two"
        e.Evaluate(js).AsString().Should().Be("P,#text,SPAN,#text,#comment,DIV,#text");
    }

    [TestMethod]
    public void TreeWalker_whatToShow_element_only()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var w = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT);
              var names = []; var n;
              while ((n = w.nextNode()) !== null) names.push(n.nodeName);
              return names.join(',');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("P,SPAN,DIV");
    }

    [TestMethod]
    public void TreeWalker_filter_function_rejects()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var w = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT, function(n){
                return n.nodeName === 'SPAN' ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT;
              });
              var names = []; var n;
              while ((n = w.nextNode()) !== null) names.push(n.nodeName);
              return names.join(',');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("P,DIV");
    }

    [TestMethod]
    public void TreeWalker_filter_object_acceptNode()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var w = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT, {
                acceptNode: function(n){ return n.nodeName === 'DIV' ? 1 : 3; }
              });
              var names = []; var n;
              while ((n = w.nextNode()) !== null) names.push(n.nodeName);
              return names.join(',');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("DIV");
    }

    [TestMethod]
    public void NodeIterator_iterates_preorder_and_back()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.createNodeIterator(document.body) instanceof NodeIterator").AsBoolean().Should().BeTrue();
        var js = """
            (function(){
              var it = document.createNodeIterator(document.body, NodeFilter.SHOW_ELEMENT);
              var fwd = []; var n;
              while ((n = it.nextNode()) !== null) fwd.push(n.nodeName);
              var back = [];
              while ((n = it.previousNode()) !== null) back.push(n.nodeName);
              return fwd.join(',') + '|' + back.join(',');
            })()
            """;
        // forward visits body,p,span,div; previousNode re-yields div (pointer
        // flips to before) then walks back through span,p,body.
        e.Evaluate(js).AsString().Should().Be("BODY,P,SPAN,DIV|DIV,SPAN,P,BODY");
    }

    [TestMethod]
    public void TreeWalker_firstChild_and_parentNode()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var w = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT);
              var a = w.firstChild().nodeName;      // P
              var b = w.firstChild().nodeName;      // SPAN
              var c = w.parentNode().nodeName;      // P
              return a + ',' + b + ',' + c;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("P,SPAN,P");
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
