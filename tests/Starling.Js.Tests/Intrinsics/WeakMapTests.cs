using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>WeakMap</c> (B3-3).</summary>
[TestClass]
public class WeakMapTests
{
    [TestMethod]
    public void WeakMap_constructor_wired()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("WeakMap").IsObject.Should().BeTrue();
        rt.Realm.WeakMapConstructor.Should().NotBeNull();
    }

    [TestMethod]
    public void WeakMap_called_without_new_throws()
    {
        Eval(@"
            var ok = false;
            try {
                var m = new WeakMap();
                WeakMap.call(m, []);
            } catch (e) {
                ok = e instanceof TypeError && e.message === ""Constructor WeakMap requires 'new'"";
            }
            ok;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Set_get_with_object_key()
    {
        Eval(@"
            var m = new WeakMap();
            var k = {};
            m.set(k, 7);
            m.get(k);").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Constructor_iterable_initialization_calls_observable_set_method()
    {
        var r = Eval(@"
            var key = {};
            var calls = [];
            class W extends WeakMap {
                set(k, v) {
                    calls.push(v);
                    return WeakMap.prototype.set.call(this, k, v);
                }
            }
            var w = new W([[key, 7]]);
            calls.join('|') + ';' + w.get(key);");

        r.AsString.Should().Be("7;7");
    }

    [TestMethod]
    public void Constructor_closes_iterator_when_set_method_throws()
    {
        var r = Eval(@"
            var key = {};
            var closed = false;
            var index = 0;
            var iterable = {
                [Symbol.iterator]: function() {
                    return {
                        next: function() {
                            index++;
                            return index === 1
                                ? { done: false, value: [key, 7] }
                                : { done: true };
                        },
                        return: function() {
                            closed = true;
                            return {};
                        }
                    };
                }
            };
            class W extends WeakMap {
                set() { throw new TypeError('stop'); }
            }
            try { new W(iterable); } catch (e) {}
            closed;");

        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Primitive_key_on_set_throws_TypeError()
    {
        Eval(@"
            var ok = false;
            try { new WeakMap().set(1, 'x'); }
            catch (e) { ok = e instanceof TypeError; }
            ok;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Has_and_delete()
    {
        var r = Eval(@"
            var m = new WeakMap();
            var k = {};
            m.set(k, 1);
            var out = m.has(k);
            m.delete(k);
            out + ',' + m.has(k);");
        r.AsString.Should().Be("true,false");
    }

    [TestMethod]
    public void No_size_and_no_symbol_iterator()
    {
        // Spec: WeakMap.prototype has no `size` and no @@iterator. Plain
        // member access returns undefined.
        Eval("typeof (new WeakMap()).size;").AsString.Should().Be("undefined");
        Eval("typeof (new WeakMap())[Symbol.iterator];").AsString.Should().Be("undefined");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
