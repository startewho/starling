using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-04c2 — a local declared inside a class method / accessor /
/// constructor body that is captured by a nested closure AND mutated must be
/// boxed into a shared <see cref="Cell"/>, exactly like a local in a plain
/// function body. Before this fix the class-template compile path skipped the
/// mutated-capture → Cell promotion that <see cref="JsCompiler"/> runs for
/// plain function bodies, so the nested closure performed a cell op on a raw
/// Number and the VM threw "value is Number, not Object". These tests pin the
/// live-binding semantics for class-member bodies, while keeping the
/// already-working read-only capture path green.
/// </summary>
[TestClass]
public class MethodCaptureCellTests
{
    [TestMethod]
    public void Method_body_mutated_capture_through_arrow_returns_live_value()
    {
        // The minimal repro from the WP. Threw "value is Number, not Object"
        // before the fix; should return 2.
        Eval(@"
            class C { m(){ let i = 0; let f = () => i++; f(); f(); return i; } }
            new C().m();
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Method_body_let_mutated_through_object_method_increments()
    {
        // object-method `inc()` captures + mutates a class-method-body `let`.
        Eval(@"
            class C { m(){ let i = 0; return { inc(){ return i++; } }; } }
            let o = new C().m();
            o.inc();          // returns 0, i becomes 1
            o.inc();          // returns 1, i becomes 2
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Symbol_iterator_method_capture_sums_with_for_of()
    {
        // The shape that surfaced this bug: an iterator factory whose next()
        // mutates the captured `i`. Before the fix this threw
        // "value is Number, not Object"; now it runs.
        //
        // The WP text says "expected 6", but that is a miscount: with
        // `done: i++ >= 3`, value is read before the post-increment, so the
        // for-of consumes values 0, 1, 2 (the 4th result has done:true and its
        // value is discarded) → sum 3. Verified against Node:
        //   `for (const v of new C()) s += v` ⇒ 3.
        Eval(@"
            class C { [Symbol.iterator](){ let i=0; return { next(){ return { value:i, done:i++>=3 }; } }; } }
            let s=0;
            for (const v of new C()) s+=v;
            s;
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Method_body_read_only_capture_still_works()
    {
        // Control: a class-method local captured READ-ONLY already worked and
        // must stay working (no regression).
        Eval(@"
            class C { m(){ let x = 5; return { get(){ return x + x; } }; } }
            new C().m().get();
        ").AsNumber.Should().Be(10);
    }

    [TestMethod]
    public void Getter_body_mutated_capture_propagates()
    {
        Eval(@"
            class C {
                get value(){
                    let n = 0;
                    let bump = () => { n += 3; };
                    bump(); bump();
                    return n;
                }
            }
            new C().value;
        ").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Setter_body_mutated_capture_propagates()
    {
        Eval(@"
            let out = 0;
            class C {
                set value(v){
                    let acc = v;
                    let add = () => { acc += v; };
                    add(); add();
                    out = acc;
                }
            }
            let c = new C();
            c.value = 5;     // acc: 5 -> 10 -> 15
            out;
        ").AsNumber.Should().Be(15);
    }

    [TestMethod]
    public void Constructor_body_mutated_capture_propagates()
    {
        Eval(@"
            class C {
                constructor(){
                    let i = 0;
                    let f = () => i++;
                    f(); f(); f();
                    this.count = i;
                }
            }
            new C().count;
        ").AsNumber.Should().Be(3);
    }

    [TestMethod]
    public void Constructor_param_captured_and_mutated_propagates()
    {
        // A captured + mutated CONSTRUCTOR PARAMETER must be promoted to a Cell
        // (PromoteParamCell), same as a plain function parameter.
        Eval(@"
            class C {
                constructor(x){
                    let add = (y) => { x = x + y; return x; };
                    add(1); add(2);
                    this.total = x;
                }
            }
            new C(10).total;
        ").AsNumber.Should().Be(13);
    }

    [TestMethod]
    public void Method_param_captured_and_mutated_propagates()
    {
        // Captured + mutated METHOD PARAMETER promotes to a Cell too.
        Eval(@"
            class C {
                m(x){
                    let add = (y) => { x = x + y; return x; };
                    add(1); add(2); add(3);
                    return x;
                }
            }
            new C().m(10);
        ").AsNumber.Should().Be(16);
    }

    [TestMethod]
    public void Field_initializer_mutated_capture_propagates()
    {
        // Field initializers run as their own thunk; a captured + mutated local
        // inside one must also box to a Cell.
        Eval(@"
            class C {
                v = (() => { let i = 0; let f = () => i++; f(); f(); return i; })();
            }
            new C().v;
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Static_block_mutated_capture_propagates()
    {
        // Inside a static block `this` is the class itself. (Referencing the
        // class by its name `C` from a static block is a separate, pre-existing
        // class-name-binding limitation — the name is written to the global
        // only after BuildClass runs the static blocks — so this test uses
        // `this`, which already works, to isolate the capture-cell behavior.)
        Eval(@"
            class C {
                static result;
                static {
                    let i = 0;
                    let f = () => i++;
                    f(); f(); f(); f();
                    this.result = i;
                }
            }
            C.result;
        ").AsNumber.Should().Be(4);
    }

    [TestMethod]
    public void Inner_function_declaration_in_method_captures_and_mutates()
    {
        // A nested function declaration (not arrow) inside a method body that
        // mutates the captured let — exercises the HoistFunctionDeclarations
        // path inside a class member body.
        Eval(@"
            class C {
                m(){
                    let x = 1;
                    function inc(){ x++; }
                    inc(); inc(); inc();
                    return x;
                }
            }
            new C().m();
        ").AsNumber.Should().Be(4);
    }

    [TestMethod]
    public void Method_var_captured_and_mutated_through_function_decl()
    {
        // A captured + mutated method-top `var` promotes to a Cell, mirroring
        // the plain-function counter pattern.
        Eval(@"
            class C {
                m(){
                    var total = 0;
                    function add(n){ total = total + n; }
                    add(2); add(3); add(5);
                    return total;
                }
            }
            new C().m();
        ").AsNumber.Should().Be(10);
    }

    // ----------------------------------------------------- Helpers

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
