using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: CSS Typed OM style maps — element.attributeStyleMap (mutable,
/// over inline style) and element.computedStyleMap() (read-only surface).
/// </summary>
[TestClass]
public sealed class TypedOmStyleMapTests
{
    private const string Html = "<!doctype html><html><body><div id='d' style='color: red'></div></body></html>";

    [TestMethod]
    public void attributeStyleMap_get_set_has_delete_size()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var m = document.getElementById('d').attributeStyleMap;
              var hadColor = m.has('color');
              var colorText = String(m.get('color'));
              m.set('opacity', '0.5');
              var size1 = m.size;
              m.delete('color');
              var hasColor2 = m.has('color');
              return hadColor + '|' + colorText + '|' + size1 + '|' + hasColor2;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|red|2|false");
    }

    [TestMethod]
    public void attributeStyleMap_set_roundtrips_to_inline_style()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var d = document.getElementById('d');
              d.attributeStyleMap.set('display', 'flex');
              return d.style.display;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("flex");
    }

    [TestMethod]
    public void attributeStyleMap_forEach_and_clear()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var m = document.getElementById('d').attributeStyleMap;
              m.set('opacity', '1');
              var names = []; m.forEach(function(v, k){ names.push(k); });
              m.clear();
              return names.sort().join(',') + '|' + m.size;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("color,opacity|0");
    }

    [TestMethod]
    public void computedStyleMap_surface_present()
    {
        var (e, _) = NewSession(Html);
        // No layout host in the bare context → empty, but the map + methods exist.
        e.Evaluate("typeof document.getElementById('d').computedStyleMap").AsString().Should().Be("function");
        e.Evaluate("typeof document.getElementById('d').computedStyleMap().get").AsString().Should().Be("function");
        e.Evaluate("document.getElementById('d').computedStyleMap().size").AsNumber().Should().Be(0);
        e.Evaluate("document.getElementById('d').computedStyleMap().has('color')").AsBoolean().Should().BeFalse();
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
