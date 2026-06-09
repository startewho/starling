using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: TextDecoder enforces fatal (throws on malformed UTF-8) and
/// resolves non-UTF-8 labels to a real encoding.
/// </summary>
[TestClass]
public sealed class TextDecoderFatalTests
{
    [TestMethod]
    public void fatal_throws_on_malformed_utf8()
    {
        var e = NewSession();
        // 0xFF is not valid UTF-8.
        var js = """
            (function(){
              try { new TextDecoder('utf-8', { fatal: true }).decode(new Uint8Array([0xFF])); return 'no-throw'; }
              catch (x) { return x.name; }
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("TypeError");
    }

    [TestMethod]
    public void non_fatal_replaces_malformed()
    {
        var e = NewSession();
        // Non-fatal decode does not throw (replacement char).
        e.Evaluate("(function(){ try { new TextDecoder().decode(new Uint8Array([0xFF])); return 'ok'; } catch (x) { return 'threw'; } })()")
            .AsString().Should().Be("ok");
    }

    [TestMethod]
    public void latin1_label_decodes_high_bytes()
    {
        var e = NewSession();
        // 0xE9 is 'é' in windows-1252/latin1.
        var js = "new TextDecoder('latin1').decode(new Uint8Array([0xE9]))";
        e.Evaluate(js).AsString().Should().Be("é");
        e.Evaluate("new TextDecoder('latin1').encoding").AsString().Should().Be("windows-1252");
    }

    [TestMethod]
    public void unknown_label_throws_RangeError()
    {
        var e = NewSession();
        e.Evaluate("(function(){ try { new TextDecoder('no-such-encoding'); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("RangeError");
    }

    [TestMethod]
    public void utf8_roundtrip_still_works()
    {
        var e = NewSession();
        e.Evaluate("new TextDecoder().decode(new TextEncoder().encode('héllo 🌟'))").AsString().Should().Be("héllo 🌟");
    }

    private static global::Jint.Engine NewSession()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body></body></html>");
        var baseUrl = global::Starling.Url.UrlParser.Parse("about:blank").Value;
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
