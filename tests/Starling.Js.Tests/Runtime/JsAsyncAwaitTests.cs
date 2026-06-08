using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B1b-2c — Async/await tests. Async function invocation returns a
/// Promise immediately and runs the body on a worker thread; each await
/// re-suspends until the awaited promise settles.
/// </summary>
[TestClass]
public class JsAsyncAwaitTests
{
    [TestMethod]
    public void Async_function_returns_a_promise_resolving_to_body_value()
    {
        var (runtime, r) = Eval(@"
            async function f() { return 1 }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Await_resolves_to_promise_value()
    {
        var (runtime, _) = Eval(@"
            async function f() { return await Promise.resolve(42); }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Await_resolves_primitive_value()
    {
        var (runtime, _) = Eval(@"
            (async function() { return await '1'; })()
                .then(function(v) { globalThis.r = v; });
        ");

        runtime.GetGlobal("r").AsString.Should().Be("1");
    }

    [TestMethod]
    public void Async_function_arguments_are_not_reused_between_functions()
    {
        var (runtime, _) = Eval(@"
            async function method() {
              return arguments[0] + ':' + String(arguments[1]);
            }

            async function other() {
              return arguments[0] + ':' + String(arguments[1]);
            }

            method(42, undefined).then(function(v) { globalThis.a = v; });
            other(10, undefined).then(function(v) { globalThis.b = v; });
        ");

        (runtime.GetGlobal("a").AsString + "|" + runtime.GetGlobal("b").AsString)
            .Should().Be("42:undefined|10:undefined");
    }

    [TestMethod]
    public void Sequential_awaits_add_correctly()
    {
        var (runtime, _) = Eval(@"
            async function f() {
                var a = await Promise.resolve(1);
                var b = await Promise.resolve(2);
                return a + b;
            }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Awaited_rejection_is_catchable()
    {
        var (runtime, _) = Eval(@"
            async function f() {
                try { await Promise.reject('bad'); return 'unreached'; }
                catch (e) { return 'caught ' + e; }
            }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("caught bad");
    }

    [TestMethod]
    public void Await_inside_catch_preserves_binding_and_does_not_rerun_try()
    {
        var (runtime, _) = Eval(@"
            async function f() {
                var tries = 0;
                try {
                    tries++;
                    await Promise.reject(42);
                } catch (e) {
                    await Promise.resolve();
                    return tries + ':' + e;
                }
            }
            f().then(function(v) { globalThis.r = v; });
        ");

        runtime.GetGlobal("r").AsString.Should().Be("1:42");
    }

    [TestMethod]
    public void Catch_binding_does_not_leak_after_await_inside_catch()
    {
        var (runtime, _) = Eval(@"
            async function f() {
                try {
                    throw 1;
                } catch (e) {
                    await Promise.resolve();
                }

                try {
                    return e;
                } catch (err) {
                    return err.name;
                }
            }
            f().then(function(v) { globalThis.r = v; });
        ");

        runtime.GetGlobal("r").AsString.Should().Be("ReferenceError");
    }

    [TestMethod]
    public void Awaited_finally_preserves_catch_return_and_throw_completions()
    {
        var (runtime, _) = Eval(@"
            async function returns() {
                try {
                    throw 1;
                } catch {
                    await Promise.resolve();
                    return 2;
                } finally {
                    await Promise.resolve();
                }
            }
            async function throws() {
                try {
                    throw 1;
                } catch {
                    await Promise.resolve();
                    throw 4;
                } finally {
                    await Promise.resolve();
                }
            }
            returns().then(function(v) { globalThis.returned = v; });
            throws().catch(function(e) { globalThis.thrown = e; });
        ");

        runtime.GetGlobal("returned").AsNumber.Should().Be(2);
        runtime.GetGlobal("thrown").AsNumber.Should().Be(4);
    }

    [TestMethod]
    public void Async_function_that_throws_yields_rejected_promise()
    {
        var (runtime, _) = Eval(@"
            async function f() { throw 1 }
            f().catch(function(e) { globalThis.r = e });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Async_arrow_returns_promise()
    {
        var (runtime, _) = Eval(@"
            var f = async () => await Promise.resolve(5);
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(5);
    }

    [TestMethod]
    public void Async_arrow_with_concise_body_returns_promise()
    {
        var (runtime, _) = Eval(@"
            var f = async x => x + 1;
            f(10).then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(11);
    }

    [TestMethod]
    public void Async_generator_yields_promises()
    {
        // wp:M3-04g — .next() returns a Promise of {value, done}. (Full
        // yield/await interleaving + for-await is covered in
        // AsyncGeneratorTests.)
        var (runtime, _) = Eval(@"
            async function* g() { yield 1; yield 2; }
            var it = g();
            it.next().then(function(r) { globalThis.r = r.value });
        ");
        runtime.GetGlobal("r").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Await_thenable_resolve_then_throw_follows_nested_resolution()
    {
        var (runtime, _) = Eval(@"
            async function f() {
                try {
                    return await {
                        then: function(resolve) {
                            resolve({ then: function(resolve2) { resolve2('ok'); } });
                            throw 'bad';
                        }
                    };
                } catch (e) {
                    return 'caught:' + e;
                }
            }
            f().then(function(v) { globalThis.r = v });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("ok");
    }

    [TestMethod]
    public void Await_resume_does_not_reevaluate_control_flow_tests()
    {
        var (runtime, _) = Eval(@"
            async function run() {
                var tests = 0;
                var whileTests = 0;
                var switchTests = 0;
                var ifResult = 0;
                if (++tests === 1) {
                    await Promise.resolve();
                    ifResult = tests;
                }
                while (++whileTests <= 1) {
                    await Promise.resolve();
                    break;
                }
                switch (++switchTests) {
                    case 1:
                        await Promise.resolve();
                        break;
                    default:
                        switchTests = 99;
                }
                return ifResult + ':' + whileTests + ':' + switchTests;
            }
            run().then(function(v) { globalThis.r = v; });
        ");

        runtime.GetGlobal("r").AsString.Should().Be("1:1:1");
    }

    [TestMethod]
    public void Await_resume_preserves_expression_left_operands_and_call_arguments()
    {
        var (runtime, _) = Eval(@"
            async function run() {
                var d = 0;
                var sum = (++d) + (await Promise.resolve(10));
                var i = 0;
                var foo = function(a, b, c) { return [a, b, c]; };
                var call = foo(++i, ++i, await Promise.resolve(++i));
                return JSON.stringify({ d: d, sum: sum, call: call, i: i });
            }
            run().then(function(v) { globalThis.r = v; });
        ");

        runtime.GetGlobal("r").AsString.Should().Be("{\"d\":1,\"sum\":11,\"call\":[1,2,3],\"i\":3}");
    }

    [TestMethod]
    public void Await_resume_preserves_spread_iterators_and_literal_progress()
    {
        var (runtime, _) = Eval(@"
            async function run() {
                function* gen() { yield 'a'; yield 'b'; yield 'c'; }
                var g = gen();
                var arr = [...g, await Promise.resolve('d')];
                var i = 0;
                var obj = { a: ++i, b: ++i, c: await Promise.resolve(++i) };
                var text = `${++i}-${await Promise.resolve('x')}-${++i}`;
                return JSON.stringify({ arr: arr, obj: obj, text: text, i: i });
            }
            run().then(function(v) { globalThis.r = v; });
        ");

        runtime.GetGlobal("r").AsString.Should().Be("{\"arr\":[\"a\",\"b\",\"c\",\"d\"],\"obj\":{\"a\":1,\"b\":2,\"c\":3},\"text\":\"4-x-5\",\"i\":5}");
    }

    [TestMethod]
    public void For_await_normal_exhaustion_does_not_call_return()
    {
        var (runtime, _) = Eval(@"
            globalThis.closed = 'no';
            async function run() {
                var iterable = {
                    [Symbol.asyncIterator]() {
                        var state = { i: 0 };
                        return {
                            next: function() {
                                state.i = state.i + 1;
                                return Promise.resolve(state.i <= 2
                                    ? { value: state.i, done: false }
                                    : { value: undefined, done: true });
                            },
                            return: function() {
                                globalThis.closed = 'yes';
                                return Promise.resolve({ value: undefined, done: true });
                            }
                        };
                    }
                };
                var out = '';
                for await (var x of iterable) { out = out + x; }
                return out + '|' + globalThis.closed;
            }
            run().then(
                function(v) { globalThis.r = v },
                function(e) { globalThis.r = 'err:' + e });
        ");
        runtime.GetGlobal("r").AsString.Should().Be("12|no");
    }

    private static (JsRuntime runtime, JsValue result) Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        var rt = new JsRuntime();
        var r = new JsVm(rt).Run(chunk);
        return (rt, r);
    }
}
