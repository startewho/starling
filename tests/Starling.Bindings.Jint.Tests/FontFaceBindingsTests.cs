using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: CSS Font Loading — document.fonts (FontFaceSet) + FontFace
/// constructor. Mirrors canonical against Jint.
/// </summary>
[TestClass]
public sealed class FontFaceBindingsTests
{
    private const string Html =
        "<!doctype html><html><head><style>" +
        "@font-face { font-family: 'Acme'; src: url(acme.woff2); }" +
        "@font-face { font-family: 'Beta'; src: url(beta.woff2); font-weight: bold; }" +
        "</style></head><body></body></html>";

    [TestMethod]
    public void fonts_set_is_seeded_from_font_face_rules()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.fonts.size").AsNumber().Should().Be(2);
        e.Evaluate("document.fonts.status").AsString().Should().Be("loaded");
    }

    [TestMethod]
    public void FontFace_constructor_reads_descriptors()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var f = new FontFace('Custom', 'url(c.woff2)', { weight: '700', style: 'italic' });
              return f.family + '|' + f.weight + '|' + f.style;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("Custom|700|italic");
    }

    [TestMethod]
    public void FontFace_requires_two_args()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("(function(){ try { new FontFace('x'); return 'no'; } catch (e) { return 'threw'; } })()")
            .AsString().Should().Be("threw");
    }

    [TestMethod]
    public void fonts_add_has_delete_roundtrip()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var f = new FontFace('New', 'url(n.woff2)');
              var before = document.fonts.has(f);
              document.fonts.add(f);
              var after = document.fonts.has(f);
              var del = document.fonts.delete(f);
              return before + '|' + after + '|' + del + '|' + document.fonts.has(f);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("false|true|true|false");
    }

    [TestMethod]
    public void fonts_check_and_forEach()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("document.fonts.check('12px Acme')").AsBoolean().Should().BeTrue();
        var js = """
            (function(){
              var fams = [];
              document.fonts.forEach(function(f){ fams.push(f.family); });
              return fams.sort().join(',');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("Acme,Beta");
    }

    [TestMethod]
    public void fonts_ready_and_face_load_return_promises()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("typeof document.fonts.ready.then").AsString().Should().Be("function");
        e.Evaluate("typeof new FontFace('Z','url(z)').load().then").AsString().Should().Be("function");
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
