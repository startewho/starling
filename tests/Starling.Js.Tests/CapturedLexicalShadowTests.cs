using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests;

/// <summary>
/// Regression for the root cause of github.com's high-contrast-cookie runtime
/// failure ("not a function: [object Object]"): a captured block-scoped
/// <c>let</c> that shadows a same-named <c>function</c> declaration in the
/// enclosing scope must use its OWN cell — it must not clobber the function.
///
/// <para>Was: captured lexicals were preallocated function-lifetime cells by the
/// pre-pass (<c>PreallocateCapturedInStatement</c>) by NAME, and
/// <c>HoistLexicalName</c> returned early for captured names — so a block
/// <c>let t</c> shared the hoisted <c>function t</c> cell and clobbered it. Fix:
/// the pre-pass no longer reserves block-scoped lexicals (only function-level
/// ones); <c>HoistLexicalName</c> gives a captured block lexical its OWN cell at
/// block entry. These tests guard that fix and the per-iteration <c>let</c>
/// semantics it touches.</para>
/// </summary>
[TestClass]
public class CapturedLexicalShadowTests
{
    private static string Eval(string src)
    {
        var rt = new JsRuntime();
        new JsVm(rt).Run(JsCompiler.Compile(new JsParser(src).ParseProgram(), "<t>"));
        return JsValue.ToStringValue(rt.GetGlobal("__r"));
    }

    [TestMethod]
    public void Captured_block_let_does_not_clobber_same_named_function_decl()
    {
        // A closure reads `t`; `t` is a function declaration in the enclosing
        // scope, and a sibling block has its own `let t`. The closure must see
        // the function, not the block's object.
        Eval(@"globalThis.__r = (()=>{
                    if (true) { let t = { x: 1 }; }
                    function t(a) { return 'fn'; }
                    return (function(){ return typeof t; })();
                })();")
            .Should().Be("function");
    }

    [TestMethod]
    public void Block_let_shadowing_outer_var_captured_does_not_clobber()
    {
        Eval(@"globalThis.__r = (()=>{
                    var t = 'outer';
                    { let t = { x: 1 }; }
                    return (function(){ return t; })();
                })();")
            .Should().Be("outer");
    }

    // Guards the per-iteration `let` semantics the pre-pass change touches:
    // each loop iteration must capture its OWN binding.
    [TestMethod]
    public void Per_iteration_let_in_for_loop_captures_distinct_bindings()
    {
        Eval(@"var fns=[]; for (let i=0;i<3;i++){ fns.push(function(){ return i; }); }
               globalThis.__r = fns[0]()+','+fns[1]()+','+fns[2]();")
            .Should().Be("0,1,2");
    }

    [TestMethod]
    public void Per_iteration_let_in_for_of_captures_distinct_bindings()
    {
        Eval(@"var fns=[]; for (let v of [10,20,30]){ fns.push(()=>v); }
               globalThis.__r = fns[0]()+','+fns[1]()+','+fns[2]();")
            .Should().Be("10,20,30");
    }

    [TestMethod]
    public void Block_let_in_loop_body_captured_is_per_iteration()
    {
        Eval(@"var fns=[]; for (var i=0;i<3;i++){ let k=i*2; fns.push(()=>k); }
               globalThis.__r = fns[0]()+','+fns[1]()+','+fns[2]();")
            .Should().Be("0,2,4");
    }

    [TestMethod]
    public void Captured_block_let_read_after_init_is_the_block_value()
    {
        Eval(@"globalThis.__r = (()=>{
                    let out;
                    { let t = 7; out = (function(){ return t; })(); }
                    return out;
                })();")
            .Should().Be("7");
    }

    [TestMethod]
    public void Block_const_shadowing_function_decl_captured_does_not_clobber()
    {
        Eval(@"globalThis.__r = (()=>{
                    if (true) { const t = 1; }
                    function t(){ return 'fn'; }
                    return (function(){ return typeof t; })();
                })();")
            .Should().Be("function");
    }

    [TestMethod]
    public void Destructuring_block_let_captured_does_not_clobber_function_decl()
    {
        Eval(@"globalThis.__r = (()=>{
                    if (true) { let { t } = { t: 1 }; }
                    function t(){ return 'fn'; }
                    return (function(){ return typeof t; })();
                })();")
            .Should().Be("function");
    }

    [TestMethod]
    public void Tdz_through_closure_within_a_block_still_throws_then_reads()
    {
        // A closure created before the let, inside the same block, captures the
        // block cell: reading before init throws ReferenceError; after init reads it.
        Eval(@"globalThis.__r = (()=>{
                    let result;
                    {
                        let read = () => x;
                        var threw = false;
                        try { read(); } catch (e) { threw = (e instanceof ReferenceError); }
                        let x = 42;
                        result = threw + ',' + read();
                    }
                    return result;
                })();")
            .Should().Be("true,42");
    }

    [TestMethod]
    public void Nested_block_let_shadows_function_decl_within_block()
    {
        // Inside the block, `t` is the block let; outside, the function.
        Eval(@"globalThis.__r = (()=>{
                    function t(){ return 'fn'; }
                    let inside;
                    { let t = 'blk'; inside = (function(){ return t; })(); }
                    var outside = (function(){ return typeof t; })();
                    return inside + '|' + outside;
                })();")
            .Should().Be("blk|function");
    }
}
