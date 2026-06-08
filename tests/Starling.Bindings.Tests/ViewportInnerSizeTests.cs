using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Bindings.Backend;
using Starling.Dom;
using Starling.Js.Hosting;

namespace Starling.Bindings.Tests;

/// <summary>
/// Guards the wiring that exposes the layout viewport to script as
/// <c>window.innerWidth</c>/<c>innerHeight</c>. The engine sets
/// <see cref="ScriptSessionOptions.ViewportWidth"/>/<c>ViewportHeight</c>; the
/// Starling backend must forward them into the window install options, or
/// responsive pages that branch on the inner size (and the common
/// "reveal above-the-fold content" failsafe that compares
/// <c>getBoundingClientRect().top</c> against <c>innerHeight</c>) silently break.
/// </summary>
[TestClass]
public sealed class ViewportInnerSizeTests
{
    [TestMethod]
    public void Window_inner_size_reflects_session_viewport()
    {
        using var session = NewSession(viewportWidth: 1280, viewportHeight: 900, out var doc, out var html);

        session.RunClassicScript(
            "document.documentElement.setAttribute('data-vp', window.innerWidth + 'x' + window.innerHeight);",
            "<test>");

        html.GetAttribute("data-vp").Should().Be("1280x900");
    }

    [TestMethod]
    public void Inner_size_defaults_to_zero_without_a_viewport_hint()
    {
        using var session = NewSession(viewportWidth: 0, viewportHeight: 0, out _, out var html);

        session.RunClassicScript(
            "document.documentElement.setAttribute('data-vp', window.innerWidth + 'x' + window.innerHeight);",
            "<test>");

        html.GetAttribute("data-vp").Should().Be("0x0");
    }

    private static IScriptSession NewSession(
        int viewportWidth, int viewportHeight, out Document doc, out Element html)
    {
        doc = new Document();
        html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);

        var url = global::Starling.Url.UrlParser.Parse("https://example.com/").Value!;
        var http = new global::Starling.Net.StarlingHttpClient();

        static Task<string?> Fetch(global::Starling.Url.Url url, CancellationToken ct)
            => Task.FromResult<string?>(null);

        var options = new ScriptSessionOptions(
            Document: doc,
            BaseUrl: url,
            Fetcher: Fetch,
            Http: http,
            LayoutHost: null,
            LoggerFactory: NullLoggerFactory.Instance)
        {
            ViewportWidth = viewportWidth,
            ViewportHeight = viewportHeight,
        };

        return new StarlingScriptEngineFactory().CreateSession(options);
    }
}
