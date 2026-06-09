using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 3 parity: functional document.cookie backed by a real CookieJar
/// (get/set round-trip). Mirrors the canonical behavior.
/// </summary>
[TestClass]
public sealed class CookieBindingsTests
{
    [TestMethod]
    public void cookie_set_then_get_roundtrips()
    {
        var e = NewSession();
        e.Evaluate("document.cookie = 'a=1'");
        e.Evaluate("document.cookie = 'b=2'");
        var cookies = e.Evaluate("document.cookie").AsString();
        cookies.Should().Contain("a=1").And.Contain("b=2");
    }

    [TestMethod]
    public void cookie_starts_empty()
    {
        var e = NewSession();
        e.Evaluate("document.cookie").AsString().Should().Be("");
    }

    [TestMethod]
    public void cookie_update_overwrites_same_name()
    {
        var e = NewSession();
        e.Evaluate("document.cookie = 'k=first'");
        e.Evaluate("document.cookie = 'k=second'");
        e.Evaluate("document.cookie").AsString().Should().Contain("k=second").And.NotContain("first");
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
