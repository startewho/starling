using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

/// <summary>
/// Editable form-control JS surface: the <c>value</c> IDL attribute (live
/// value with content-attribute fallback), <c>document.activeElement</c>, and
/// <c>element.focus()</c>/<c>.blur()</c> — the script-visible half of
/// click-to-type.
/// </summary>
[TestClass]
public sealed class InputValueBindingTests
{
    [TestMethod]
    public void Input_value_reads_attribute_then_live_value()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var i = document.createElement('input');
            i.setAttribute('value', 'init');
            var before = i.value;          // content attribute is the initial value
            i.value = 'typed';             // assignment sets the live value
            result = before + '|' + i.value;
        """).AsString.Should().Be("init|typed");
    }

    [TestMethod]
    public void Setting_value_in_js_updates_the_dom_input_value()
    {
        var (runtime, doc) = BuildEnv();
        Eval(runtime, """
            var i = document.createElement('input');
            i.id = 'box';
            document.body.appendChild(i);
            i.value = 'hello';
        """);
        doc.GetElementById("box")!.InputValue.Should().Be("hello");
    }

    [TestMethod]
    public void ActiveElement_defaults_to_body_and_tracks_focus_and_blur()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var i = document.createElement('input');
            document.body.appendChild(i);
            var atStart = document.activeElement === document.body;
            i.focus();
            var afterFocus = document.activeElement === i;
            i.blur();
            var afterBlur = document.activeElement === document.body;
            result = atStart && afterFocus && afterBlur;
        """).AsBool.Should().BeTrue();
    }

    // ---------------------------------------------------------------- helpers

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return (runtime, doc);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
