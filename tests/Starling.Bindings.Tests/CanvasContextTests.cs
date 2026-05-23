using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

/// <summary>
/// HTMLCanvasElement.getContext("2d") — a minimal context for the text-metrics
/// path (font + measureText) that table libraries (@tanstack/table) use to size
/// columns. Full raster canvas is out of scope; this just keeps that path from
/// throwing "getContext is not a function" (which killed mcmaster.com's bundle).
/// </summary>
[TestClass]
public sealed class CanvasContextTests
{
    [TestMethod]
    public void Canvas_getContext_2d_returns_a_context()
    {
        Eval("""
            var c = document.createElement('canvas');
            result = c.getContext('2d') != null;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Context_measureText_returns_positive_width_scaling_with_length()
    {
        Eval("""
            var ctx = document.createElement('canvas').getContext('2d');
            ctx.font = '20px Arial';
            var a = ctx.measureText('x').width;
            var b = ctx.measureText('xxxx').width;
            result = a > 0 && b > a;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Context_font_round_trips()
    {
        Eval("""
            var ctx = document.createElement('canvas').getContext('2d');
            ctx.font = '13px Helvetica';
            result = ctx.font;
        """).AsString.Should().Be("13px Helvetica");
    }

    [TestMethod]
    public void NonCanvas_element_has_no_context()
    {
        Eval("""
            result = document.createElement('div').getContext('2d');
        """).IsNull.Should().BeTrue();
    }

    [TestMethod]
    public void Unknown_context_id_returns_null()
    {
        Eval("""
            result = document.createElement('canvas').getContext('webgl');
        """).IsNull.Should().BeTrue();
    }

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
        new JsVm(runtime).Run(JsCompiler.Compile(program));
        return runtime.GetGlobal("result");
    }
}
