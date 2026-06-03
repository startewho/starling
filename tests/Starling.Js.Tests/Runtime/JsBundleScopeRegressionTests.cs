using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Runtime;

[TestClass]
public class JsBundleScopeRegressionTests
{
    [TestMethod]
    public void Reused_minified_parameter_name_keeps_inner_call_argument_binding()
    {
        const string src = """
            var e;
            !function(e) {
                var i = [];
                function u(e, t) {
                    e.forEach(function (value) { t.push(value); });
                }
                u(["input", "change"], i);
                globalThis.result = i.join(",");
            }(e || (e = {}));
            result;
            """;

        Eval(src).AsString.Should().Be("input,change");
    }

    [TestMethod]
    public void Reused_minified_parameter_name_keeps_array_for_each_with_arrow_callback()
    {
        const string src = """
            var e;
            !function(e) {
                const i = new Map;
                function u(e, t) {
                    e.forEach((e => i.set(e, t)));
                }
                u(["input", "change"], {browserEventName: "field"});
                globalThis.result = i.get("change").browserEventName;
            }(e || (e = {}));
            result;
            """;

        Eval(src).AsString.Should().Be("field");
    }

    [TestMethod]
    public void Nested_iife_class_name_does_not_displace_later_outer_function_declaration()
    {
        const string src = """
            !function() {
                !function(e) {
                    class u {
                    }
                    globalThis.inner = new u(e).constructor.name;
                }({});
                const i = new Map;
                function u(e, t) {
                    e.forEach((e => i.set(e, t)));
                }
                u(["input", "change"], {browserEventName: "field"});
                globalThis.result = inner + ":" + i.get("change").browserEventName;
            }();
            result;
            """;

        Eval(src).AsString.Should().Be("u:field");
    }

