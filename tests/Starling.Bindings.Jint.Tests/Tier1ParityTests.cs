using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 1 parity fixes (tasks/jint/PARITY_GAPS.md): prototype routing for
/// CharacterData/Comment/Fragment nodes, a real DOMException, event subtype
/// reflection + host-dispatch prototype selection, and live collections. Each
/// test asserts the behavior the Jint backend lacked before the fix, matching the
/// canonical Starling.Js backend.
/// </summary>
[TestClass]
public sealed class Tier1ParityTests
{
    private const string Html =
        "<!doctype html><html><head><title>Hi</title></head>" +
        "<body><div id='main' class='a b'><p class='lead'>one</p><p>two</p>" +
        "<span id='s' name='widget'>x</span></div></body></html>";

    // ---- Fix 1: SelectPrototype routing -------------------------------------

    [TestMethod]
    public void TextNode_data_and_length_are_reachable()
    {
        // Before the routing fix, a Text node wrapped against Node.prototype, so
        // `.data`/`.length` (defined on CharacterData.prototype) read undefined.
        var (engine, _) = NewSession(Html);
        engine.Evaluate("document.createTextNode('hello').data").AsString().Should().Be("hello");
        engine.Evaluate("document.createTextNode('hello').length").AsNumber().Should().Be(5);
        // A Text node already in the tree, too.
        engine.Evaluate("document.querySelector('.lead').firstChild.data").AsString().Should().Be("one");
    }

