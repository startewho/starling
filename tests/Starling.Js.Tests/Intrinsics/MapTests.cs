using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>Map</c> (B3-3).</summary>
[TestClass]
public class MapTests
{
    [TestMethod]
    public void Empty_map_has_size_zero_and_constructor_wired()
    {
        var rt = new JsRuntime();
        var Map_ = rt.GetGlobal("Map");
        Map_.IsObject.Should().BeTrue();
        rt.Realm.MapConstructor.Should().BeSameAs(Map_.AsObject);

        Eval("new Map().size;").AsNumber.Should().Be(0);
    }

    [TestMethod]
    public void Map_called_without_new_throws()
    {
        Eval(@"
            var ok = false;
            try {
                var m = new Map();
                Map.call(m, []);
            } catch (e) {
                ok = e instanceof TypeError && e.message === ""Constructor Map requires 'new'"";
            }
            ok;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Map_from_iterable_of_entries()
    {
        Eval("new Map([[1,'a'],[2,'b']]).get(1);").AsString.Should().Be("a");
        Eval("new Map([[1,'a'],[2,'b']]).get(2);").AsString.Should().Be("b");
        Eval("new Map([[1,'a'],[2,'b']]).size;").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Constructor_iterable_initialization_calls_observable_set_method()
    {
        var r = Eval(@"
            var calls = [];
            class M extends Map {
                set(k, v) {
                    calls.push(k + ':' + v);
                    return Map.prototype.set.call(this, k, v);
                }
            }
            var m = new M([[1, 'a'], [2, 'b']]);
            calls.join('|') + ';' + m.size + ';' + m.get(2);");

        r.AsString.Should().Be("1:a|2:b;2;b");
    }

    [TestMethod]
    public void Constructor_closes_iterator_when_set_method_throws()
    {
        var r = Eval(@"
            var closed = false;
            var index = 0;
            var iterable = {
                [Symbol.iterator]: function() {
                    return {
                        next: function() {
                            index++;
                            return index === 1
                                ? { done: false, value: [1, 2] }
                                : { done: true };
                        },
                        return: function() {
                            closed = true;
                            return {};
                        }
                    };
                }
            };
            class M extends Map {
                set() { throw new TypeError('stop'); }
            }
            try { new M(iterable); } catch (e) {}
            closed;");

        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Set_returns_the_map_and_chains()
    {
        Eval("new Map().set('a', 1).set('b', 2).size;").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Has_delete_clear_work()
    {
        var src = @"
            var m = new Map();
            m.set('a', 1);
            m.set('b', 2);
            var r = m.has('a') + ',' + m.has('z');
            m.delete('a');
            r = r + ',' + m.has('a') + ',' + m.size;
            m.clear();
            r = r + ',' + m.size;
            r;";
        Eval(src).AsString.Should().Be("true,false,false,1,0");
    }

    [TestMethod]
    public void Insertion_order_preserved_for_keys()
    {
        var r = Eval(@"
            var m = new Map();
            m.set(2,'b');
            m.set(1,'a');
            var out = '';
            var it = m.keys();
            var n = it.next(); out += n.value;
            n = it.next(); out += ',' + n.value;
            n = it.next(); out += ',' + (n.done ? 'done' : n.value);
            out;");
        r.AsString.Should().Be("2,1,done");
    }

    [TestMethod]
    public void NaN_keys_collapse_to_one_entry()
    {
        var r = Eval(@"
            var m = new Map();
            m.set(NaN, 1);
            m.set(NaN, 2);
            m.size + ':' + m.get(NaN);");
        r.AsString.Should().Be("1:2");
    }

    [TestMethod]
    public void Plus_zero_and_minus_zero_are_the_same_key()
    {
        var r = Eval(@"
            var m = new Map();
            m.set(+0, 'p');
            m.set(-0, 'm');
            m.size + ':' + m.get(0);");
        r.AsString.Should().Be("1:m");
    }

    [TestMethod]
    public void Negative_zero_key_is_observed_as_positive_zero()
    {
        var r = Eval(@"
            var map = new Map();
            map.set(-0, 'foo');
            var reciprocal;
            map.forEach(function(value, key) {
                reciprocal = 1 / key;
            });
            reciprocal === Infinity && map.get(+0) === 'foo';");

        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void ForEach_invokes_callback_with_value_key_map()
    {
        var r = Eval(@"
            var m = new Map();
            m.set('a', 1); m.set('b', 2);
            var out = '';
            m.forEach(function(v, k) { out += k + '=' + v + ';'; });
            out;");
        r.AsString.Should().Be("a=1;b=2;");
    }

    [TestMethod]
    public void For_of_over_entries_yields_pairs()
    {
        var r = Eval(@"
            var m = new Map();
            m.set('a', 1); m.set('b', 2);
            var out = '';
            for (var pair of m.entries()) { out += pair[0] + '=' + pair[1] + ';'; }
            out;");
        r.AsString.Should().Be("a=1;b=2;");
    }

    [TestMethod]
    public void For_of_over_map_uses_entries_via_symbol_iterator()
    {
        var r = Eval(@"
            var m = new Map();
            m.set('a', 1); m.set('b', 2);
            var out = '';
            for (var pair of m) { out += pair[0] + '=' + pair[1] + ';'; }
            out;");
        r.AsString.Should().Be("a=1;b=2;");
    }

    [TestMethod]
    public void Map_iterator_has_proper_iterator_prototype_chain()
    {
        var r = Eval(@"
            var iterator = new Map()[Symbol.iterator]();
            var proto1 = Object.getPrototypeOf(iterator);
            var proto2 = Object.getPrototypeOf(proto1);
            proto2.hasOwnProperty(Symbol.iterator) + ','
                + proto1.hasOwnProperty(Symbol.iterator) + ','
                + iterator.hasOwnProperty(Symbol.iterator) + ','
                + (iterator[Symbol.iterator]() === iterator);");

        r.AsString.Should().Be("true,false,false,true");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
