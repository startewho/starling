using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: Node/Element/Document method holes — getRootNode, isSameNode,
/// isEqualNode, compareDocumentPosition (+ constants), hasAttributes, click,
/// document.doctype + DocumentType name/publicId/systemId.
/// </summary>
[TestClass]
public sealed class NodeMethodHolesTests
{
    private const string Html =
        "<!doctype html><html><body><div id='a'><span id='s'>x</span></div><div id='b'>y</div></body></html>";

    [TestMethod]
    public void getRootNode_and_isSameNode()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.getElementById('s').getRootNode() === document").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('a').isSameNode(document.getElementById('a'))").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('a').isSameNode(document.getElementById('b'))").AsBoolean().Should().BeFalse();
    }

    [TestMethod]
    public void isEqualNode_structural()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var p1 = document.createElement('p'); p1.setAttribute('class','x'); p1.appendChild(document.createTextNode('hi'));
              var p2 = document.createElement('p'); p2.setAttribute('class','x'); p2.appendChild(document.createTextNode('hi'));
              var p3 = document.createElement('p'); p3.setAttribute('class','y'); p3.appendChild(document.createTextNode('hi'));
              return p1.isEqualNode(p2) + '|' + p1.isEqualNode(p3) + '|' + p1.isSameNode(p2);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|false|false");
    }

    [TestMethod]
    public void compareDocumentPosition_constants_and_order()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("Node.DOCUMENT_POSITION_FOLLOWING").AsNumber().Should().Be(4);
        e.Evaluate("Node.DOCUMENT_POSITION_CONTAINED_BY").AsNumber().Should().Be(16);
        // #a precedes #b → a.compareDocumentPosition(b) has FOLLOWING (4).
        e.Evaluate("(document.getElementById('a').compareDocumentPosition(document.getElementById('b')) & 4) !== 0")
            .AsBoolean().Should().BeTrue();
        // #a contains #s → CONTAINED_BY (16).
        e.Evaluate("(document.getElementById('a').compareDocumentPosition(document.getElementById('s')) & 16) !== 0")
            .AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void hasAttributes_and_click()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.getElementById('a').hasAttributes()").AsBoolean().Should().BeTrue();
        e.Evaluate("document.createElement('div').hasAttributes()").AsBoolean().Should().BeFalse();
        var js = """
            (function(){
              var n = 0;
              document.getElementById('a').addEventListener('click', function(){ n++; });
              document.getElementById('a').click();
              return n;
            })()
            """;
        e.Evaluate(js).AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void document_doctype_exposes_name()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.doctype !== null").AsBoolean().Should().BeTrue();
        e.Evaluate("document.doctype.name").AsString().Should().Be("html");
        e.Evaluate("document.doctype instanceof DocumentType").AsBoolean().Should().BeTrue();
        e.Evaluate("typeof document.doctype.publicId").AsString().Should().Be("string");
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
