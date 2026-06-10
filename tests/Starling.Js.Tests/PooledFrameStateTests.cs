// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// Guards for the pooled per-frame state introduced for the CallFrame
/// allocation hotspot: the locals array is rented from the shared array pool
/// (returned at frame pop, kept across generator/async suspension, and NEVER
/// returned when it escapes through a mapped <c>arguments</c> object or a
/// direct-eval scope), and the try-frame stack is created lazily on the first
/// <c>EnterTry</c>. Every test churns the pool with extra calls so a recycled
/// array that leaks state shows up as a wrong value.
/// </summary>
[TestClass]
public class PooledFrameStateTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    [TestMethod]
    public void Locals_read_undefined_after_heavy_pool_reuse()
    {
        // a() dirties a pooled locals array with numbers; b() then rents from
        // the same pool and must still observe plain `var` slots as undefined.
        Eval("""
            function a(x){ var p = x + 1, q = x + 2, r = x + 3; return p + q + r; }
            function b(){ var p, q, r; return p === undefined && q === undefined && r === undefined; }
            var ok = true;
            for (var i = 0; i < 2000; i++) { a(i); ok = ok && b(); }
            ok;
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Escaped_mapped_arguments_survives_frame_pop_and_pool_churn()
    {
        // f's locals array escapes into the mapped arguments object. After f
        // pops, churn() recycles pooled arrays heavily; the kept arguments
        // objects must still read (and write through) the original slots.
        Eval("""
            function f(a, b){ a = a * 10; return arguments; }
            function churn(n){ var x = n, y = n + 1, z = n + 2; return n <= 0 ? x : churn(n - 1) + y - z; }
            var kept = [];
            for (var i = 1; i <= 50; i++) kept.push(f(i, i + 100));
            churn(400);
            var ok = true;
            for (var j = 1; j <= 50; j++) {
                var args = kept[j - 1];
                ok = ok && args[0] === j * 10 && args[1] === j + 100;
                args[0] = -j;
                ok = ok && args[0] === -j;
            }
            ok;
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Generator_locals_survive_suspension_across_pool_churn()
    {
        // The suspended frame keeps its pooled locals array across next()
        // calls; the churn between resumes must not recycle it.
        Eval("""
            function* g(seed){ var a = seed + 1, b = seed + 2; yield a; yield b; return a + b; }
            function churn(n){ var x = n, y = n, z = n; return n <= 0 ? x : churn(n - 1) + y - z; }
            var it = g(10);
            var r1 = it.next().value; churn(300);
            var r2 = it.next().value; churn(300);
            var r3 = it.next().value;
            r1 === 11 && r2 === 12 && r3 === 23;
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Sloppy_generator_mapped_arguments_escape_survives_suspension()
    {
        // The LocalsEscaped mark must ride the suspension snapshot: the
        // generator's frame is re-materialized on every resume, completes, and
        // its locals array must STILL stay out of the pool because the mapped
        // arguments object kept it.
        // (`yield arguments` directly is a pre-existing compiler gap —
        // ReferenceError — so capture it through a var first.)
        Eval("""
            function* g(a){ var k = arguments; yield k; a = 77; yield a; }
            function churn(n){ var x = n, y = n, z = n; return n <= 0 ? x : churn(n - 1) + y - z; }
            var it = g(7);
            var args = it.next().value;
            it.next(); it.next();           // complete the body
            churn(400);                      // recycle pooled arrays
            args[0] === 77;                  // mapped slot still aliases the param
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Async_function_locals_survive_await_across_pool_churn()
    {
        var runtime = new JsRuntime();
        var program = new JsParser("""
            async function f(){ var v = 40; await 0; v += 2; return v; }
            function churn(n){ var x = n, y = n, z = n; return n <= 0 ? x : churn(n - 1) + y - z; }
            var p = f();
            churn(400);
            p.then(function(r){ globalThis.r = r; });
            """).ParseProgram();
        new JsVm(runtime).Run(JsCompiler.CompileForEval(program));
        runtime.GetGlobal("r").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Throw_across_trampolined_frames_runs_finally_with_lazy_try_stack()
    {
        // inner() has no try (TryStack stays null and is unwound through);
        // mid() has finally only; outer() catches. Order proves the finally
        // ran during the cross-frame unwind.
        Eval("""
            var log = '';
            function inner(){ log += 'i'; throw new Error('boom'); }
            function mid(){ try { inner(); } finally { log += 'f'; } }
            function outer(){ try { mid(); } catch (e) { log += 'c:' + e.message; } }
            outer();
            log;
            """).AsString.Should().Be("ifc:boom");
    }

    [TestMethod]
    public void Deep_recursion_range_error_unwind_keeps_engine_usable()
    {
        // The unwinder releases every pooled frame on the way out; the
        // follow-up calls must compute correctly on recycled arrays.
        Eval("""
            function r(n){ var pad1 = n, pad2 = n; return r(n + 1) + pad1 - pad2; }
            var caught = false;
            try { r(0); } catch (e) { caught = e instanceof RangeError; }
            function sum3(a, b, c){ var t = a + b + c; return t; }
            caught && sum3(1, 2, 3) === 6;
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Direct_eval_reads_and_writes_caller_locals_after_churn()
    {
        // The eval scope references the caller's locals array directly; the
        // caller frame's locals are excluded from pooling, so later frames can
        // never alias them.
        Eval("""
            function churn(n){ var x = n, y = n, z = n; return n <= 0 ? x : churn(n - 1) + y - z; }
            function f(a){ var b = 2; eval('b = a + b;'); churn(200); return b; }
            var first = f(40);
            churn(300);
            first === 42 && f(40) === 42;
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Return_through_finally_in_generator_releases_frame_once()
    {
        // generator .return() at a yield inside try/finally: the sentinel
        // path diverts through the finalizer, then releases the pooled frame
        // exactly once at completion.
        Eval("""
            var log = '';
            function* g(){ try { yield 1; yield 2; } finally { log += 'f'; } }
            var it = g();
            it.next();
            var r = it.return(9);
            log + '|' + r.value + ':' + r.done + '|' + it.next().done;
            """).AsString.Should().Be("f|9:true|true");
    }
}