    [TestMethod]
    public void Blazor_style_async_options_gate_resolves_after_promise_resolve()
    {
        const string src = """
            globalThis.trace = "";
            var release;
            var gate = new Promise(function (resolve) { release = resolve; trace += "executor;"; });
            var options;
            function configure(value) {
                trace += "configure;";
                !async function (pending) {
                    trace += "inner-start;";
                    options = await pending;
                    trace += "inner-after;";
                    release();
                    trace += "released;";
                }(value);
            }
            async function start(value) {
                trace += "outer-start;";
                configure(Promise.resolve(value || {}));
                trace += "outer-before-await;";
                await gate;
                trace += "outer-after;";
                globalThis.result = options.flag;
            }
            start({ flag: "ready" }).catch(function (error) {
                globalThis.error = error && error.message ? error.message : String(error);
            });
            """;

        var runtime = EvalRuntime(src);
        var debug = runtime.GetGlobal("trace").AsString + "|"
            + (runtime.GetGlobal("error").IsString ? runtime.GetGlobal("error").AsString : "");
        debug.Should().Be(
            "executor;outer-start;configure;inner-start;outer-before-await;inner-after;released;outer-after;|");
        runtime.GetGlobal("result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Blazor_minified_options_gate_resolves_before_platform_load()
    {
        const string src = """
            globalThis.trace = "";
            let Mt, Kt;
            const Gt = new Promise((e => { Kt = e; trace += "gate-executor;"; }));
            function Xt(e) {
                if (Mt) throw new Error("configured");
                !async function(e) {
                    trace += "options-start;";
                    const t = await e;
                    trace += "options-after;";
                    Mt = t, Kt();
                    trace += "gate-release;";
                }(e)
            }
            function Zt() {
                return (async () => {
                    trace += "load-start;";
                    await Gt;
                    trace += "load-after-gate;";
                    globalThis.result = Mt.flag;
                })();
            }
            function Yt() {
                return new Promise(async function(resolve) {
                    const pending = Zt();
                    trace += "before-await-load;";
                    await pending;
                    trace += "after-await-load;";
                    resolve();
                });
            }
            async function an(e) {
                Xt(Promise.resolve(e || {}));
                await Yt();
            }
            an({ flag: "ready" }).catch(function (error) {
                globalThis.error = error && error.message ? error.message : String(error);
            });
            """;

        var runtime = EvalRuntime(src);
        var debug = runtime.GetGlobal("trace").AsString + "|"
            + (runtime.GetGlobal("error").IsString ? runtime.GetGlobal("error").AsString : "");
        debug.Should().Be(
            "gate-executor;options-start;load-start;before-await-load;options-after;gate-release;load-after-gate;after-await-load;|");
        runtime.GetGlobal("result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Cross_script_async_options_gate_resolves_pending_await()
    {
        const string setup = """
            globalThis.trace = "";
            let Mt, Kt;
            const Gt = new Promise((e => { Kt = e; trace += "gate-executor;"; }));
            globalThis.startOptions = function(e) {
                !async function(e) {
                    trace += "options-start;";
                    const t = await e;
                    trace += "options-after;";
                    Mt = t, Kt();
                    trace += "gate-release;";
                }(e)
            };
            globalThis.startLoad = function() {
                return (async () => {
                    trace += "load-start;";
                    await Gt;
                    trace += "load-after-gate;";
                    globalThis.result = Mt.flag;
                })();
            };
            """;

        var runtime = EvalRuntime(setup);
        EvalInto(runtime, """
            startOptions(Promise.resolve({ flag: "ready" }));
            startLoad();
            """);

        var debug = runtime.GetGlobal("trace").AsString + "|"
            + (runtime.GetGlobal("error").IsString ? runtime.GetGlobal("error").AsString : "");
        debug.Should().Be(
            "gate-executor;options-start;load-start;options-after;gate-release;load-after-gate;|");
        runtime.GetGlobal("result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Async_arrow_captures_outer_lexical_used_only_as_await_operand()
    {
        const string src = """
            function outer() {
                let release;
                const gate = new Promise(resolve => { release = resolve; });
                const start = async () => {
                    await gate;
                    globalThis.result = "ready";
                };
                start();
                release();
            }
            outer();
            """;

        EvalGlobal(src, "result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Cross_script_async_gate_with_nullish_assignment_cache_resumes()
    {
        const string setup = """
            globalThis.trace = "";
            let Mt, Pt, Kt;
            const Gt = new Promise((e => { Kt = e; trace += "gate-executor;"; }));
            globalThis.startOptions = function(e) {
                !async function(e) {
                    trace += "options-start;";
                    const t = await e;
                    trace += "options-after;";
                    Mt = t, Kt();
                    trace += "gate-release;";
                }(e)
            };
            globalThis.startLoad = function(e) {
                return Pt ??= (async () => {
                    trace += "load-start;";
                    await Gt;
                    trace += "load-after-gate;";
                    const t = Mt ?? {};
                    t.environment || (t.environment = e?.environmentName ?? void 0);
                    globalThis.result = t.flag;
                })(), Pt;
            };
            """;

        var runtime = EvalRuntime(setup);
        EvalInto(runtime, """
            startOptions(Promise.resolve({ flag: "ready" }));
            startLoad();
            """);

        var debug = runtime.GetGlobal("trace").AsString + "|"
            + (runtime.GetGlobal("error").IsString ? runtime.GetGlobal("error").AsString : "");
        debug.Should().Be(
            "gate-executor;options-start;load-start;options-after;gate-release;load-after-gate;|");
        runtime.GetGlobal("result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Blazor_full_zT_body_resumes_after_options_gate()
    {
        const string setup = """
            globalThis.trace = "";
            let Mt, Pt, Ht, Kt;
            const gt = {
                load: async function (options, complete) {
                    trace += "gt.load;";
                    globalThis.result = options.flag;
                }
            };
            const Gt = new Promise((e => { Kt = e; trace += "gate-executor;"; }));
            globalThis.startOptions = function(e) {
                !async function(e) {
                    trace += "options-start;";
                    const t = await e;
                    trace += "options-after;";
                    Mt = t, Kt();
                    trace += "gate-release;";
                }(e)
            };
            globalThis.startLoad = function(e) {
                return Pt ??= (async () => {
                    trace += "load-start;";
                    await Gt;
                    trace += "load-after-gate;";
                    const t = Mt ?? {};
                    t.environment || (t.environment = e?.environmentName ?? void 0);
                    const n = Mt?.configureRuntime;
                    t.configureRuntime = t => {
                        n?.(t), e?.environmentVariables && t.withEnvironmentVariables(e.environmentVariables)
                    };
                    await gt.load(t, Ht);
                    trace += "load-after-gt;";
                })(), Pt;
            };
            """;

        var runtime = EvalRuntime(setup);
        EvalInto(runtime, """
            startOptions(Promise.resolve({ flag: "ready" }));
            startLoad();
            """);

        var debug = runtime.GetGlobal("trace").AsString + "|"
            + (runtime.GetGlobal("error").IsString ? runtime.GetGlobal("error").AsString : "");
        debug.Should().Be(
            "gate-executor;options-start;load-start;options-after;gate-release;load-after-gate;gt.load;load-after-gt;|");
        runtime.GetGlobal("result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Bang_async_iife_runs_body_after_await()
    {
        const string src = """
            !async function () {
                globalThis.before = "ran";
                globalThis.after = await Promise.resolve("done");
            }();
            """;

        var runtime = EvalRuntime(src);
        runtime.GetGlobal("before").AsString.Should().Be("ran");
        runtime.GetGlobal("after").AsString.Should().Be("done");
    }

    [TestMethod]
    public void Promise_reaction_can_resolve_pending_gate()
    {
        const string src = """
            var release;
            var gate = new Promise(function (resolve) { release = resolve; });
            gate.then(function () { globalThis.result = "ready"; });
            Promise.resolve().then(function () { release(); });
            """;

        EvalGlobal(src, "result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Promise_executor_function_expression_writes_outer_lexical_before_constructor_returns()
    {
        const string src = """
            function makeController() {
                let state = null;
                const promise = new Promise(function (resolve, reject) {
                    state = {
                        isDone: false,
                        promise: null,
                        resolve: function (value) {
                            if (!state.isDone) {
                                state.isDone = true;
                                resolve(value);
                            }
                        },
                        reject: function (error) {
                            if (!state.isDone) {
                                state.isDone = true;
                                reject(error);
                            }
                        }
                    };
                });
                state.promise = promise;
                state.resolve("ready");
                promise.then(function (value) { globalThis.result = value; });
            }
            makeController();
            """;

        EvalGlobal(src, "result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Async_continuation_can_resolve_pending_gate()
    {
        const string src = """
            var release;
            var gate = new Promise(function (resolve) { release = resolve; });
            gate.then(function () { globalThis.result = "ready"; });
            !async function () {
                await Promise.resolve();
                release();
            }();
            """;

        EvalGlobal(src, "result").AsString.Should().Be("ready");
    }

    [TestMethod]
    public void Ignored_async_function_still_resumes_after_awaited_gate()
    {
        const string src = """
            var release;
            var gate = new Promise(function (resolve) { release = resolve; });
            async function start() {
                await gate;
                globalThis.result = "ready";
            }
            start();
            Promise.resolve().then(function () { release(); });
            """;

        EvalGlobal(src, "result").AsString.Should().Be("ready");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    private static JsValue EvalGlobal(string src, string name)
        => EvalRuntime(src).GetGlobal(name);

    private static JsRuntime EvalRuntime(string src)
    {
        var runtime = new JsRuntime();
        EvalInto(runtime, src);
        return runtime;
    }

    private static void EvalInto(JsRuntime runtime, string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        new JsVm(runtime).Run(chunk);
    }

}
