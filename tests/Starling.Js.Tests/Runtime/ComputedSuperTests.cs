using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-04h — end-to-end (parse → compile → run) coverage for computed
/// super-property access/assignment: <c>super[expr]</c>, <c>super[expr](args)</c>,
/// and <c>super[expr] = v</c> inside class methods/accessors. Reads resolve
/// through the home object's prototype with <c>this</c> as the receiver; writes
/// (per spec super-set semantics, §13.3.4) target <c>this</c>. Existing
/// <c>super.name</c> behavior must stay unchanged.
/// </summary>
[TestClass]
public class ComputedSuperTests
{
    [TestMethod]
    public void Computed_super_method_call_resolves_to_base_method()
    {
        Eval(@"
            class A { greet() { return 'a'; } }
            class B extends A {
                greet() {
                    const k = 'gr' + 'eet';
                    return super[k]() + 'b';
                }
            }
            new B().greet();
        ").AsString.Should().Be("ab");
    }

    [TestMethod]
    public void Computed_super_method_call_forwards_arguments()
    {
        Eval(@"
            class A { add(x, y) { return x + y; } }
            class B extends A {
                add(x, y) {
                    const k = 'add';
                    return super[k](x, y) * 10;
                }
            }
            new B().add(2, 3);
        ").AsNumber.Should().Be(50);
    }

    [TestMethod]
    public void Computed_super_reads_data_property_from_prototype()
    {
        Eval(@"
            class A { constructor() {} }
            A.prototype.tag = 'base';
            class B extends A {
                read() { const k = 'tag'; return super[k]; }
            }
            new B().read();
        ").AsString.Should().Be("base");
    }

    [TestMethod]
    public void Computed_super_read_uses_this_as_getter_receiver()
    {
        // A getter on the base prototype must see `this` (the B instance), not
        // the prototype object, as its receiver.
        Eval(@"
            class A { get who() { return this.name; } }
            class B extends A {
                constructor() { super(); this.name = 'derived'; }
                read() { const k = 'who'; return super[k]; }
            }
            new B().read();
        ").AsString.Should().Be("derived");
    }

    [TestMethod]
    public void Computed_super_assignment_writes_to_this_not_prototype()
    {
        // Per spec, super[k] = v sets the property on the receiver (this), so
        // the base prototype must remain untouched and the own property lands
        // on the instance.
        Eval(@"
            class A {}
            class B extends A {
                set() { const k = 'x'; super[k] = 41; return this.x; }
            }
            var b = new B();
            var v = b.set();
            v === 41 && b.hasOwnProperty('x') && !A.prototype.hasOwnProperty('x');
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Computed_super_assignment_returns_assigned_value()
    {
        Eval(@"
            class A {}
            class B extends A {
                set() { const k = 'y'; return (super[k] = 7); }
            }
            new B().set();
        ").AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Computed_super_compound_assignment_reads_proto_writes_this()
    {
        // super[k] += v: read resolves through the prototype, the result is
        // written back onto `this`.
        Eval(@"
            class A {}
            A.prototype.n = 10;
            class B extends A {
                bump() { const k = 'n'; super[k] += 5; return this.n; }
            }
            var b = new B();
            var v = b.bump();
            v === 15 && b.hasOwnProperty('n') && A.prototype.n === 10;
        ").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Computed_super_with_symbol_key()
    {
        Eval(@"
            const sym = Symbol('marker');
            class A {}
            A.prototype[sym] = 'sym-value';
            class B extends A {
                read() { return super[sym]; }
            }
            new B().read();
        ").AsString.Should().Be("sym-value");
    }

    [TestMethod]
    public void Computed_super_in_static_method_resolves_against_base_constructor()
    {
        Eval(@"
            class A { static make() { return 'A.make'; } }
            class B extends A {
                static make() {
                    const k = 'make';
                    return super[k]() + '+B';
                }
            }
            B.make();
        ").AsString.Should().Be("A.make+B");
    }

    [TestMethod]
    public void Computed_super_key_with_numeric_coercion()
    {
        // Numeric keys flow through ToPropertyKey → string just like object
        // computed access.
        Eval(@"
            class A {}
            A.prototype[0] = 'zero';
            class B extends A {
                read() { return super[0]; }
            }
            new B().read();
        ").AsString.Should().Be("zero");
    }

    // ---- regression: non-computed super.name must still work ----

    [TestMethod]
    public void Regression_super_dot_name_method_call_still_works()
    {
        Eval(@"
            class A { greet() { return 'a'; } }
            class B extends A { greet() { return super.greet() + 'b'; } }
            new B().greet();
        ").AsString.Should().Be("ab");
    }

    [TestMethod]
    public void Regression_super_dot_name_read_still_works()
    {
        Eval(@"
            class A { get who() { return 'base'; } }
            class B extends A { read() { return super.who; } }
            new B().read();
        ").AsString.Should().Be("base");
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
