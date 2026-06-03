using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests;

/// <summary>
/// Tests for update expressions (++ / --) applied to a PRIVATE member target:
/// <c>this.#x++</c>, <c>++this.#x</c>, <c>--this.#x</c>, <c>this.#x--</c>.
/// ECMAScript §13.4 Update Expressions.
///
/// Regression for BUG B3: <see cref="JsCompiler"/>'s EmitUpdate cast the
/// member property straight to <c>Identifier</c> in its non-computed arm, so a
/// <c>PrivateNameExpression</c> ("#x") threw <c>InvalidCastException</c> at
/// compile time. The fix routes private targets through PrivateGet/PrivateSet.
/// </summary>
[TestClass]
public class PrivateFieldUpdateTests
{
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-increment-operator", "13.4.4 Postfix Increment Operator")]
    [SpecFact]
    public void Postfix_increment_private_returns_old_then_mutates()
    {
        // First m() returns the OLD value (1) and mutates #x to 2; the second
        // m() reads 2 (and mutates to 3). No InvalidCastException at compile time.
        Eval(@"
            class C { #x = 1; m() { return this.#x++; } }
            let c = new C();
            let a = c.m();
            let b = c.m();
            a + ',' + b
        ").AsString.Should().Be("1,2");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-prefix-increment-operator", "13.4.2 Prefix Increment Operator")]
    [SpecFact]
    public void Prefix_increment_private_returns_new_value()
    {
        // ++this.#x: #x starts at 1, becomes 2, prefix returns the new value (2).
        Eval(@"
            class C { #x = 1; m() { return ++this.#x; } }
            new C().m()
        ").AsNumber.Should().Be(2);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-prefix-decrement-operator", "13.4.3 Prefix Decrement Operator")]
    [SpecFact]
    public void Prefix_decrement_private_returns_new_value()
    {
        // --this.#x: #x starts at 1, becomes 0, prefix returns the new value (0).
        Eval(@"
            class C { #x = 1; m() { return --this.#x; } }
            new C().m()
        ").AsNumber.Should().Be(0);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-postfix-decrement-operator", "13.4.5 Postfix Decrement Operator")]
    [SpecFact]
    public void Postfix_decrement_private_returns_old_then_mutates()
    {
        // this.#x--: postfix returns the OLD value (1) and mutates #x to 0; a
        // follow-up read confirms the mutation took effect.
        Eval(@"
            class C {
                #x = 1;
                m() { return this.#x--; }
                read() { return this.#x; }
            }
            let c = new C();
            let a = c.m();
            a + ',' + c.read()
        ").AsString.Should().Be("1,0");
    }

    // -----------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
