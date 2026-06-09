using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: DOM §4.9 Attr / NamedNodeMap — real element.attributes,
/// Attr-node methods, document.createAttribute. Mirrors canonical against Jint.
/// </summary>
[TestClass]
public sealed class AttrBindingsTests
{
    private const string Html =
        "<!doctype html><html><body><div id='d' class='a b' data-x='1'>hi</div></body></html>";

    [TestMethod]
    public void attributes_is_a_NamedNodeMap()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.getElementById('d').attributes instanceof NamedNodeMap").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('d').attributes.length").AsNumber().Should().Be(3);
        e.Evaluate("Object.prototype.toString.call(document.getElementById('d').attributes)")
            .AsString().Should().Be("[object NamedNodeMap]");
    }

    [TestMethod]
    public void attributes_indexed_and_named_access()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.getElementById('d').attributes[0] instanceof Attr").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('d').attributes.id.value").AsString().Should().Be("d");
        e.Evaluate("document.getElementById('d').attributes['data-x'].value").AsString().Should().Be("1");
        e.Evaluate("document.getElementById('d').attributes.getNamedItem('class').name").AsString().Should().Be("class");
        e.Evaluate("document.getElementById('d').attributes.item(0).name").AsString().Should().Be("id");
        e.Evaluate("document.getElementById('d').attributes.getNamedItem('missing')").IsNull().Should().BeTrue();
    }

    [TestMethod]
    public void getAttributeNode_returns_live_Attr()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.getElementById('d').getAttributeNode('id').value").AsString().Should().Be("d");
        e.Evaluate("document.getElementById('d').getAttributeNode('id').ownerElement === document.getElementById('d')")
            .AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('d').getAttributeNode('nope')").IsNull().Should().BeTrue();
    }

    [TestMethod]
    public void createAttribute_and_setAttributeNode_roundtrip()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var a = document.createAttribute('title');
              a.value = 'hello';
              var d = document.getElementById('d');
              d.setAttributeNode(a);
              return d.getAttribute('title') + '|' + (a instanceof Attr) + '|' + d.getAttributeNode('title').value;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("hello|true|hello");
    }

    [TestMethod]
    public void removeAttributeNode_removes_and_throws_when_missing()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var d = document.getElementById('d');
              var a = d.getAttributeNode('data-x');
              var removed = d.removeAttributeNode(a);
              return d.hasAttribute('data-x') + '|' + (removed === a);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("false|true");

        e.Evaluate("""
            (function(){
              var d = document.getElementById('d');
              var orphan = document.createAttribute('zzz');
              try { d.removeAttributeNode(orphan); return 'no-throw'; }
              catch (x) { return x.name; }
            })()
            """).AsString().Should().Be("NotFoundError");
    }

    [TestMethod]
    public void named_access_does_not_shadow_prototype_methods()
    {
        var (e, _) = NewSession(Html);
        // An attribute literally named "item" must not shadow the item() method.
        var js = """
            (function(){
              var d = document.getElementById('d');
              d.setAttribute('item', 'x');
              return typeof d.attributes.item;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("function");
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