    [TestMethod]
    public void Node_subtypes_resolve_instanceof()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("document.createTextNode('x') instanceof Text").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.createTextNode('x') instanceof CharacterData").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.createTextNode('x') instanceof Node").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.createComment('c') instanceof Comment").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.createComment('c').data").AsString().Should().Be("c");
        engine.Evaluate("document.createDocumentFragment() instanceof DocumentFragment").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.createDocumentFragment() instanceof Node").AsBoolean().Should().BeTrue();
    }

    // ---- Fix 2: DOMException ------------------------------------------------

    [TestMethod]
    public void DomException_is_constructible_with_name_message_code()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("new DOMException('boom', 'AbortError').name").AsString().Should().Be("AbortError");
        engine.Evaluate("new DOMException('boom', 'AbortError').message").AsString().Should().Be("boom");
        engine.Evaluate("new DOMException('boom', 'AbortError').code").AsNumber().Should().Be(20);
        engine.Evaluate("new DOMException('x', 'NotFoundError').code").AsNumber().Should().Be(8);
        // Unknown name → code 0; default name is "Error".
        engine.Evaluate("new DOMException('x').name").AsString().Should().Be("Error");
        engine.Evaluate("new DOMException('x', 'Whatever').code").AsNumber().Should().Be(0);
    }

    [TestMethod]
    public void DomException_instanceof_constants_and_toString()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("new DOMException() instanceof DOMException").AsBoolean().Should().BeTrue();
        engine.Evaluate("DOMException.ABORT_ERR").AsNumber().Should().Be(20);
        engine.Evaluate("DOMException.NOT_FOUND_ERR").AsNumber().Should().Be(8);
        engine.Evaluate("new DOMException('m', 'NotFoundError').INDEX_SIZE_ERR").AsNumber().Should().Be(1);
        engine.Evaluate("String(new DOMException('m', 'NotFoundError'))").AsString().Should().Be("NotFoundError: m");
    }

    // ---- Fix 3: Event subtype reflection + dispatch -------------------------

    [TestMethod]
    public void MouseEvent_reflects_init_and_chains_instanceof()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("new MouseEvent('click', {clientX: 5, clientY: 7, button: 1}).clientX").AsNumber().Should().Be(5);
        engine.Evaluate("new MouseEvent('click', {clientX: 5, clientY: 7}).clientY").AsNumber().Should().Be(7);
        engine.Evaluate("new MouseEvent('click', {button: 2}).button").AsNumber().Should().Be(2);
        engine.Evaluate("new MouseEvent('click', {ctrlKey: true}).ctrlKey").AsBoolean().Should().BeTrue();
        engine.Evaluate("new MouseEvent('click') instanceof MouseEvent").AsBoolean().Should().BeTrue();
        engine.Evaluate("new MouseEvent('click') instanceof UIEvent").AsBoolean().Should().BeTrue();
        engine.Evaluate("new MouseEvent('click') instanceof Event").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void KeyboardEvent_reflects_key_and_code()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("new KeyboardEvent('keydown', {key: 'Enter', code: 'Enter'}).key").AsString().Should().Be("Enter");
        engine.Evaluate("new KeyboardEvent('keydown', {code: 'KeyA'}).code").AsString().Should().Be("KeyA");
        engine.Evaluate("new KeyboardEvent('keydown', {keyCode: 13}).keyCode").AsNumber().Should().Be(13);
    }

    [TestMethod]
    public void Dispatched_event_keeps_subtype_prototype_and_properties()
    {
        // The listener must see a real MouseEvent with the dispatched clientX,
        // not a bare Event with clientX === undefined.
        var (engine, _) = NewSession(Html);
        var script = """
            (function(){
              var el = document.body;
              var seen = { x: -1, mouse: false, evt: false };
              el.addEventListener('click', function(e){
                seen.x = e.clientX; seen.mouse = (e instanceof MouseEvent); seen.evt = (e instanceof Event);
              });
              el.dispatchEvent(new MouseEvent('click', { clientX: 42, bubbles: true }));
              return seen.x + '|' + seen.mouse + '|' + seen.evt;
            })()
            """;
        engine.Evaluate(script).AsString().Should().Be("42|true|true");
    }

    // ---- Fix 4: live collections --------------------------------------------

    [TestMethod]
    public void ChildNodes_is_a_NodeList_with_item_and_iteration()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("document.querySelector('#main').childNodes instanceof NodeList").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.querySelector('#main').children instanceof HTMLCollection").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.querySelector('#main').children.item(0).tagName").AsString().Should().Be("P");
        engine.Evaluate("document.querySelector('#main').children[1].tagName").AsString().Should().Be("P");
        // for-of over an HTMLCollection (it has @@iterator).
        engine.Evaluate("(function(){var n=0; for (const c of document.querySelector('#main').children) n++; return n;})()")
            .AsNumber().Should().Be(3);
        // forEach is a NodeList method (HTMLCollection has none, matching browsers).
        engine.Evaluate("(function(){var n=0; document.querySelector('#main').childNodes.forEach(function(){n++;}); return n;})()")
            .AsNumber().Should().Be(3);
        engine.Evaluate("typeof document.querySelector('#main').children.forEach").AsString().Should().Be("undefined");
    }

    [TestMethod]
    public void HtmlCollection_named_access_and_namedItem()
    {
        var (engine, _) = NewSession(Html);
        // Named access by id.
        engine.Evaluate("document.querySelector('#main').children.s.tagName").AsString().Should().Be("SPAN");
        engine.Evaluate("document.querySelector('#main').children.namedItem('s').id").AsString().Should().Be("s");
        // Named access by the name attribute of an HTML element.
        engine.Evaluate("document.querySelector('#main').children.namedItem('widget').id").AsString().Should().Be("s");
    }

    [TestMethod]
    public void QuerySelectorAll_is_a_static_NodeList()
    {
        var (engine, doc) = NewSession(Html);
        engine.Evaluate("document.querySelectorAll('#main p') instanceof NodeList").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.querySelectorAll('#main p').length").AsNumber().Should().Be(2);
        engine.Evaluate("document.querySelectorAll('#main p')[0].textContent").AsString().Should().Be("one");
        engine.Evaluate("document.querySelectorAll('#main p').item(1).textContent").AsString().Should().Be("two");
    }

    [TestMethod]
    public void ChildNodes_is_live()
    {
        var (engine, _) = NewSession(Html);
        // A NodeList reference reflects later tree mutations (liveness).
        var script = """
            (function(){
              var list = document.querySelector('#main').childNodes;
              var before = list.length;
              document.querySelector('#main').appendChild(document.createElement('b'));
              return (list.length - before);
            })()
            """;
        engine.Evaluate(script).AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void ClassList_is_indexable_and_iterable()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("document.querySelector('#main').classList[0]").AsString().Should().Be("a");
        engine.Evaluate("document.querySelector('#main').classList[1]").AsString().Should().Be("b");
        engine.Evaluate("(function(){var s=''; for (const t of document.querySelector('#main').classList) s+=t; return s;})()")
            .AsString().Should().Be("ab");
        engine.Evaluate("document.querySelector('#main').classList instanceof DOMTokenList").AsBoolean().Should().BeTrue();
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession(string html)
    {
        var doc = HtmlParser.Parse(html);
        var baseUrl = global::Starling.Url.UrlParser.Parse("about:blank").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine: engine,
            document: doc,
            baseUrl: baseUrl,
            http: http,
            loggerFactory: NullLoggerFactory.Instance,
            loop: new WebEventLoop(),
            layoutHost: null,
            fetch: (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return (engine, doc);
    }
}
