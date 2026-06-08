using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>WeakSet</c> (B3-3).</summary>
[TestClass]
public class WeakSetTests
{
    [TestMethod]
    public void WeakSet_constructor_wired()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("WeakSet").IsObject.Should().BeTrue();
        rt.Realm.WeakSetConstructor.Should().NotBeNull();
    }

    [TestMethod]
    public void WeakSet_called_without_new_throws()
    {
        Eval(@"
            var ok = false;
            try {
                var s = new WeakSet();
                WeakSet.call(s, []);
            } catch (e) {
                ok = e instanceof TypeError && e.message === ""Constructor WeakSet requires 'new'"";
            }
            ok;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Add_and_has_for_object_key()
    {
        Eval(@"
            var s = new WeakSet();
            var k = {};
            s.add(k);
            s.has(k);").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Constructor_iterable_initialization_calls_observable_add_method()
    {
        var r = Eval(@"
            var key = {};
            var calls = 0;
            class W extends WeakSet {
                add(v) {
                    calls++;
                    return WeakSet.prototype.add.call(this, v);
                }
            }
            var w = new W([key]);
            calls + ';' + w.has(key);");

        r.AsString.Should().Be("1;true");
    }

    [TestMethod]
    public void Constructor_closes_iterator_when_add_method_throws()
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
                                ? { done: false, value: key }
                                : { done: true };
                        },
                        return: function() {
                            closed = true;
                            return {};
                        }
                    };
                }
            };
            class W extends WeakSet {
                add() { throw new TypeError('stop'); }
            }
            try { new W(iterable); } catch (e) {}
            closed;");

        r.AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Primitive_throws_TypeError_on_add()
    {
        Eval(@"
            var ok = false;
            try { new WeakSet().add(1); }
            catch (e) { ok = e instanceof TypeError; }
            ok;").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Delete_removes_membership()
    {
        var r = Eval(@"
            var s = new WeakSet();
            var k = {};
            s.add(k);
            var before = s.has(k);
            s.delete(k);
            before + ',' + s.has(k);");
        r.AsString.Should().Be("true,false");
    }

    [TestMethod]
    public void No_size_and_no_iteration()
    {
        Eval("typeof (new WeakSet()).size;").AsString.Should().Be("undefined");
        Eval("typeof (new WeakSet())[Symbol.iterator];").AsString.Should().Be("undefined");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
