using AwesomeAssertions;
using Starling.IdlGen.Emit;
using Starling.IdlGen.Mapping;
using Starling.IdlGen.Merging;
using Starling.IdlGen.Parsing;

namespace Starling.IdlGen.Tests;

[TestClass]
public class CallbackEmitterTests
{
    private static string Emit(string idl, out int count)
    {
        var model = IdlMerger.Merge([IdlParser.Parse(idl)]);
        return new CallbackEmitter(model, new TypeMapper(model), new ClrMap()).Emit(["I"], out count);
    }

    [TestMethod]
    public void Emits_delegate_with_void_return()
    {
        string code = Emit(
            "callback Done = undefined (boolean ok);" +
            "interface I { undefined f(Done d); };", out int n);
        n.Should().Be(1);
        code.Should().Contain("public delegate void Done(bool ok);");
    }

    [TestMethod]
    public void Complex_param_types_fall_back_to_js_value()
    {
        string code = Emit(
            "callback Cb = undefined (sequence<long> items);" +
            "interface I { undefined f(Cb c); };", out _);
        code.Should().Contain("public delegate void Cb(JsValue items);");
    }

    [TestMethod]
    public void Escapes_csharp_keyword_parameter_names()
    {
        string code = Emit(
            "callback Cb = undefined (boolean event);" +
            "interface I { undefined f(Cb c); };", out _);
        code.Should().Contain("bool @event");
    }

    [TestMethod]
    public void Found_through_dictionary_fields()
    {
        // A callback referenced only via a dictionary field is still emitted.
        string code = Emit(
            "callback Cb = undefined ();" +
            "dictionary Opts { Cb handler; };" +
            "interface I { undefined f(Opts o); };", out int n);
        n.Should().Be(1);
        code.Should().Contain("public delegate void Cb();");
    }
}
