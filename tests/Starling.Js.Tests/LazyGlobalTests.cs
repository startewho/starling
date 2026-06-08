using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// wp:js-bootstrap — lazy intrinsic installation. The long-tail built-ins
/// (Intl, Date, Proxy/Reflect/Math/JSON, the ArrayBuffer/DataView/typed-array
/// cluster, Map/Set/Weak*/FinalizationRegistry) are installed on first access
/// via <see cref="JsGlobalObject"/>'s placeholder-plus-registry mechanism. Every
/// observable must match an eager build. These tests pin the spec-observable
/// invariants: presence and key order before access, the data-descriptor shape,
/// delete-before-access, overwrite-before-access, non-enumerability, and that
/// each deferred intrinsic is fully functional after deferral.
/// </summary>
[TestClass]
public class LazyGlobalTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    private static string EvalStr(string src) => Eval(src).AsString;
    private static double EvalNum(string src) => Eval(src).AsNumber;
    private static bool EvalBool(string src) => Eval("String(" + src + ")").AsString == "true";

    /// <summary>Run a statement sequence whose final statement is an expression,
    /// coercing that result to a string. Use for snippets that need statements
    /// (var decls, delete, multiple statements) which cannot be wrapped in a
    /// single <c>String(...)</c> call.</summary>
    private static string EvalStmtStr(string finalExpr, string setup)
        => EvalStr(setup + " String(" + finalExpr + ")");

    // ----- Invariant 1: presence + key order BEFORE any access -----

    [TestMethod]
    public void Deferred_names_are_present_in_globalThis_before_access()
    {
        // Fresh realm, no prior touch — `in` must already see every deferred name.
        EvalBool("'Intl' in globalThis").Should().BeTrue();
        EvalBool("'Map' in globalThis").Should().BeTrue();
        EvalBool("'Date' in globalThis").Should().BeTrue();
        EvalBool("'Proxy' in globalThis").Should().BeTrue();
        EvalBool("'Reflect' in globalThis").Should().BeTrue();
        EvalBool("'Math' in globalThis").Should().BeTrue();
        EvalBool("'JSON' in globalThis").Should().BeTrue();
        EvalBool("'ArrayBuffer' in globalThis").Should().BeTrue();
        EvalBool("'DataView' in globalThis").Should().BeTrue();
        EvalBool("'Uint8Array' in globalThis").Should().BeTrue();
        EvalBool("'Set' in globalThis").Should().BeTrue();
        EvalBool("'WeakMap' in globalThis").Should().BeTrue();
        EvalBool("'WeakSet' in globalThis").Should().BeTrue();
        EvalBool("'WeakRef' in globalThis").Should().BeTrue();
        EvalBool("'FinalizationRegistry' in globalThis").Should().BeTrue();
    }

    [TestMethod]
    public void getOwnPropertyNames_lists_deferred_names_before_access()
    {
        EvalNum("Object.getOwnPropertyNames(globalThis).indexOf('Map') >= 0 ? 1 : 0")
            .Should().Be(1);
        EvalNum("Object.getOwnPropertyNames(globalThis).indexOf('Intl') >= 0 ? 1 : 0")
            .Should().Be(1);
    }

    [TestMethod]
    public void getOwnPropertyNames_preserves_relative_install_order()
    {
        // Install order in JsRuntime: Map ... Math ... Date ... Proxy ... Reflect.
        // The placeholders sit at their install-time creation positions, so the
        // relative order in getOwnPropertyNames is byte-identical to an eager
        // build. We assert the pairwise ordering with no prior access (reading
        // indexOf does not materialize).
        const string src = @"
            var n = Object.getOwnPropertyNames(globalThis);
            var ok =
                n.indexOf('Map') < n.indexOf('Math') &&
                n.indexOf('Math') < n.indexOf('Date') &&
                n.indexOf('Date') < n.indexOf('Proxy') &&
                n.indexOf('Proxy') < n.indexOf('Reflect');
            String(ok)";
        EvalStr(src).Should().Be("true");
    }

    // ----- Invariant 2: data-descriptor shape (materializes, allowed) -----

    [TestMethod]
    public void getOwnPropertyDescriptor_is_a_data_descriptor()
    {
        const string src = @"
            var d = Object.getOwnPropertyDescriptor(globalThis, 'Map');
            String(
                ('value' in d) &&
                d.writable === true &&
                d.enumerable === false &&
                d.configurable === true &&
                !('get' in d) && !('set' in d) &&
                d.value === Map)";
        EvalStr(src).Should().Be("true");
    }

    [TestMethod]
    public void descriptor_value_is_the_real_constructor()
    {
        EvalBool("Object.getOwnPropertyDescriptor(globalThis,'Map').value === Map").Should().BeTrue();
        EvalBool("typeof Object.getOwnPropertyDescriptor(globalThis,'Intl').value === 'object'").Should().BeTrue();
    }

    // ----- Invariant 3: delete before access -----

    [TestMethod]
    public void delete_before_access_removes_the_global()
    {
        EvalStmtStr("'Intl' in globalThis", "delete globalThis.Intl;").Should().Be("false");
        EvalStmtStr("'Map' in globalThis", "delete globalThis.Map;").Should().Be("false");
        EvalStmtStr("'Proxy' in globalThis", "delete globalThis.Proxy;").Should().Be("false");
    }

    // ----- Invariant 4: overwrite before access -----

    [TestMethod]
    public void overwrite_before_access_sticks()
    {
        EvalNum("globalThis.Proxy = 5; globalThis.Proxy").Should().Be(5);
        EvalNum("globalThis.Intl = 7; globalThis.Intl").Should().Be(7);
        EvalNum("Map = 9; Map").Should().Be(9);
    }

    [TestMethod]
    public void redefine_before_access_sticks()
    {
        const string src = @"
            Object.defineProperty(globalThis, 'Reflect', { value: 42, configurable: true });
            String(globalThis.Reflect)";
        EvalStr(src).Should().Be("42");
    }

    // ----- Invariant 5: functional after deferral -----

    [TestMethod]
    public void deferred_intrinsics_work_after_first_access()
    {
        EvalNum("new Map([[1,2]]).get(1)").Should().Be(2);
        EvalNum("new Uint8Array(4).length").Should().Be(4);
        EvalBool("Reflect.has({a:1},'a')").Should().BeTrue();
        EvalNum("Math.max(1,2)").Should().Be(2);
        EvalNum("new Date(0).getTime()").Should().Be(0);
        EvalBool("typeof Intl.DateTimeFormat === 'function'").Should().BeTrue();
        EvalStr("JSON.stringify({a:1})").Should().Be("{\"a\":1}");
        EvalNum("new Set([1,1,2]).size").Should().Be(2);
        EvalNum("var ab = new ArrayBuffer(8); new DataView(ab).byteLength").Should().Be(8);
    }

    [TestMethod]
    public void proxy_constructs_and_traps()
    {
        EvalNum("new Proxy({x:3},{}).x").Should().Be(3);
        EvalNum("new Proxy({},{get(){return 11;}}).anything").Should().Be(11);
    }

    [TestMethod]
    public void weak_collections_work_after_deferral()
    {
        EvalStmtStr("wm.has(k)", "var k={}; var wm=new WeakMap(); wm.set(k,1);").Should().Be("true");
        EvalStmtStr("ws.has(k2)", "var k2={}; var ws=new WeakSet(); ws.add(k2);").Should().Be("true");
        EvalStmtStr("new WeakRef(k3).deref() === k3", "var k3={};").Should().Be("true");
        EvalBool("typeof new FinalizationRegistry(function(){}) === 'object'").Should().BeTrue();
    }

    [TestMethod]
    public void typed_array_cluster_materializes_together()
    {
        // Touching one typed array materializes the whole shared cluster
        // (ArrayBuffer/DataView/all typed arrays).
        const string src = @"
            var probe = Int8Array;
            String(
                typeof ArrayBuffer === 'function' &&
                typeof DataView === 'function' &&
                typeof Uint8ClampedArray === 'function' &&
                typeof Float64Array === 'function' &&
                typeof BigInt64Array === 'function')";
        EvalStr(src).Should().Be("true");
    }

    // ----- Invariant 6: deferred names are NOT enumerable -----

    [TestMethod]
    public void deferred_names_are_not_in_Object_keys()
    {
        const string src = @"
            var k = Object.keys(globalThis);
            String(
                k.indexOf('Map') < 0 &&
                k.indexOf('Intl') < 0 &&
                k.indexOf('Date') < 0 &&
                k.indexOf('Proxy') < 0 &&
                k.indexOf('Reflect') < 0 &&
                k.indexOf('Math') < 0 &&
                k.indexOf('JSON') < 0 &&
                k.indexOf('Uint8Array') < 0)";
        EvalStr(src).Should().Be("true");
    }

    // Even after materialization the real intrinsic is non-enumerable too.
    [TestMethod]
    public void materialized_names_stay_non_enumerable()
    {
        const string src = @"
            void Map; void Intl;
            var k = Object.keys(globalThis);
            String(k.indexOf('Map') < 0 && k.indexOf('Intl') < 0)";
        EvalStr(src).Should().Be("true");
    }
}
