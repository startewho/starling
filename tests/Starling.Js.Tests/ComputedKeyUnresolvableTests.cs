using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-53 — a computed property key is evaluated (GetValue, §6.2.5.5) at the
/// point the class / object is defined, so an unresolvable key reference throws
/// a ReferenceError and aborts the whole definition. Covers class instance and
/// static methods / getters / setters / fields plus object-literal accessors,
/// methods, and data properties; also pins that a resolvable computed key still
/// installs the member and is evaluated exactly once in source order.
/// </summary>
[TestClass]
public class ComputedKeyUnresolvableTests
{
    // ----------------------------------------------------- class members

    [TestMethod]
    public void Class_getter_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, class { get [test262unresolvable]() {} };").Should().Be("ReferenceError");

    [TestMethod]
    public void Class_setter_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, class { set [test262unresolvable](_) {} };").Should().Be("ReferenceError");

    [TestMethod]
    public void Class_method_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, class { [test262unresolvable]() {} };").Should().Be("ReferenceError");

    [TestMethod]
    public void Class_instance_field_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, class { [test262unresolvable] = 1; };").Should().Be("ReferenceError");

    [TestMethod]
    public void Class_static_field_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, class { static [test262unresolvable] = 1; };").Should().Be("ReferenceError");

    [TestMethod]
    public void Class_static_method_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, class { static [test262unresolvable]() {} };").Should().Be("ReferenceError");

    // ----------------------------------------------------- object literals

    [TestMethod]
    public void Object_getter_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, ({ get [test262unresolvable]() {} });").Should().Be("ReferenceError");

    [TestMethod]
    public void Object_setter_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, ({ set [test262unresolvable](_) {} });").Should().Be("ReferenceError");

    [TestMethod]
    public void Object_data_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, ({ [test262unresolvable]: 1 });").Should().Be("ReferenceError");

    [TestMethod]
    public void Object_method_unresolvable_computed_key_throws_reference_error()
        => EvalErrorName("0, ({ [test262unresolvable]() {} });").Should().Be("ReferenceError");

    // -------------------------------------- non-regression: resolvable keys

    [TestMethod]
    public void Resolvable_computed_key_still_defines_member()
    {
        Eval(@"
            const k = 'greet';
            class Foo { [k]() { return 'hi'; } }
            new Foo().greet();
        ").AsString.Should().Be("hi");
    }

    [TestMethod]
    public void Resolvable_computed_key_evaluated_exactly_once()
    {
        // The key expression is a bare identifier bound to a function call's
        // result via a getter-like side effect; evaluating it must happen
        // exactly once at definition time even though the member installs once.
        Eval(@"
            let calls = 0;
            function key() { calls++; return 'm'; }
            class Foo { [key()]() {} }
            calls;
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Resolvable_object_computed_key_still_defines_member()
    {
        Eval(@"
            const k = 'v';
            const o = { [k]: 42 };
            o.v;
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Typeof_unresolvable_identifier_does_not_throw()
    {
        // §13.5.1.2 — typeof on an unresolvable Reference returns "undefined";
        // the checked-load opcode used for computed keys must NOT leak into the
        // typeof path.
        Eval("typeof test262unresolvable;").AsString.Should().Be("undefined");
    }

    // ----------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    /// <summary>Run <paramref name="src"/> and return the <c>name</c> of the
    /// thrown JS error object (e.g. "ReferenceError"), or fail if nothing
    /// throws.</summary>
    private static string EvalErrorName(string src)
    {
        try
        {
            Eval(src);
        }
        catch (JsThrow ex)
        {
            ex.Value.IsObject.Should().BeTrue("a JS error object should be thrown");
            return ex.Value.AsObject.Get("name").AsString;
        }

        throw new AssertFailedException($"expected a thrown error but none was thrown for: {src}");
    }
}
