using AwesomeAssertions;
using Starling.IdlGen.Emit;
using Starling.IdlGen.Mapping;
using Starling.IdlGen.Merging;
using Starling.IdlGen.Parsing;

namespace Starling.IdlGen.Tests;

[TestClass]
public class DictionaryEmitterTests
{
    private static string Emit(string idl, out int count)
    {
        var model = IdlMerger.Merge([IdlParser.Parse(idl)]);
        return new DictionaryEmitter(model, new TypeMapper(model), new ClrMap()).Emit(["I"], out count);
    }

    [TestMethod]
    public void Required_uses_required_modifier_optional_is_nullable()
    {
        string code = Emit(
            "dictionary Opts { required boolean must; double maybe; };" +
            "interface I { undefined f(Opts o); };", out int n);
        n.Should().Be(1);
        code.Should().Contain("public class Opts");
        code.Should().Contain("public required bool Must { get; init; }");
        code.Should().Contain("public double? Maybe { get; init; }");
    }

    [TestMethod]
    public void Inheritance_emits_base_class()
    {
        string code = Emit(
            "dictionary Base { boolean a; };" +
            "dictionary Derived : Base { boolean b; };" +
            "interface I { undefined f(Derived d); };", out _);
        code.Should().Contain("public class Derived : Base");
        code.Should().Contain("public class Base");
    }

    [TestMethod]
    public void Unknown_field_type_falls_back_to_js_value()
    {
        // A sequence-typed field is not modeled as a C# type yet, so it becomes
        // JsValue. (Callback and dictionary fields now get their generated types.)
        string code = Emit(
            "dictionary Opts { sequence<long> items; };" +
            "interface I { undefined f(Opts o); };", out _);
        code.Should().Contain("public JsValue? Items { get; init; }");
    }

    [TestMethod]
    public void Pulls_in_referenced_dictionaries_transitively()
    {
        string code = Emit(
            "dictionary Inner { boolean x; };" +
            "dictionary Outer { Inner nested; };" +
            "interface I { undefined f(Outer o); };", out int n);
        n.Should().Be(2);
        code.Should().Contain("public class Inner");
        code.Should().Contain("public Inner? Nested { get; init; }");
    }
}
