using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 4 parity: CharacterData mutation methods + Text.splitText/wholeText, and
/// Element.insertAdjacentElement/insertAdjacentText.
/// </summary>
[TestClass]
public sealed class CharacterDataAndAdjacentTests
{
    private const string Html = "<!doctype html><html><body><div id='d'>hello</div></body></html>";

    [TestMethod]
    public void characterData_mutation_methods()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('d').firstChild;
              var sub = t.substringData(1, 3);   // ell
              t.appendData('!');                  // hello!
              t.insertData(0, '>');               // >hello!
              t.deleteData(0, 1);                 // hello!
              t.replaceData(0, 5, 'HELLO');       // HELLO!
              return sub + '|' + t.data;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("ell|HELLO!");
    }

    [TestMethod]
    public void splitText_and_wholeText()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var t = document.getElementById('d').firstChild;
              var rest = t.splitText(2);          // 'he' | 'llo'
              return t.data + '|' + rest.data + '|' + (rest.previousSibling === t) + '|' + t.wholeText;
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("he|llo|true|hello");
    }

    [TestMethod]
    public void substringData_out_of_bounds_throws()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("(function(){ try { document.getElementById('d').firstChild.substringData(99,1); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("IndexSizeError");
    }

    [TestMethod]
    public void insertAdjacentElement_positions()
    {
        var (e, _) = NewSession(Html);
        var js = """
            (function(){
              var d = document.getElementById('d');
              var a = document.createElement('a'); a.id = 'bb';
              var b = document.createElement('b'); b.id = 'ae';
              d.insertAdjacentElement('beforebegin', a);
              d.insertAdjacentElement('afterend', b);
              return (d.previousSibling.id) + '|' + (d.nextSibling.id);
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("bb|ae");
    }

    [TestMethod]
    public void insertAdjacentText_and_bad_position()
    {
        var (e, _) = NewSession(Html);
        e.Evaluate("""
            (function(){
              var d = document.getElementById('d');
              d.insertAdjacentText('afterbegin', 'X');
              return d.firstChild.data;
            })()
            """).AsString().Should().Be("X");
        e.Evaluate("(function(){ try { document.getElementById('d').insertAdjacentText('nope', 'y'); return 'no'; } catch (x) { return x.name; } })()")
            .AsString().Should().Be("SyntaxError");
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
