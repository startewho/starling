using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

/// <summary>
/// CSSOM stylesheet subsystem (CSSOM §6): document.styleSheets →
/// CSSStyleSheet.cssRules → CSSStyleRule (.style / .selectorText / .cssText).
/// Mirrors the chain exercised by css/css-syntax WPT tests.
/// </summary>
[TestClass]
public sealed class CssomBindingTests
{
    private static (JsRuntime, Document) BuildEnv(string styleCss)
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var head = doc.CreateElement("head");
        var style = doc.CreateElement("style");
        style.AppendChild(doc.CreateTextNode(styleCss));
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(head);
        head.AppendChild(style);
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

    [TestMethod]
    public void StyleSheets_exposes_inline_style_element()
    {
        var (rt, _) = BuildEnv(".foo { color: red; }");
        Eval(rt, "result = document.styleSheets.length;").AsNumber.Should().Be(1);
        Eval(rt, "result = document.styleSheets[0].cssRules.length;").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void CssRules_exposes_style_rule_selector_and_style()
    {
        var (rt, _) = BuildEnv(".foo { color: red; }");
        Eval(rt, "result = document.styleSheets[0].cssRules[0].selectorText;")
            .AsString.Should().Be(".foo");
        Eval(rt, "result = document.styleSheets[0].cssRules[0].style.getPropertyValue('color');")
            .AsString.Should().Be("red");
    }

    [TestMethod]
    public void SetProperty_round_trips_via_getPropertyValue()
    {
        var (rt, _) = BuildEnv(".foo {}");
        Eval(rt, """
            var s = document.styleSheets[0].cssRules[0].style;
            s.setProperty("line-height", "1.0");
            result = s.getPropertyValue("line-height");
        """).AsString.Should().Be("1");
    }

    [TestMethod]
    public void SetProperty_rejects_invalid_decimal()
    {
        var (rt, _) = BuildEnv(".foo {}");
        Eval(rt, """
            var s = document.styleSheets[0].cssRules[0].style;
            s.setProperty("line-height", "0");
            s.setProperty("line-height", "1.");
            result = s.getPropertyValue("line-height");
        """).AsString.Should().Be("0");
    }

    [TestMethod]
    public void SelectorText_setter_round_trips_anb()
    {
        var (rt, _) = BuildEnv("foo { color: blue; }");
        Eval(rt, """
            var rule = document.styleSheets[0].cssRules[0];
            rule.selectorText = ":nth-child(odd)";
            result = rule.selectorText;
        """).AsString.Should().Be(":nth-child(2n+1)");
    }

    [TestMethod]
    public void SelectorText_setter_rejects_invalid_keeping_previous()
    {
        var (rt, _) = BuildEnv("foo { color: blue; }");
        Eval(rt, """
            var rule = document.styleSheets[0].cssRules[0];
            rule.selectorText = "foo";
            rule.selectorText = ":nth-child(+ n)";
            result = rule.selectorText;
        """).AsString.Should().Be("foo");
    }

    [TestMethod]
    public void Style_zIndex_camelcase_accessor_round_trips()
    {
        var (rt, _) = BuildEnv("foo { z-index: 0; }");
        Eval(rt, """
            var rule = document.styleSheets[0].cssRules[0];
            rule.style.zIndex = "12345";
            result = rule.style.zIndex;
        """).AsString.Should().Be("12345");
    }
}
