using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Html;
using Starling.Js.Hosting;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: window.onerror is invoked on an uncaught listener error
/// (message, source, lineno, colno, error); a truthy return cancels the default
/// console report. Exercised through the session's DOMContentLoaded dispatch path.
/// </summary>
[TestClass]
public sealed class WindowOnErrorTests
{
    [TestMethod]
    public void onerror_invoked_and_can_cancel_console_report()
    {
        var logs = new List<string>();
        using var session = NewSession(logs);
        session.RunClassicScript("""
            window.__seen = null;
            window.onerror = function(message, source, lineno, colno, error){
              window.__seen = message;
              console.log('ONERROR:' + message);
              return true; // handled → suppress default "Uncaught" console report
            };
            document.addEventListener('DOMContentLoaded', function(){ throw new Error('boom'); });
            """, "<setup>");

        session.FireDomContentLoaded();

        logs.Should().Contain(l => l.Contains("ONERROR") && l.Contains("boom"));
        logs.Should().NotContain(l => l.StartsWith("Uncaught"));
    }

    [TestMethod]
    public void without_onerror_listener_error_is_swallowed()
    {
        var logs = new List<string>();
        using var session = NewSession(logs);
        session.RunClassicScript(
            "document.addEventListener('DOMContentLoaded', function(){ throw new Error('kaboom'); });",
            "<setup>");

        // No onerror installed → the listener error is reported to the engine log,
        // not surfaced as ONERROR, and dispatch must not throw out of FireDomContentLoaded.
        var act = () => session.FireDomContentLoaded();
        act.Should().NotThrow();
        logs.Should().NotContain(l => l.Contains("ONERROR"));
    }

    [TestMethod]
    public void onerror_receives_message_and_error_arguments()
    {
        var logs = new List<string>();
        using var session = NewSession(logs);
        session.RunClassicScript("""
            window.onerror = function(message, source, lineno, colno, error){
              console.log('ARGS:' + (typeof message) + ',' + (error instanceof Error));
              return true;
            };
            document.addEventListener('DOMContentLoaded', function(){ throw new TypeError('x'); });
            """, "<setup>");
        session.FireDomContentLoaded();
        logs.Should().Contain(l => l.Contains("ARGS:string,true"));
    }

    private static JintScriptSession NewSession(List<string> logs)
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body></body></html>");
        var url = global::Starling.Url.UrlParser.Parse("https://example.com/").Value;
        var http = new Starling.Net.StarlingHttpClient();
        var options = new ScriptSessionOptions(
            Document: doc,
            BaseUrl: url,
            Fetcher: (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
            Http: http,
            LayoutHost: null,
            LoggerFactory: NullLoggerFactory.Instance);
        return new JintScriptSession(options) { ConsoleSink = (_, msg) => logs.Add(msg) };
    }
}
