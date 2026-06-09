using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 3/4 parity: real Blob/File/FormData classes, AbortController→fetch, and
/// fetch body coverage (Response.blob/formData, Headers.getSetCookie). The async
/// pieces are resolved by driving the session pump.
/// </summary>
[TestClass]
public sealed class BlobFileFormDataAbortTests
{
    // ---- Blob / File (bare context, synchronous surface) --------------------

    [TestMethod]
    public void blob_construct_size_type_slice()
    {
        var e = Bare();
        e.Evaluate("new Blob(['hello', ' world']).size").AsNumber().Should().Be(11);
        e.Evaluate("new Blob(['x'], { type: 'TEXT/Plain' }).type").AsString().Should().Be("text/plain");
        e.Evaluate("new Blob(['hello world']).slice(0, 5).size").AsNumber().Should().Be(5);
        e.Evaluate("new Blob(['abc']) instanceof Blob").AsBoolean().Should().BeTrue();
        e.Evaluate("Object.prototype.toString.call(new Blob([]))").AsString().Should().Be("[object Blob]");
    }

    [TestMethod]
    public void file_extends_blob_with_name_and_lastModified()
    {
        var e = Bare();
        e.Evaluate("new File(['x'], 'a.txt', { type: 'text/plain', lastModified: 123 }).name").AsString().Should().Be("a.txt");
        e.Evaluate("new File(['x'], 'a.txt').lastModified").AsNumber().Should().Be(0);
        e.Evaluate("new File(['x'], 'a.txt') instanceof Blob").AsBoolean().Should().BeTrue();
        e.Evaluate("new File(['x'], 'a.txt') instanceof File").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void formData_full_surface()
    {
        var e = Bare();
        var js = """
            (function(){
              var fd = new FormData();
              fd.append('a', '1'); fd.append('a', '2'); fd.append('b', '3');
              var all = fd.getAll('a').join(',');
              fd.set('a', '9');
              var afterSet = fd.getAll('a').join(',');
              fd.delete('b');
              var keys = []; for (var k of fd.keys()) keys.push(k);
              var seen = []; fd.forEach(function(v, k){ seen.push(k + '=' + v); });
              return all + '|' + afterSet + '|' + fd.has('b') + '|' + keys.join(',') + '|' + seen.join(';');
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("1,2|9|false|a|a=9");
    }

    [TestMethod]
    public void formData_accepts_blob_values_as_file()
    {
        var e = Bare();
        var js = """
            (function(){
              var fd = new FormData();
              fd.append('f', new Blob(['data'], { type: 'text/plain' }), 'note.txt');
              var v = fd.get('f');
              return (v instanceof File) + '|' + v.name + '|' + v.size;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("true|note.txt|4");
    }

    // ---- fetch + abort + body (session) -------------------------------------

    [TestMethod]
    public void aborted_signal_rejects_fetch()
    {
        var logs = new List<string>();
        using var session = Session(logs);
        session.RunClassicScript("""
            window.__r = 'pending';
            var c = new AbortController();
            c.abort();
            fetch('https://example.com/', { signal: c.signal })
              .then(function(){ window.__r = 'resolved'; })
              .catch(function(e){ window.__r = 'rejected:' + e.name; });
            """, "<t>");
        Pump(session);
        session.RunClassicScript("console.log('R:' + window.__r);", "<t>");
        logs.Should().Contain(l => l == "R:rejected:AbortError");
    }

    [TestMethod]
    public void request_signal_is_exposed_and_abort_static()
    {
        var logs = new List<string>();
        using var session = Session(logs);
        session.RunClassicScript("""
            var c = new AbortController();
            var r = new Request('https://example.com/', { signal: c.signal });
            console.log('SIG:' + (r.signal === c.signal));
            console.log('ABORTED:' + AbortSignal.abort().aborted);
            """, "<t>");
        logs.Should().Contain("SIG:true");
        logs.Should().Contain("ABORTED:true");
    }

    [TestMethod]
    public void response_blob_and_formData_and_getSetCookie()
    {
        var logs = new List<string>();
        using var session = Session(logs);
        session.RunClassicScript("""
            window.__out = '';
            var r = new Response('a=1&b=2', { headers: { 'content-type': 'application/x-www-form-urlencoded', 'set-cookie': 'x=1' } });
            r.clone().blob().then(function(b){ window.__out += 'blob:' + b.size + ',' + b.type + '|'; });
            r.formData().then(function(fd){ window.__out += 'fd:' + fd.get('a') + ',' + fd.get('b') + '|'; });
            window.__sc = new Response('', { headers: { 'set-cookie': 'k=v' } }).headers.getSetCookie().join(';');
            """, "<t>");
        Pump(session);
        session.RunClassicScript("console.log('O:' + window.__out + ' SC:' + window.__sc);", "<t>");
        logs.Should().Contain(l => l.Contains("blob:7,application/x-www-form-urlencoded")
                                   && l.Contains("fd:1,2") && l.Contains("SC:k=v"));
    }

    [TestMethod]
    public void formData_body_sets_multipart_content_type()
    {
        var logs = new List<string>();
        using var session = Session(logs);
        session.RunClassicScript("""
            var fd = new FormData();
            fd.append('field', 'value');
            var req = new Request('https://example.com/', { method: 'POST', body: fd });
            console.log('CT:' + /^multipart\/form-data; boundary=/.test(req.headers.get('content-type')));
            """, "<t>");
        logs.Should().Contain("CT:true");
    }

    private static void Pump(JintScriptSession session)
    {
        for (var i = 0; i < 200 && session.PumpOnce(); i++) { }
    }

    private static global::Jint.Engine Bare()
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

    private static JintScriptSession Session(List<string> logs)
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body></body></html>");
        var url = global::Starling.Url.UrlParser.Parse("https://example.com/").Value;
        var http = new Starling.Net.StarlingHttpClient();
        var options = new Starling.Js.Hosting.ScriptSessionOptions(
            Document: doc,
            BaseUrl: url,
            Fetcher: (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null),
            Http: http,
            LayoutHost: null,
            LoggerFactory: NullLoggerFactory.Instance);
        return new JintScriptSession(options) { ConsoleSink = (_, msg) => logs.Add(msg) };
    }
}
