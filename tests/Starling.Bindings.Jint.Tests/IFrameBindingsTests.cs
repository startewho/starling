using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: HTMLIFrameElement browsing-context surface —
/// contentDocument/contentWindow, parent/top/frameElement wiring, and src load.
/// </summary>
[TestClass]
public sealed class IFrameBindingsTests
{
    private const string Html =
        "<!doctype html><html><body><iframe id='f'></iframe></body></html>";

    [TestMethod]
    public void srcless_iframe_has_blank_contentDocument()
    {
        var (e, _) = NewSession(Html, null);
        e.Evaluate("document.getElementById('f').contentDocument !== null").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('f').contentDocument.body.tagName").AsString().Should().Be("BODY");
        e.Evaluate("document.getElementById('f').contentDocument.documentElement.tagName").AsString().Should().Be("HTML");
    }

    [TestMethod]
    public void contentWindow_document_identity_and_wiring()
    {
        var (e, _) = NewSession(Html, null);
        e.Evaluate("document.getElementById('f').contentWindow.document === document.getElementById('f').contentDocument")
            .AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('f').contentWindow.self === document.getElementById('f').contentWindow")
            .AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('f').contentWindow.parent === window").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('f').contentWindow.top === window").AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('f').contentWindow.frameElement === document.getElementById('f')")
            .AsBoolean().Should().BeTrue();
        e.Evaluate("document.getElementById('f').contentWindow.length").AsNumber().Should().Be(0);
    }

    [TestMethod]
    public void contentDocument_defaultView_is_contentWindow()
    {
        var (e, _) = NewSession(Html, null);
        e.Evaluate("document.getElementById('f').contentDocument.defaultView === document.getElementById('f').contentWindow")
            .AsBoolean().Should().BeTrue();
        // main document.defaultView is the main window.
        e.Evaluate("document.defaultView === window").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void src_loads_html_and_fires_load_and_runs_scripts()
    {
        const string frameHtml =
            "<!doctype html><html><body><p id='p'>frame body</p>" +
            "<script>document.getElementById('p').textContent = 'changed by script';</script></body></html>";
        var (e, _) = NewSession(Html, (_, _) => System.Threading.Tasks.Task.FromResult<string?>(frameHtml));
        var js = """
            (function(){
              var f = document.getElementById('f');
              var loaded = false;
              f.addEventListener('load', function(){ loaded = true; });
              f.setAttribute('src', 'child.html');
              var doc = f.contentDocument;   // triggers lazy load
              return loaded + '|' + doc.getElementById('p').textContent;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|changed by script");
    }

    [TestMethod]
    public void onload_handler_is_invoked()
    {
        const string frameHtml = "<!doctype html><html><body>ok</body></html>";
        var (e, _) = NewSession(Html, (_, _) => System.Threading.Tasks.Task.FromResult<string?>(frameHtml));
        var js = """
            (function(){
              var f = document.getElementById('f');
              var n = 0;
              f.onload = function(){ n++; };
              f.setAttribute('src', 'child.html');
              void f.contentDocument;
              return n;
            })()
            """;
        e.Evaluate(js).AsNumber().Should().Be(1);
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession(
        string html, Func<global::Starling.Url.Url, CancellationToken, System.Threading.Tasks.Task<string?>>? fetch)
    {
        var doc = HtmlParser.Parse(html);
        var baseUrl = global::Starling.Url.UrlParser.Parse("https://example.com/").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine, doc, baseUrl, http, NullLoggerFactory.Instance,
            new WebEventLoop(), null,
            fetch ?? ((_, _) => System.Threading.Tasks.Task.FromResult<string?>(null)));
        JintBindings.InstallAll(ctx);
        return (engine, doc);
    }
}
