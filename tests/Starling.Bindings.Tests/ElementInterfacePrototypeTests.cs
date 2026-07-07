using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// Every element wrapper must carry its real per-interface prototype chain
/// (HTMLDivElement.prototype → HTMLElement.prototype → Element.prototype →
/// Node.prototype → EventTarget.prototype), so idlharness-style checks —
/// Object.getPrototypeOf, .constructor identity, and native instanceof — hold
/// structurally rather than through a faked @@hasInstance.
/// </summary>
[TestClass]
[Spec("html", "https://html.spec.whatwg.org/multipage/dom.html#htmlelement", "4")]
public sealed class ElementInterfacePrototypeTests
{
    [TestMethod]
    public void Div_prototype_is_HTMLDivElement_prototype()
    {
        Eval("""
            var d = document.createElement('div');
            result = Object.getPrototypeOf(d) === HTMLDivElement.prototype;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Div_prototype_chain_reaches_EventTarget()
    {
        Eval("""
            var d = document.createElement('div');
            var p = Object.getPrototypeOf(d);
            result =
                p === HTMLDivElement.prototype &&
                (p = Object.getPrototypeOf(p)) === HTMLElement.prototype &&
                (p = Object.getPrototypeOf(p)) === Element.prototype &&
                (p = Object.getPrototypeOf(p)) === Node.prototype &&
                (p = Object.getPrototypeOf(p)) === EventTarget.prototype;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Div_constructor_is_HTMLDivElement()
    {
        Eval("""
            var d = document.createElement('div');
            result = d.constructor === HTMLDivElement;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Div_is_instanceof_whole_chain()
    {
        Eval("""
            var d = document.createElement('div');
            result =
                (d instanceof HTMLDivElement) &&
                (d instanceof HTMLElement) &&
                (d instanceof Element) &&
                (d instanceof Node) &&
                (d instanceof EventTarget);
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    [DataRow("a", "HTMLAnchorElement")]
    [DataRow("input", "HTMLInputElement")]
    [DataRow("span", "HTMLSpanElement")]
    [DataRow("br", "HTMLBRElement")]
    public void Known_tags_map_to_their_interface(string tag, string ctorName)
    {
        Eval($$"""
            var e = document.createElement('{{tag}}');
            result =
                Object.getPrototypeOf(e) === {{ctorName}}.prototype &&
                (e instanceof {{ctorName}}) &&
                (e instanceof HTMLElement);
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Unknown_tag_maps_to_HTMLUnknownElement()
    {
        Eval("""
            var e = document.createElement('foobar');
            result =
                Object.getPrototypeOf(e) === HTMLUnknownElement.prototype &&
                (e instanceof HTMLUnknownElement) &&
                (e instanceof HTMLElement) &&
                !(e instanceof HTMLDivElement);
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Base_interface_tag_uses_HTMLElement_directly()
    {
        Eval("""
            var e = document.createElement('section');
            result =
                Object.getPrototypeOf(e) === HTMLElement.prototype &&
                (e instanceof HTMLElement) &&
                !(e instanceof HTMLUnknownElement);
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Element_prototype_member_reachable_from_div()
    {
        Eval("""
            var d = document.createElement('div');
            result =
                typeof d.getAttribute === 'function' &&
                d.getAttribute === Element.prototype.getAttribute;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Media_hierarchy_chains_through_HTMLMediaElement()
    {
        Eval("""
            var a = document.createElement('audio');
            var v = document.createElement('video');
            result =
                Object.getPrototypeOf(a) === HTMLAudioElement.prototype &&
                (a instanceof HTMLAudioElement) &&
                (a instanceof HTMLMediaElement) &&
                Object.getPrototypeOf(v) === HTMLVideoElement.prototype &&
                (v instanceof HTMLVideoElement) &&
                (v instanceof HTMLMediaElement) &&
                Object.getPrototypeOf(HTMLAudioElement.prototype) === HTMLMediaElement.prototype;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Same_node_keeps_wrapper_identity_and_prototype()
    {
        Eval("""
            document.body.appendChild(document.createElement('div'));
            var first = document.body.firstChild;
            var again = document.body.firstChild;
            result = first === again && Object.getPrototypeOf(first) === HTMLDivElement.prototype;
        """).AsBool.Should().BeTrue();
    }

    // ---- Helpers ------------------------------------------------------------

    private static JsValue Eval(string source)
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
