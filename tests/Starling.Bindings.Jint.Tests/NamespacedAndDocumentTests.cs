using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: namespaced attr methods, Node namespace lookups + baseURI,
/// and Document factories (createCDATASection/PI/attributeNS, adoptNode,
/// importNode, getElementsByTagNameNS, DOMImplementation).
/// </summary>
[TestClass]
public sealed class NamespacedAndDocumentTests
{
    private const string Html = "<!doctype html><html><body><div id='d'></div></body></html>";

    [TestMethod]
    public void namespaced_attribute_methods()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var d = document.getElementById('d');
              d.setAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href', '#x');
              var has = d.hasAttributeNS('http://www.w3.org/1999/xlink', 'href');
              var val = d.getAttributeNS('http://www.w3.org/1999/xlink', 'href');
              d.removeAttributeNS('http://www.w3.org/1999/xlink', 'href');
              return has + '|' + val + '|' + d.hasAttributeNS('http://www.w3.org/1999/xlink', 'href');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|#x|false");
    }

    [TestMethod]
    public void baseURI_and_namespace_lookups()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("typeof document.getElementById('d').baseURI").AsString().Should().Be("string");
        // An HTML element resolves the HTML namespace as the default.
        e.Evaluate("document.getElementById('d').lookupNamespaceURI(null)").AsString()
            .Should().Be("http://www.w3.org/1999/xhtml");
        e.Evaluate("document.getElementById('d').isDefaultNamespace('http://www.w3.org/1999/xhtml')")
            .AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void createCDATASection_and_PI_and_attributeNS()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.createCDATASection('x').nodeType").AsNumber().Should().Be(4);
        e.Evaluate("document.createProcessingInstruction('xml-stylesheet', 'href=\"x\"').nodeType").AsNumber().Should().Be(7);
        e.Evaluate("document.createAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href').name").AsString().Should().Be("xlink:href");
        e.Evaluate("(function(){ try { document.createCDATASection(']]>'); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("InvalidCharacterError");
    }

    [TestMethod]
    public void importNode_clones_and_adoptNode_detaches()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var p = document.createElement('p'); p.id = 'orig';
              document.body.appendChild(p);
              var copy = document.importNode(p, true);
              var adopted = document.adoptNode(p);
              return (copy !== p) + '|' + copy.id + '|' + (adopted === p) + '|' + (p.parentNode === null);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|orig|true|true");
    }

    [TestMethod]
    public void DOMImplementation_createHTMLDocument_and_DocumentType()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.implementation === document.implementation").AsBoolean().Should().BeTrue();
        e.Evaluate("typeof document.implementation.createHTMLDocument").AsString().Should().Be("function");
        var js = """
            (function(){
              var d = document.implementation.createHTMLDocument('Hi');
              var dt = document.implementation.createDocumentType('html', '', '');
              return d.doctype !== null && d.title === 'Hi' ? ('ok|' + dt.name) : ('no|' + d.title);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("ok|html");
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession(string html)
    {
        var doc = HtmlParser.Parse(html);
        var baseUrl = global::Starling.Url.UrlParser.Parse("https://example.com/page").Value;
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
