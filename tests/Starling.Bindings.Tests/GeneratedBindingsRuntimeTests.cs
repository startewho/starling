using AwesomeAssertions;
using Starling.Bindings.Generated;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

// Proves the generated binding glue actually works, not just that it compiles.
// The real bindings are installed first, then the generated installers overwrite
// the members they cover, so driving them from real JS exercises the generated
// accessors and methods against a real DOM.
[TestClass]
public sealed class GeneratedBindingsRuntimeTests
{
    [TestMethod]
    public void Generated_accessors_and_methods_read_real_dom()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // id (generated getter), setAttribute / hasAttribute (generated methods),
        // and tagName via the generated OVERRIDE binding (HTML uppercasing).
        var result = Eval(runtime, """
            var e = document.createElement('div');
            e.setAttribute('id', 'foo');
            result = e.id + '/' + e.tagName + '/' + e.hasAttribute('id') + '/' + e.hasAttribute('missing');
        """);

        result.AsString.Should().Be("foo/DIV/true/false");
    }

    [TestMethod]
    public void Generated_node_wrapping_accessor_preserves_identity()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // firstChild is a generated node-wrapping accessor; it must return the
        // same JS wrapper as the child looked up directly.
        Eval(runtime, """
            var parent = document.createElement('div');
            var child = document.createElement('span');
            parent.appendChild(child);
            result = parent.firstChild === child;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Generated_string_setter_writes_through_to_dom()
    {
        var (runtime, doc) = BuildEnvWithGenerated();

        Eval(runtime, """
            var e = document.createElement('div');
            e.id = 'written';
            result = e.id;
        """).AsString.Should().Be("written");
    }

    [TestMethod]
    public void Generated_dotnet11_union_type_works()
    {
        // NodeOrString is a generated .NET 11 union type from IDL (Node or DOMString).
        // Implicit conversion in, then an exhaustive switch over the case types.
        Starling.Bindings.Generated.NodeOrString fromString = "hello";
        string s = fromString switch
        {
            Starling.Dom.Node => "node",
            string str => str,
        };
        s.Should().Be("hello");

        Starling.Bindings.Generated.NodeOrString fromNode = new Document();
        string n = fromNode switch
        {
            Starling.Dom.Node => "node",
            string str => str,
        };
        n.Should().Be("node");
    }

    private static (JsRuntime, Document) BuildEnvWithGenerated()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);

        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());

        // Overwrite the covered prototype members with the generated versions.
        CoreDomBindingsGenerated.InstallNode(runtime.Realm);
        CoreDomBindingsGenerated.InstallElement(runtime.Realm);
        CoreDomBindingsGenerated.InstallDocument(runtime.Realm);
        CoreDomBindingsGenerated.InstallCharacterData(runtime.Realm);

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
