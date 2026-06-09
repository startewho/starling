using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// B1b-2c — Generator tests. Generators park a heap-backed frame; the
/// dispatcher loop consults the SuspendedFrame on each .next() to read the
/// yielded value and resume.
/// </summary>
[TestClass]
public class JsGeneratorTests
{
    [TestMethod]
    public void Basic_generator_yields_two_values_then_done()
    {
        var r = Eval(@"
            function* g() { yield 1; yield 2; }
            var it = g();
            var a = it.next();
            var b = it.next();
            var c = it.next();
            '' + a.value + '|' + a.done + ',' + b.value + '|' + b.done + ',' + c.value + '|' + c.done
        ");
        r.AsString.Should().Be("1|false,2|false,undefined|true");
    }

    [TestMethod]
    public void Generator_next_with_value_is_yielded_back()
    {
        var r = Eval(@"
            function* g() {
                var x = yield 'first';
                yield 'got: ' + x;
            }
            var it = g();
            it.next();              // primer — yields 'first'
            it.next('hello').value  // resumed with 'hello'; yields 'got: hello'
        ");
        r.AsString.Should().Be("got: hello");
    }

    [TestMethod]
    public void Generator_return_value_appears_as_final_done_result()
    {
        var r = Eval(@"
            function* g() { return 42; }
            var v = g().next();
            '' + v.value + '|' + v.done
        ");
        r.AsString.Should().Be("42|true");
    }

    [TestMethod]
    public void Generator_for_of_iterates_yielded_values()
    {
        var r = Eval(@"
            function* range(n) {
                var i = 0;
                while (i < n) { yield i; i = i + 1; }
            }
            var sum = 0;
            for (var x of range(4)) sum = sum + x;
            sum
        ");
        r.AsNumber.Should().Be(6); // 0+1+2+3
    }

    [TestMethod]
    public void Generator_yield_in_for_update_expression_runs_each_iteration()
    {
        var r = Eval(@"
            function* foo() {
                for (var i = 0; i < 5; yield i++) {}
            }
            var str = '';
            for (var val of foo()) str += val;
            str;
        ");

        r.AsString.Should().Be("01234");
    }

    [TestMethod]
    public void For_of_ignores_generator_return_value()
    {
        Eval(@"
            function* foo() {
                yield 'a';
                return 'b';
            }
            var str = '';
            for (var val of foo()) str += val;
            str;
        ").AsString.Should().Be("a");

        Eval(@"
            function* foo() {
                return 'a';
            }
            var str = '';
            for (var val of foo()) str += val;
            str;
        ").AsString.Should().Be("");
    }

    [TestMethod]
    public void Generator_yields_undefined_as_visible_value()
    {
        var r = Eval(@"
            function* foo() {
                yield undefined;
            }
            var str = '';
            for (var val of foo()) str += val;
            str;
        ");

        r.AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Generator_is_its_own_iterator_via_symbol_iterator()
    {
        var r = Eval(@"
            function* g() { yield 1 }
            var it = g();
            it[Symbol.iterator]() === it
        ");
        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Generator_throw_is_catchable_in_body()
    {
        var r = Eval(@"
            function* g() {
                try { yield 1 } catch (e) { yield 'caught: ' + e }
            }
            var it = g();
            it.next();
            it.throw('boom').value
        ");
        r.AsString.Should().Be("caught: boom");
    }

    [TestMethod]
    public void Generator_done_after_explicit_return()
    {
        var r = Eval(@"
            function* g() { yield 1; return 'fin'; yield 2; }
            var it = g();
            var a = it.next();
            var b = it.next();
            var c = it.next();
            '' + a.value + ',' + b.value + '/' + b.done + ',' + c.value + '/' + c.done
        ");
        r.AsString.Should().Be("1,fin/true,undefined/true");
    }

    [TestMethod]
    public void Generator_resumes_with_values_sent_after_each_yield()
    {
        var r = Eval(@"
            function* counter(value) {
                while (true) {
                    const step = yield value++;
                    if (step) value += step;
                }
            }
            var it = counter(0);
            it.next().value + ',' +
                it.next().value + ',' +
                it.next().value + ',' +
                it.next().value + ',' +
                it.next(10).value + ',' +
                it.next().value + ',' +
                it.next(10).value;
        ");

        r.AsString.Should().Be("0,1,2,3,14,15,26");
    }

    [TestMethod]
    public void Generator_fibonacci_can_destructure_and_reset_from_next_value()
    {
        var r = Eval(@"
            function* fibonacci() {
                let current = 0;
                let next = 1;
                while (true) {
                    const reset = yield current;
                    [current, next] = [next, next + current];
                    if (reset) {
                        current = 0;
                        next = 1;
                    }
                }
            }
            var sequence = fibonacci();
            sequence.next().value + ',' +
                sequence.next().value + ',' +
                sequence.next().value + ',' +
                sequence.next().value + ',' +
                sequence.next().value + ',' +
                sequence.next().value + ',' +
                sequence.next().value + ',' +
                sequence.next(true).value + ',' +
                sequence.next().value + ',' +
                sequence.next().value;
        ");

        r.AsString.Should().Be("0,1,1,2,3,5,8,0,1,1");
    }

    [TestMethod]
    public void Generator_resume_does_not_reevaluate_binary_left_operand()
    {
        var r = Eval(@"
            function* gen() {
                let d = 0;
                const sum = (++d) + (yield 10);
                return [d, sum];
            }
            var g = gen();
            g.next();
            JSON.stringify(g.next(5).value);
        ");

        r.AsString.Should().Be("[1,6]");
    }

    [TestMethod]
    public void Generator_resume_does_not_reevaluate_call_arguments_before_yield()
    {
        var r = Eval(@"
            function* gen() {
                let i = 0;
                const foo = (a, b, c) => [a, b, c];
                const result = foo(++i, ++i, yield ++i);
                return [result, i];
            }
            var g = gen();
            g.next();
            JSON.stringify(g.next('done').value);
        ");

        r.AsString.Should().Be("[[1,2,\"done\"],3]");
    }

    [TestMethod]
    public void Generator_resume_preserves_switch_lexical_binding_after_yield()
    {
        var r = Eval(@"
            function* gen() {
                switch (1) {
                    case 1:
                        let x = 1;
                        yield;
                        return x;
                    default:
                        return 0;
                }
            }
            var g = gen();
            g.next();
            g.next().value;
        ");

        r.AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Generator_resume_does_not_reiterate_one_shot_spread_iterator()
    {
        var r = Eval(@"
            function* inner() { yield 'a'; yield 'b'; yield 'c'; }
            function* outer() {
                const g = inner();
                const result = [...g, yield 'wait'];
                return result;
            }
            var o = outer();
            o.next();
            JSON.stringify(o.next('d').value);
        ");

        r.AsString.Should().Be("[\"a\",\"b\",\"c\",\"d\"]");
    }

    [TestMethod]
    public void Generator_resume_preserves_nested_try_finally_stack()
    {
        var r = Eval(@"
            var log = '';
            function* g() {
                try {
                    try {
                        yield 'pause';
                    } finally {
                        log += 'inner';
                    }
                } finally {
                    log += '>outer';
                }
            }
            var it = g();
            var first = it.next();
            var ret = it.return('x');
            var done = it.next();
            first.value + ':' + first.done + '|' +
                log + '|' +
                ret.value + ':' + ret.done + '|' +
                done.value + ':' + done.done;
        ");

        r.AsString.Should().Be("pause:false|inner>outer|x:true|undefined:true");
    }

    [TestMethod]
    public void Generator_resume_preserves_operand_stack_across_nested_expression()
    {
        var r = Eval(@"
            function* g() {
                let i = 0;
                const result = ((++i) * (yield 'pause')) + (++i);
                return result + ':' + i;
            }
            var it = g();
            var first = it.next();
            var second = it.next(10);
            first.value + ':' + first.done + '|' +
                second.value + ':' + second.done;
        ");

        r.AsString.Should().Be("pause:false|12:2:true");
    }

    [TestMethod]
    public void Generator_throw_after_suspend_preserves_catch_scope()
    {
        var r = Eval(@"
            function* g() {
                let after = 'later';
                try {
                    yield 'pause';
                } catch (e) {
                    let local = 'caught:' + e;
                    yield local + ':' + after;
                }
                return 'done';
            }
            var it = g();
            var first = it.next();
            var second = it.throw('boom');
            var third = it.next();
            first.value + ':' + first.done + '|' +
                second.value + ':' + second.done + '|' +
                third.value + ':' + third.done;
        ");

        r.AsString.Should().Be("pause:false|caught:boom:later:false|done:true");
    }

    [TestMethod]
    public void Yield_star_delegates_to_inner_generator()
    {
        var r = Eval(@"
            function* a() { yield 1; yield 2 }
            function* b() { yield* a(); yield 3 }
            var out = '';
            for (var x of b()) out = out + x;
            out
        ");
        r.AsString.Should().Be("123");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
