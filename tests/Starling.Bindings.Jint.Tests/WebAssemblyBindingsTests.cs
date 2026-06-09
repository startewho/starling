using AwesomeAssertions;
using Jint;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Tier 2 parity: WebAssembly JS API (Wasmtime-backed). Mirrors canonical against
/// Jint — surface presence, validate, Module/Instance with a numeric export,
/// Memory, and the error types.
/// </summary>
[TestClass]
public sealed class WebAssemblyBindingsTests
{
    // (module (func (export "add") (param i32 i32) (result i32)
    //          local.get 0 local.get 1 i32.add)
    //  (memory (export "mem") 1))
    private const string AddModuleWat = @"
        var bytes = new Uint8Array([
          0,97,115,109,1,0,0,0,
          1,7,1,96,2,127,127,1,127,
          3,2,1,0,
          5,3,1,0,1,
          7,13,2,3,97,100,100,0,0,3,109,101,109,2,0,
          10,9,1,7,0,32,0,32,1,106,11
        ]);";

    [TestMethod]
    public void WebAssembly_surface_is_present()
    {
        var (e, _) = NewSession();
        e.Evaluate("typeof WebAssembly").AsString().Should().Be("object");
        e.Evaluate("typeof WebAssembly.Module").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.Instance").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.Memory").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.Table").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.compile").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.instantiate").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.validate").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.CompileError").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.LinkError").AsString().Should().Be("function");
        e.Evaluate("typeof WebAssembly.RuntimeError").AsString().Should().Be("function");
    }

    [TestMethod]
    public void validate_accepts_valid_and_rejects_garbage()
    {
        var (e, _) = NewSession();
        e.Evaluate(AddModuleWat + "WebAssembly.validate(bytes)").AsBoolean().Should().BeTrue();
        e.Evaluate("WebAssembly.validate(new Uint8Array([1,2,3,4]))").AsBoolean().Should().BeFalse();
    }

    [TestMethod]
    public void Module_Instance_export_runs()
    {
        var (e, _) = NewSession();
        var js = AddModuleWat + """
            var mod = new WebAssembly.Module(bytes);
            var inst = new WebAssembly.Instance(mod, {});
            inst.exports.add(20, 22);
            """;
        e.Evaluate(js).AsNumber().Should().Be(42);
        e.Evaluate(AddModuleWat + "new WebAssembly.Module(bytes) instanceof WebAssembly.Module").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void instance_exports_memory_buffer()
    {
        var (e, _) = NewSession();
        var js = AddModuleWat + """
            var inst = new WebAssembly.Instance(new WebAssembly.Module(bytes), {});
            inst.exports.mem.buffer.byteLength;
            """;
        // memory min 1 page = 65536 bytes
        e.Evaluate(js).AsNumber().Should().Be(65536);
    }

    [TestMethod]
    public void Memory_constructor_and_grow()
    {
        var (e, _) = NewSession();
        var js = """
            var m = new WebAssembly.Memory({ initial: 1 });
            var before = m.buffer.byteLength;
            var old = m.grow(1);
            before + '|' + old + '|' + m.buffer.byteLength;
            """;
        e.Evaluate(js).AsString().Should().Be("65536|1|131072");
    }

    [TestMethod]
    public void CompileError_thrown_on_bad_module()
    {
        var (e, _) = NewSession();
        var js = """
            (function(){
              try { new WebAssembly.Module(new Uint8Array([0,1,2,3])); return 'no-throw'; }
              catch (x) { return (x instanceof WebAssembly.CompileError) ? 'CompileError' : ('other:' + x); }
            })()
            """;
        e.Evaluate(js).AsString().Should().Be("CompileError");
    }

    [TestMethod]
    public void error_types_are_constructible()
    {
        var (e, _) = NewSession();
        e.Evaluate("new WebAssembly.LinkError('m').message").AsString().Should().Be("m");
        e.Evaluate("new WebAssembly.RuntimeError('r') instanceof WebAssembly.RuntimeError").AsBoolean().Should().BeTrue();
        e.Evaluate("new WebAssembly.CompileError('c') instanceof Error").AsBoolean().Should().BeTrue();
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession()
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
        return (engine, doc);
    }
}
