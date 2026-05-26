using AwesomeAssertions;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:M3-83 — section 9.3 cross-realm execution. When code enters a SECOND realm
/// — the Test262 $262.createRealm() pattern where host code holds the foreign
/// realm's global eval (or a function/class it produced) and invokes it while
/// the HOST realm's VM is the one on the native stack — the engine must
/// establish that foreign realm as the running execution context. Otherwise the
/// foreign realm has no <see cref="JsRealm.ActiveVm"/> and its eval throws
/// "eval requires an active execution context", and any function it returns
/// would resolve globals / allocate intrinsics / throw errors in the WRONG
/// realm. These tests model that by constructing two runtimes directly and
/// invoking the foreign one's machinery from the host's VM.
/// </summary>
[TestClass]
public class CrossRealmExecutionTests
{
    /// <summary>The foreign realm's eval, invoked from the host VM, resolves free
    /// identifiers against the FOREIGN global environment — not the host's. A
    /// binding only the foreign global has must be visible.</summary>
    [TestMethod]
    public void Foreign_realm_eval_resolves_against_foreign_global()
    {
        var host = new JsRuntime();
        var foreign = new JsRuntime();
        foreign.SetGlobal("x", JsValue.Number(1));

        // Host holds the foreign realm's eval function and calls it from the host
        // VM. This is exactly: other = $262.createRealm().global; other.eval('x').
        var foreignEval = foreign.GetGlobal("eval");
        var hostVm = new JsVm(host);
        var result = AbstractOperations.Call(hostVm, foreignEval, JsValue.Undefined,
            new[] { JsValue.String("x") });

        result.AsNumber.Should().Be(1, "the foreign realm resolves x against the foreign global");
    }

    /// <summary>A top-level binding the host global defines must NOT be visible to
    /// the foreign realm's eval (distinct global environments).</summary>
    [TestMethod]
    public void Host_global_does_not_leak_into_foreign_realm_eval()
    {
        var host = new JsRuntime();
        var foreign = new JsRuntime();
        host.SetGlobal("hostOnly", JsValue.Number(42));

        var foreignEval = foreign.GetGlobal("eval");
        var hostVm = new JsVm(host);
        var result = AbstractOperations.Call(hostVm, foreignEval, JsValue.Undefined,
            new[] { JsValue.String("typeof hostOnly") });

        result.AsString.Should().Be("undefined");
    }

    /// <summary>The foreign realm's intrinsics are DISTINCT objects from the
    /// host's: Object in realm B is not identical to Object in realm A.</summary>
    [TestMethod]
    public void Foreign_realm_intrinsics_are_distinct_from_host()
    {
        var host = new JsRuntime();
        var foreign = new JsRuntime();

        var hostObject = host.GetGlobal("Object");
        var foreignObject = foreign.GetGlobal("Object");

        hostObject.IsObject.Should().BeTrue();
        foreignObject.IsObject.Should().BeTrue();
        ReferenceEquals(hostObject.AsObject, foreignObject.AsObject).Should().BeFalse(
            "each realm gets its own Object intrinsic");
    }

    /// <summary>A function RETURNED from the foreign realm's eval runs with the
    /// FOREIGN realm active: when called from the host VM, the body resolves a
    /// free identifier against the foreign global.</summary>
    [TestMethod]
    public void Function_from_foreign_eval_runs_with_foreign_realm_active()
    {
        var host = new JsRuntime();
        var foreign = new JsRuntime();
        foreign.SetGlobal("secret", JsValue.Number(7));

        var foreignEval = foreign.GetGlobal("eval");
        var hostVm = new JsVm(host);
        var fn = AbstractOperations.Call(hostVm, foreignEval, JsValue.Undefined,
            new[] { JsValue.String("(function () { return secret; })") });

        fn.IsObject.Should().BeTrue();
        var result = AbstractOperations.Call(hostVm, fn, JsValue.Undefined, System.Array.Empty<JsValue>());
        result.AsNumber.Should().Be(7);
    }

    /// <summary>A class produced by the foreign realm's eval, when constructed
    /// from the host VM, throws errors from the FOREIGN realm: a private brand
    /// check on a wrong receiver throws the foreign realm's TypeError, not the
    /// host's — the core of the cross-realm brand-check Test262 cluster.</summary>
    [TestMethod]
    public void Foreign_class_brand_check_throws_foreign_realm_typeerror()
    {
        var host = new JsRuntime();
        var foreign = new JsRuntime();

        var foreignEval = foreign.GetGlobal("eval");
        var hostVm = new JsVm(host);
        const string classSrc = "(class { #m(){ return 1; } access(o){ return o.#m(); } })";
        var ctor = AbstractOperations.Call(hostVm, foreignEval, JsValue.Undefined,
            new[] { JsValue.String(classSrc) });

        var instance = AbstractOperations.Construct(hostVm, ctor, System.Array.Empty<JsValue>());
        var access = instance.AsObject.Get("access");
        AbstractOperations.Call(hostVm, access, instance, new[] { instance })
            .AsNumber.Should().Be(1);

        var threw = false;
        try
        {
            AbstractOperations.Call(hostVm, access, instance, new[] { JsValue.Object(host.Global) });
        }
        catch (JsThrow jt)
        {
            threw = true;
            jt.Value.IsObject.Should().BeTrue();
            var proto = jt.Value.AsObject.GetPrototypeOf();
            ReferenceEquals(proto, foreign.Realm.TypeErrorPrototype).Should().BeTrue(
                "the brand-check TypeError comes from the constructor's realm");
        }

        threw.Should().BeTrue("a wrong-receiver private brand check must throw");
    }
}
