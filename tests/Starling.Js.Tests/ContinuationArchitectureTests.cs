// SPDX-License-Identifier: Apache-2.0

using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Modules;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

[TestClass]
public class ContinuationArchitectureTests
{
    [TestMethod]
    public void Generator_creation_and_resume_does_not_start_worker_thread()
    {
        var (runtime, result) = Eval("""
            function* g() { yield 1; return 2; }
            var it = g();
            var a = it.next();
            var b = it.next();
            a.value + ':' + a.done + '|' + b.value + ':' + b.done;
            """);

        result.AsString.Should().Be("1:false|2:true");
        runtime.Diagnostics.ContinuationThreadStarts.Should().Be(0);
    }

    [TestMethod]
    public void Async_function_await_resume_does_not_start_worker_thread()
    {
        var (runtime, _) = Eval("""
            async function f() { return await Promise.resolve(42); }
            f().then(function(v) { globalThis.r = v; });
            """);

        runtime.GetGlobal("r").AsNumber.Should().Be(42);
        runtime.Diagnostics.ContinuationThreadStarts.Should().Be(0);
    }

    [TestMethod]
    public void Async_generator_request_queue_does_not_start_worker_thread()
    {
        var (runtime, _) = Eval("""
            async function* g() {
                await Promise.resolve();
                yield 'a';
                await Promise.resolve();
                yield 'b';
            }
            var it = g();
            globalThis.log = '';
            it.next().then(function(r) { globalThis.log += r.value + ':' + r.done + ';'; });
            it.next().then(function(r) { globalThis.log += r.value + ':' + r.done + ';'; });
            it.next().then(function(r) { globalThis.log += r.value + ':' + r.done + ';'; });
            """);

        runtime.GetGlobal("log").AsString.Should().Be("a:false;b:false;undefined:true;");
        runtime.Diagnostics.ContinuationThreadStarts.Should().Be(0);
    }

    [TestMethod]
    public void Top_level_await_does_not_start_worker_thread()
    {
        var runtime = new JsRuntime();
        JsValue captured = JsValue.Undefined;
        runtime.RegisterGlobal("report", args =>
        {
            captured = args.Length > 0 ? args[0] : JsValue.Undefined;
            return JsValue.Undefined;
        });

        var loader = new ModuleLoader(runtime, new MapHost(new Dictionary<string, string>
        {
            ["main"] = """
                const a = await Promise.resolve(20);
                const b = await Promise.resolve(22);
                report(a + b);
                """,
        }));

        runtime.WithActiveVm(() => loader.LoadAndEvaluate("main"));

        captured.AsNumber.Should().Be(42);
        runtime.Diagnostics.ContinuationThreadStarts.Should().Be(0);
    }

    [TestMethod]
    public void Continuation_frame_reports_stable_js_stack_after_resume()
    {
        var (runtime, _) = Eval("""
            async function resumed() {
                await Promise.resolve();
                throw new Error('after resume');
            }
            resumed().catch(function(e) { globalThis.stack = e.stack; });
        """);

        var stack = runtime.GetGlobal("stack").AsString;
        stack.Should().Contain("Error: after resume");
        stack.Should().Contain("at resumed");
    }

    [TestMethod]
    public void Continuation_frame_reports_stable_js_stack_after_yield_resume()
    {
        var (runtime, _) = Eval("""
            function* resumedGenerator() {
                yield 'pause';
                throw new Error('after yield');
            }
            var it = resumedGenerator();
            it.next();
            try {
                it.next();
            } catch (e) {
                globalThis.stack = e.stack;
            }
            """);

        var stack = runtime.GetGlobal("stack").AsString;
        stack.Should().Contain("Error: after yield");
        stack.Should().Contain("at resumedGenerator");
    }

    [TestMethod]
    public void Recursive_normal_calls_still_use_existing_range_error_guard()
    {
        var (_, result) = Eval("""
            function recurse() { return recurse(); }
            try { recurse(); }
            catch (e) { e.name; }
            """);

        result.AsString.Should().Be("RangeError");
    }

    [TestMethod]
    public void Continuation_resume_rejects_reentrant_resume()
    {
        var (_, result) = Eval("""
            var it;
            function* g() {
                try {
                    it.next();
                    return 'unreached';
                } catch (e) {
                    return e.name;
                }
            }
            it = g();
            var r = it.next();
            r.value + ':' + r.done;
            """);

        result.AsString.Should().Be("TypeError:true");
    }

    private static (JsRuntime runtime, JsValue result) Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var rt = new JsRuntime();
        var r = new JsVm(rt).Run(chunk);
        return (rt, r);
    }

    private sealed class MapHost(Dictionary<string, string> modules) : IModuleHost
    {
        public string? Resolve(string specifier, string? referrer) =>
            modules.ContainsKey(specifier) ? specifier : null;

        public string? FetchSource(string resolvedUrl) =>
            modules.TryGetValue(resolvedUrl, out var src) ? src : null;
    }
}
