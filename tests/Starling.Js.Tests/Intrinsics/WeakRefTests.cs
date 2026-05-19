using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>WeakRef</c> (B4-6).</summary>
/// <remarks>
/// We deliberately skip the GC-timing test ("after dropping the only strong
/// reference + forced GC, deref returns undefined"). That behavior is
/// observable in principle but flaky to assert deterministically from
/// .NET — finalizers + WeakReference reclamation race the test thread. A
/// follow-up CL can add a GC-isolated fixture; for now we cover the
/// constructor + method surface and the "kept alive" pin while the target
/// is reachable.
/// </remarks>
public class WeakRefTests
{
    [Fact]
    public void WeakRef_constructor_wired()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("WeakRef").IsObject.Should().BeTrue();
        rt.Realm.WeakRefConstructor.Should().NotBeNull();
    }

    [Fact]
    public void Constructing_with_null_throws_TypeError()
    {
        Action act = () => Eval("new WeakRef(null);");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Constructing_with_primitive_throws_TypeError()
    {
        Action act = () => Eval("new WeakRef(42);");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [Fact]
    public void Deref_returns_target_while_alive()
    {
        Eval(@"
            var o = {};
            var r = new WeakRef(o);
            r.deref() === o;").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Repeated_deref_returns_same_object()
    {
        Eval(@"
            var o = {};
            var r = new WeakRef(o);
            r.deref() === r.deref();").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Deref_on_wrong_receiver_throws_TypeError()
    {
        Action act = () => Eval("WeakRef.prototype.deref.call({});");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [Fact]
    public void ToStringTag_property_is_WeakRef()
    {
        // Object.prototype.toString doesn't yet consult @@toStringTag (B3-1
        // follow-up), so we just verify the symbol property is wired.
        Eval(@"
            var r = new WeakRef({});
            r[Symbol.toStringTag];").AsString.Should().Be("WeakRef");
    }

    // TODO(B4-6 follow-up): GC-timing test under a dedicated fixture that
    // can force generation-2 collections + drain the finalization callback.
    // .NET's WeakReference reclamation is non-deterministic enough that an
    // inline test would be flaky.

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
