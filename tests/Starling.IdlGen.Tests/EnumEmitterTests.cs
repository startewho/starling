using AwesomeAssertions;
using Starling.IdlGen.Emit;
using Starling.IdlGen.Merging;
using Starling.IdlGen.Parsing;

namespace Starling.IdlGen.Tests;

[TestClass]
public class EnumEmitterTests
{
    private static string Emit(string idl, out int count)
    {
        var model = IdlMerger.Merge([IdlParser.Parse(idl)]);
        return new EnumEmitter(model).Emit(["I"], out count);
    }

    [TestMethod]
    public void Emits_enum_and_wire_map()
    {
        string code = Emit(
            "enum Mode { \"open\", \"closed\" }; interface I { undefined f(Mode m); };", out int n);
        n.Should().Be(1);
        code.Should().Contain("public enum Mode");
        code.Should().Contain("Open,");
        code.Should().Contain("Closed,");
        code.Should().Contain("Mode.Open => \"open\"");
        code.Should().Contain("\"closed\" => Mode.Closed");
    }

    [TestMethod]
    public void Sanitizes_kebab_and_digit_and_empty_values()
    {
        string code = Emit(
            "enum E { \"low-power\", \"2d\", \"\" }; interface I { undefined f(E e); };", out _);
        code.Should().Contain("LowPower,");   // kebab -> Pascal
        code.Should().Contain("_2d,");        // leading digit prefixed
        code.Should().Contain("None,");       // empty value
        code.Should().Contain("LowPower => \"low-power\"");
    }

    [TestMethod]
    public void Only_emits_enums_used_by_target_interfaces()
    {
        // Unused enum is not emitted.
        string code = Emit("enum Unused { \"x\" }; interface I { undefined f(long n); };", out int n);
        n.Should().Be(0);
        code.Should().NotContain("Unused");
    }
}
