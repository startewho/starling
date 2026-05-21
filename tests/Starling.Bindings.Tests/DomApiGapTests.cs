using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// wp:M4-01 — API gap tests for McMaster bundle surface.
/// Covers: classList, cloneNode, before/after/prepend/append, replaceWith,
/// innerText, element.style (inline), dataset, compatMode, hidden,
/// visibilityState, CustomEvent, navigator extensions, window.scrollX/Y.
/// </summary>
[TestClass]
[Spec("dom", "https://dom.spec.whatwg.org/", "Multiple")]
public sealed class DomApiGapTests
{
    // ---- element.classList --------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_add_contains_and_class_attribute_round_trip()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            e.classList.add('foo');
            e.classList.add('bar');
            result = e.classList.contains('foo') && e.classList.contains('bar')
                  && e.getAttribute('class') === 'foo bar';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_remove_clears_token()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            e.classList.add('a');
            e.classList.add('b');
            e.classList.remove('a');
            result = e.classList.contains('a');
        """).AsBool.Should().BeFalse();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_toggle_adds_and_removes_alternately()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('span');
            var a = e.classList.toggle('x');
            var b = e.classList.toggle('x');
            result = a === true && b === false;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_toggle_with_force_controls_presence()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            e.classList.add('x');
            var r1 = e.classList.toggle('x', true);
            var r2 = e.classList.toggle('y', false);
            result = r1 === true && r2 === false
                  && e.classList.contains('x') && !e.classList.contains('y');
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_replace_swaps_tokens()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            e.classList.add('old');
            var ok = e.classList.replace('old', 'new');
            result = ok === true && e.classList.contains('new') && !e.classList.contains('old');
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_replace_returns_false_when_token_absent()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            result = e.classList.replace('missing', 'new');
        """).AsBool.Should().BeFalse();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_length_and_item_work()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            e.className = 'a b c';
            result = e.classList.length === 3 && e.classList.item(1) === 'b';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_value_getter_returns_class_attribute()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('p');
            e.className = 'one two';
            result = e.classList.value;
        """).AsString.Should().Be("one two");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#domtokenlist", "DOMTokenList")]
    public void ClassList_identity_is_stable()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            result = e.classList === e.classList;
        """).AsBool.Should().BeTrue();
    }

    // ---- element.cloneNode --------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#concept-node-clone", "cloneNode")]
    public void CloneNode_shallow_copies_tag_and_attributes()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var src = document.createElement('div');
            src.setAttribute('id', 'orig');
            src.textContent = 'hello';
            var clone = src.cloneNode(false);
            result = clone.tagName === 'DIV'
                  && clone.getAttribute('id') === 'orig'
                  && clone.childNodes.length === 0;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#concept-node-clone", "cloneNode")]
    public void CloneNode_deep_copies_subtree()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var src = document.createElement('ul');
            var li = document.createElement('li');
            li.textContent = 'item';
            src.appendChild(li);
            var clone = src.cloneNode(true);
            result = clone.childNodes.length === 1
                  && clone.childNodes[0].tagName === 'LI'
                  && clone.childNodes[0].textContent === 'item';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#concept-node-clone", "cloneNode")]
    public void CloneNode_clone_is_distinct_reference()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('p');
            var clone = e.cloneNode(false);
            result = clone !== e;
        """).AsBool.Should().BeTrue();
    }

    // ---- element.before / after / prepend / append --------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-parentnode-append", "ParentNode.append")]
    public void Append_adds_elements_at_end()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var parent = document.createElement('div');
            var c1 = document.createElement('span');
            var c2 = document.createElement('p');
            parent.append(c1, c2);
            result = parent.childNodes.length === 2
                  && parent.childNodes[0].tagName === 'SPAN'
                  && parent.childNodes[1].tagName === 'P';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-parentnode-append", "ParentNode.append")]
    public void Append_string_arg_becomes_text_node()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var p = document.createElement('p');
            p.append('hello');
            result = p.textContent === 'hello';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-parentnode-prepend", "ParentNode.prepend")]
    public void Prepend_inserts_at_front()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var parent = document.createElement('div');
            var existing = document.createElement('b');
            parent.appendChild(existing);
            var first = document.createElement('em');
            parent.prepend(first);
            result = parent.firstElementChild.tagName === 'EM';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-childnode-before", "ChildNode.before")]
    public void Before_inserts_before_this_node()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var parent = document.createElement('div');
            var b = document.createElement('b');
            parent.appendChild(b);
            var em = document.createElement('em');
            b.before(em);
            result = parent.firstElementChild.tagName === 'EM'
                  && parent.lastElementChild.tagName === 'B';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-childnode-after", "ChildNode.after")]
    public void After_inserts_after_this_node()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var parent = document.createElement('div');
            var em = document.createElement('em');
            parent.appendChild(em);
            var b = document.createElement('b');
            em.after(b);
            result = parent.firstElementChild.tagName === 'EM'
                  && parent.lastElementChild.tagName === 'B';
        """).AsBool.Should().BeTrue();
    }

    // ---- element.replaceWith ------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-childnode-replacewith", "ChildNode.replaceWith")]
    public void ReplaceWith_swaps_node_in_parent()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var parent = document.createElement('div');
            var old = document.createElement('span');
            var replacement = document.createElement('p');
            parent.appendChild(old);
            old.replaceWith(replacement);
            result = parent.childNodes.length === 1
                  && parent.firstElementChild.tagName === 'P';
        """).AsBool.Should().BeTrue();
    }

    // ---- element.innerText --------------------------------------------------

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/dom.html#the-innertext-idl-attribute", "innerText")]
    public void InnerText_getter_returns_text_content()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var p = document.createElement('p');
            p.textContent = 'hello world';
            result = p.innerText;
        """).AsString.Should().Be("hello world");
    }

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/dom.html#the-innertext-idl-attribute", "innerText")]
    public void InnerText_setter_replaces_content()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var p = document.createElement('p');
            p.innerText = 'set via innerText';
            result = p.textContent;
        """).AsString.Should().Be("set via innerText");
    }

    // ---- element.style (inline CSSStyleDeclaration) -------------------------

    [SpecFact]
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface", "CSSStyleDeclaration")]
    public void Style_set_and_get_display_property()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.style.display = 'none';
            result = el.style.display;
        """).AsString.Should().Be("none");
    }

    [SpecFact]
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface", "CSSStyleDeclaration")]
    public void Style_setProperty_getPropertyValue_round_trip()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.style.setProperty('color', 'red');
            result = el.style.getPropertyValue('color');
        """).AsString.Should().Be("red");
    }

    [SpecFact]
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface", "CSSStyleDeclaration")]
    public void Style_cssText_setter_then_display_readable()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('p');
            el.style.cssText = 'display: flex; color: blue';
            result = el.style.display;
        """).AsString.Should().Be("flex");
    }

    [SpecFact]
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface", "CSSStyleDeclaration")]
    public void Style_removeProperty_clears_value()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.style.display = 'none';
            el.style.removeProperty('display');
            result = el.style.display;
        """).AsString.Should().BeEmpty();
    }

    [SpecFact]
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface", "CSSStyleDeclaration")]
    public void Style_identity_is_stable()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            result = el.style === el.style;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface", "CSSStyleDeclaration")]
    public void Style_camelCase_accessor_maps_to_kebab_property()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.style.backgroundClip = 'text';
            result = el.style.backgroundClip === 'text';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("cssom-1", "https://drafts.csswg.org/cssom/#the-cssstyledeclaration-interface", "CSSStyleDeclaration")]
    public void Style_updates_style_attribute_on_element()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.style.display = 'block';
            result = el.hasAttribute('style');
        """).AsBool.Should().BeTrue();
    }

    // ---- element.dataset ----------------------------------------------------

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/dom.html#domstringmap", "DOMStringMap")]
    public void Dataset_reads_data_attribute()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.setAttribute('data-foo-bar', 'baz');
            result = el.dataset.fooBar;
        """).AsString.Should().Be("baz");
    }

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/dom.html#domstringmap", "DOMStringMap")]
    public void Dataset_write_creates_data_attribute()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            el.dataset.myKey = 'val';
            result = el.getAttribute('data-my-key');
        """).AsString.Should().Be("val");
    }

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/dom.html#domstringmap", "DOMStringMap")]
    public void Dataset_identity_is_stable()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var el = document.createElement('div');
            result = el.dataset === el.dataset;
        """).AsBool.Should().BeTrue();
    }

    // ---- document.compatMode ------------------------------------------------

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/dom.html#dom-document-compatmode", "compatMode")]
    public void Document_compatMode_is_CSS1Compat_in_no_quirks()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.compatMode;")
            .AsString.Should().Be("CSS1Compat");
    }

    // ---- document.hidden / visibilityState ----------------------------------

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/interaction.html#page-visibility", "Page Visibility")]
    public void Document_hidden_is_false()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.hidden;")
            .AsBool.Should().BeFalse();
    }

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/interaction.html#page-visibility", "Page Visibility")]
    public void Document_visibilityState_is_visible()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.visibilityState;")
            .AsString.Should().Be("visible");
    }

    // ---- CustomEvent --------------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-customevent", "CustomEvent")]
    public void CustomEvent_constructor_creates_event_with_detail()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var ev = new CustomEvent('my-event', { detail: { x: 42 } });
            result = ev.type === 'my-event' && ev.detail.x === 42;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-customevent", "CustomEvent")]
    public void CustomEvent_can_be_dispatched_and_received()
    {
        var (runtime, _) = BuildEnv();
        var errors = new List<string>();
        runtime.Realm.ConsoleSink = (lvl, msg) => errors.Add($"{lvl}: {msg}");
        Eval(runtime, """
            var target = document.createElement('div');
            document.body.appendChild(target);
            var fired = 0;
            var receivedDetail = -1;
            var ev99 = new CustomEvent('custom', { detail: 99 });
            target.addEventListener('custom', function (e) { fired = 1; receivedDetail = e.detail; });
            target.dispatchEvent(ev99);
            firedResult = fired;
            detailResult = receivedDetail;
        """);
        errors.Should().BeEmpty("no uncaught errors");
        runtime.GetGlobal("firedResult").AsNumber.Should().Be(1, "listener should have fired");
        runtime.GetGlobal("detailResult").AsNumber.Should().Be(99);
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-customevent", "CustomEvent")]
    public void CustomEvent_bubbles_and_cancelable_from_init()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var ev = new CustomEvent('x', { bubbles: true, cancelable: true });
            result = ev.bubbles && ev.cancelable;
        """).AsBool.Should().BeTrue();
    }

    // ---- navigator extensions -----------------------------------------------

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-languages", "navigator.languages")]
    public void Navigator_languages_starts_with_en_US()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = navigator.languages[0];")
            .AsString.Should().Be("en-US");
    }

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/system-state.html#dom-navigator-cookieenabled", "navigator.cookieEnabled")]
    public void Navigator_cookieEnabled_is_true()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = navigator.cookieEnabled;")
            .AsBool.Should().BeTrue();
    }

    // ---- window.scrollX / scrollY -------------------------------------------

    [SpecFact]
    [Spec("html", "https://html.spec.whatwg.org/multipage/nav-history-apis.html#dom-window-scrollx", "scrollX/Y")]
    public void Window_scrollX_and_scrollY_default_to_zero()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            result = window.scrollX === 0 && window.scrollY === 0
                  && window.pageXOffset === 0 && window.pageYOffset === 0;
        """).AsBool.Should().BeTrue();
    }

    // ---- Node.normalize -----------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-node-normalize", "Node.normalize")]
    public void Normalize_merges_adjacent_text_nodes()
    {
        var (runtime, doc) = BuildEnv();
        var parent = doc.CreateElement("div");
        parent.AppendChild(doc.CreateTextNode("hello"));
        parent.AppendChild(doc.CreateTextNode(" world"));
        doc.Body!.AppendChild(parent);

        Eval(runtime, """
            var parent = document.body.firstChild;
            parent.normalize();
            result = parent.childNodes.length === 1 && parent.textContent === 'hello world';
        """).AsBool.Should().BeTrue();
    }

    // ---- Helpers ------------------------------------------------------------

    private static (JsRuntime, Document) BuildEnv(string? url = null)
    {
        var doc = BuildDocument();
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(DocumentUrl: url));
        return (runtime, doc);
    }

    private static Document BuildDocument()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        return doc;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
