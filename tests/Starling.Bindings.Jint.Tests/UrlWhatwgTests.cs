using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: the URL engine is the WHATWG `Starling.Url` parser (not
/// System.Uri), so edge cases match the reference backend — default-port
/// stripping, non-special schemes, relative resolution, component setters.
/// </summary>
[TestClass]
public sealed class UrlWhatwgTests
{
    [TestMethod]
    public void default_port_is_stripped()
    {
        var e = NewSession();
        e.Evaluate("new URL('https://example.com:443/p').port").AsString().Should().Be("");
        e.Evaluate("new URL('https://example.com:443/p').host").AsString().Should().Be("example.com");
        e.Evaluate("new URL('http://example.com:8080/p').port").AsString().Should().Be("8080");
        e.Evaluate("new URL('http://example.com:8080/p').host").AsString().Should().Be("example.com:8080");
    }

    [TestMethod]
    public void components_and_origin()
    {
        var e = NewSession();
        var js = """
            (function(){
              var u = new URL('https://user:pass@example.com:8443/a/b?x=1#frag');
              return [u.protocol, u.hostname, u.port, u.pathname, u.search, u.hash, u.origin].join('|');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("https:|example.com|8443|/a/b|?x=1|#frag|https://example.com:8443");
    }

    [TestMethod]
    public void relative_resolution_against_base()
    {
        var e = NewSession();
        e.Evaluate("new URL('../c', 'https://example.com/a/b/').href").AsString().Should().Be("https://example.com/a/c");
        e.Evaluate("new URL('/x', 'https://example.com/a/b').pathname").AsString().Should().Be("/x");
    }

    [TestMethod]
    public void non_special_scheme_origin_is_null()
    {
        var e = NewSession();
        e.Evaluate("new URL('data:text/plain,hi').origin").AsString().Should().Be("null");
        e.Evaluate("new URL('mailto:a@b.com').protocol").AsString().Should().Be("mailto:");
    }

    [TestMethod]
    public void setters_reparse()
    {
        var e = NewSession();
        var js = """
            (function(){
              var u = new URL('https://example.com/p');
              u.pathname = '/q'; u.search = 'a=1'; u.hash = 'h'; u.port = '8080';
              return u.href;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("https://example.com:8080/q?a=1#h");
    }

    [TestMethod]
    public void searchParams_sync_with_url()
    {
        var e = NewSession();
        var js = """
            (function(){
              var u = new URL('https://example.com/?a=1');
              u.searchParams.append('b', '2');
              return u.search + '|' + u.searchParams.get('a') + '|' + u.searchParams.getAll('b').join(',');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("?a=1&b=2|1|2");
    }

    [TestMethod]
    public void invalid_url_throws_TypeError()
    {
        var e = NewSession();
        e.Evaluate("(function(){ try { new URL('not a url'); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("TypeError");
    }

    private static global::Jint.Engine NewSession()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body></body></html>");
        var baseUrl = global::Starling.Url.UrlParser.Parse("https://example.com/").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine, doc, baseUrl, http, NullLoggerFactory.Instance,
            new WebEventLoop(), null,
            (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return engine;
    }
}
