using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 3 parity: a real MutationObserver that queues MutationRecords. Records are
/// asserted synchronously via takeRecords() (delivery is otherwise a microtask).
/// </summary>
[TestClass]
public sealed class MutationObserverBindingsTests
{
    private const string Html = "<!doctype html><html><body><div id='d'><span id='s'>hi</span></div></body></html>";

    [TestMethod]
    public void surface_present()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("typeof MutationObserver").AsString().Should().Be("function");
        e.Evaluate("typeof new MutationObserver(function(){}).observe").AsString().Should().Be("function");
        e.Evaluate("typeof new MutationObserver(function(){}).takeRecords").AsString().Should().Be("function");
    }

    [TestMethod]
    public void childList_records_are_queued()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var mo = new MutationObserver(function(){});
              mo.observe(document.getElementById('d'), { childList: true });
              document.getElementById('d').appendChild(document.createElement('p'));
              var recs = mo.takeRecords();
              return recs.length + '|' + recs[0].type + '|' + recs[0].addedNodes.length + '|' + recs[0].addedNodes[0].tagName;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("1|childList|1|P");
    }

    [TestMethod]
    public void attribute_records_with_oldValue_and_filter()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var mo = new MutationObserver(function(){});
              mo.observe(document.getElementById('d'), { attributes: true, attributeOldValue: true, attributeFilter: ['data-x'] });
              var d = document.getElementById('d');
              d.setAttribute('data-x', '1');
              d.setAttribute('data-x', '2');
              d.setAttribute('class', 'ignored');   // filtered out
              var recs = mo.takeRecords();
              return recs.length + '|' + recs[0].type + '|' + recs[0].attributeName + '|' + recs[1].oldValue;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("2|attributes|data-x|1");
    }

    [TestMethod]
    public void subtree_observation_matches_descendants()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var mo = new MutationObserver(function(){});
              mo.observe(document.getElementById('d'), { attributes: true, subtree: true });
              document.getElementById('s').setAttribute('role', 'note');
              return mo.takeRecords().length;
            })()
            """;
        e.Evaluate(js).AsNumber().Should().Be(1);
        // Without subtree, a descendant mutation is not observed.
        var js2 = """
            (function(){
              var mo = new MutationObserver(function(){});
              mo.observe(document.getElementById('d'), { attributes: true });
              document.getElementById('s').setAttribute('role', 'x');
              return mo.takeRecords().length;
            })()
            """;
        e.Evaluate(js2).AsNumber().Should().Be(0);
    }

    [TestMethod]
    public void observe_requires_a_type_option()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("(function(){ try { new MutationObserver(function(){}).observe(document.body, {}); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("TypeError");
    }

    [TestMethod]
    public void disconnect_clears_pending()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var mo = new MutationObserver(function(){});
              mo.observe(document.getElementById('d'), { childList: true });
              document.getElementById('d').appendChild(document.createElement('p'));
              mo.disconnect();
              return mo.takeRecords().length;
            })()
            """;
        e.Evaluate(js).AsNumber().Should().Be(0);
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
