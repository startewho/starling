using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

// The native-call boundary (AbstractOperations.CallNative) is the single choke
// point that turns a .NET exception escaping a native binding delegate into a
// catchable JS exception, so one throwing operation fails cleanly instead of
// unwinding a raw .NET exception out of the interpreter. These tests drive that
// boundary from real JS: a DOM-layer DomException becomes a JS DOMException with
// its spec name preserved, an arbitrary .NET exception becomes a JS TypeError,
// and the hand-written Node mutation bindings surface the right DOMException
// names now that the string-matching fallbacks are gone.
[TestClass]
public sealed class NativeExceptionTranslationTests
{
    [TestMethod]
    public void Dom_exception_from_native_delegate_becomes_js_dom_exception()
    {
        var (runtime, _) = BuildEnv();
        runtime.RegisterGlobal("boomDom", (_, _) =>
            throw DomException.Create("NotFoundError", "gone"));

        Catch(runtime, "boomDom();").Should().Be("DOMException|NotFoundError|gone");
    }

    [TestMethod]
    public void Arbitrary_dotnet_exception_from_native_delegate_becomes_type_error()
    {
        var (runtime, _) = BuildEnv();
        runtime.RegisterGlobal("boom", (_, _) =>
            throw new InvalidOperationException("kaboom"));

        Catch(runtime, "boom();").Should().Be("TypeError|kaboom");
    }

    [TestMethod]
    public void Remove_child_of_non_child_throws_not_found_error()
    {
        var (runtime, _) = BuildEnv();
        Catch(runtime, """
            var parent = document.createElement('div');
            var orphan = document.createElement('span');
            parent.removeChild(orphan);
        """).Should().StartWith("DOMException|NotFoundError");
    }

    [TestMethod]
    public void Insert_before_with_foreign_reference_child_throws_not_found_error()
    {
        var (runtime, _) = BuildEnv();
        Catch(runtime, """
            var parent = document.createElement('div');
            var child = document.createElement('span');
            var stranger = document.createElement('p');
            parent.insertBefore(child, stranger);
        """).Should().StartWith("DOMException|NotFoundError");
    }

    [TestMethod]
    public void Append_child_creating_a_cycle_throws_hierarchy_request_error()
    {
        var (runtime, _) = BuildEnv();
        Catch(runtime, """
            var parent = document.createElement('div');
            var child = document.createElement('span');
            parent.appendChild(child);
            child.appendChild(parent);
        """).Should().StartWith("DOMException|HierarchyRequestError");
    }

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

    // Run JS expected to throw; return "<constructor name>|<name-or-message...>".
    private static string Catch(JsRuntime runtime, string body)
    {
        var source = $$"""
            try { {{body}} result = 'no-throw'; }
            catch (e) {
                var ctor = (e && e.constructor && e.constructor.name) || 'other';
                result = e && e.name && e.name !== ctor
                    ? ctor + '|' + e.name + '|' + (e.message || '')
                    : ctor + '|' + ((e && e.message) || '');
            }
        """;
        return Eval(runtime, source).AsString;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
