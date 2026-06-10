using AwesomeAssertions;
using Starling.Bindings.Generated;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

// Negative Web IDL conversion tests for the generated bindings. Where
// GeneratedBindingsRuntimeTests proves the happy path, these prove the error
// paths through IdlMarshal: wrong receiver (illegal invocation), too few
// arguments, and arguments of the wrong interface type. Each assertion drives
// the generated accessor or method from real JS and checks the thrown JS error
// is a TypeError with the spec-shaped message.
[TestClass]
public sealed class GeneratedBindingsNegativeTests
{
    [TestMethod]
    public void Method_on_wrong_receiver_throws_illegal_invocation()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // hasAttribute is a generated Element method. Calling it with a plain
        // object receiver fails IdlMarshal.Receiver<Element>.
        Catch(runtime, """
            var e = document.createElement('div');
            e.hasAttribute.call({}, 'id');
        """).Should().Be("TypeError|Failed to execute 'hasAttribute' on 'Element': Illegal invocation");
    }

    [TestMethod]
    public void Node_method_on_wrong_receiver_throws_illegal_invocation()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // appendChild is a generated Node method; an unwrappable receiver throws.
        Catch(runtime, """
            var e = document.createElement('div');
            var child = document.createElement('span');
            e.appendChild.call(null, child);
        """).Should().Be("TypeError|Failed to execute 'appendChild' on 'Node': Illegal invocation");
    }

    [TestMethod]
    public void Method_with_too_few_arguments_throws_with_required_count()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // setAttribute requires 2 arguments (RequireString twice).
        Catch(runtime, """
            var e = document.createElement('div');
            e.setAttribute('id');
        """).Should().Be("TypeError|Failed to execute 'setAttribute': 2 arguments required, but only 1 present.");
    }

    [TestMethod]
    public void Method_with_no_arguments_throws_singular_required_count()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // appendChild requires 1 interface argument; the message is singular.
        Catch(runtime, """
            var e = document.createElement('div');
            e.appendChild();
        """).Should().Be("TypeError|Failed to execute 'appendChild': 1 argument required, but only 0 present.");
    }

    [TestMethod]
    public void Required_interface_argument_of_wrong_type_throws()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // appendChild takes a required Node; a string is not a Node.
        Catch(runtime, """
            var e = document.createElement('div');
            e.appendChild('not a node');
        """).Should().Be("TypeError|Failed to execute 'appendChild': parameter 1 is not of type 'Node'.");
    }

    [TestMethod]
    public void Required_interface_argument_object_is_not_a_node()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // compareDocumentPosition takes a required Node; a plain object fails.
        Catch(runtime, """
            var e = document.createElement('div');
            e.compareDocumentPosition({});
        """).Should().Be("TypeError|Failed to execute 'compareDocumentPosition': parameter 1 is not of type 'Node'.");
    }

    [TestMethod]
    public void Nullable_interface_argument_of_wrong_type_throws()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // isEqualNode takes a nullable Node? — null is allowed, but a non-null
        // value of the wrong type still throws.
        Catch(runtime, """
            var e = document.createElement('div');
            e.isEqualNode(123);
        """).Should().Be("TypeError|Failed to execute 'isEqualNode': parameter 1 is not of type 'Node'.");
    }

    [TestMethod]
    public void Nullable_interface_argument_accepts_null()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // The boundary case: null is a legal Node? and must not throw.
        Eval(runtime, """
            var e = document.createElement('div');
            result = e.isEqualNode(null);
        """).AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Insert_before_with_one_argument_throws_required_count()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // insertBefore(node, child?) requires 2 arguments even though the second
        // is a nullable Node?.
        Catch(runtime, """
            var parent = document.createElement('div');
            var child = document.createElement('span');
            parent.insertBefore(child);
        """).Should().Be("TypeError|Failed to execute 'insertBefore': 2 arguments required, but only 1 present.");
    }

    [TestMethod]
    public void Insert_before_accepts_null_reference_child()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // The nullable second argument: null means "append at the end".
        Eval(runtime, """
            var parent = document.createElement('div');
            var child = document.createElement('span');
            parent.insertBefore(child, null);
            result = parent.firstChild === child;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Unsigned_long_method_with_too_few_arguments_throws()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // replaceData requires 3 arguments; the count check runs before any
        // unsigned long conversion.
        Catch(runtime, """
            var t = document.createTextNode('hello');
            t.replaceData(0, 1);
        """).Should().Be("TypeError|Failed to execute 'replaceData': 3 arguments required, but only 2 present.");
    }

    [TestMethod]
    public void Dom_exception_from_generated_method_becomes_a_js_dom_exception()
    {
        var (runtime, _) = BuildEnvWithGenerated();

        // substringData with offset past the end raises IndexSizeError in the DOM
        // impl; the generated mechanical method must translate it to a JS
        // DOMException whose name is preserved.
        Eval(runtime, """
            var t = document.createTextNode('hi');
            try { t.substringData(5, 1); result = 'no-throw'; }
            catch (e) { result = e.constructor.name + '|' + e.name; }
        """).AsString.Should().Be("DOMException|IndexSizeError");
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
        CoreDomBindingsGenerated.InstallAll(runtime.Realm);

        return (runtime, doc);
    }

    // Run JS that is expected to throw and return "<error name>|<message>".
    private static string Catch(JsRuntime runtime, string body)
    {
        var source = $$"""
            try { {{body}} result = 'no-throw'; }
            catch (e) {
                result = ((e && e.constructor && e.constructor.name) || 'other')
                    + '|' + ((e && e.message) || '');
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
