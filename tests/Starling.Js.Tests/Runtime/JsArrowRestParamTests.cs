using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Rest parameters in the parenthesized (concise) form of a *synchronous*
/// arrow function — <c>(...a) =&gt; …</c> per §15.3 ArrowFunction /
/// ArrowFormalParameters. The async arrow path and ordinary function
/// declarations already accepted a trailing rest element; the sync arrow path
/// parsed the parentheses as a cover grouping/sequence expression first and
/// threw on the leading <c>...</c> before the <c>=&gt;</c> was ever reached.
///
/// Reproduces the mcmaster.com app-bundle blocker: a wrapper of the shape
/// <c>(t, n) =&gt; (...a) =&gt; t(n(...a))</c> failed to parse with
/// "unexpected token Ellipsis '...'". Rest now lifts to the same
/// <c>SpreadElement</c> param node the compiler's rest handling already keys
/// on, so call-time argument collection works identically to function rest
/// params.
/// </summary>
[TestClass]
public class JsArrowRestParamTests
{
    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Sole_rest_param_collects_all_args()
        => Eval("var g = (...a) => a.length; g(1,2,3)").AsNumber.Should().Be(3);

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Sole_rest_param_with_no_args_is_empty()
        => Eval("var g = (...a) => a.length; g()").AsNumber.Should().Be(0);

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Rest_param_array_is_a_real_array()
        => Eval("var g = (...a) => a.join('-'); g('x','y','z')").AsString.Should().Be("x-y-z");

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Leading_fixed_param_then_rest()
        => Eval("var g = (a, ...b) => b.length; g(1,2,3,4)").AsNumber.Should().Be(3);

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Leading_fixed_param_binds_separately_from_rest()
        => Eval("var g = (a, ...b) => a + ':' + b.join(','); g(10,20,30)")
            .AsString.Should().Be("10:20,30");

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Nested_arrow_rest_with_call_spread_bundle_shape()
        // The exact mcmaster app-bundle shape: a curried wrapper whose inner
        // arrow takes a rest param and forwards it via call-spread.
        => Eval("var w = (t, n) => (...a) => t(n(...a));"
              + "var f = w(x => x + 1, (p, q) => p * q);"
              + "f(3, 4)").AsNumber.Should().Be(13);

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Empty_param_arrow_still_works()
        => Eval("var g = () => 1; g()").AsNumber.Should().Be(1);

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Two_fixed_param_arrow_still_works()
        => Eval("var g = (a, b) => a + b; g(2, 3)").AsNumber.Should().Be(5);

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFunction", "15.3")]
    [SpecFact]
    public void Single_paren_param_arrow_still_works()
        => Eval("var g = (a) => a * 2; g(21)").AsNumber.Should().Be(42);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-comma-operator", "13.16")]
    [SpecFact]
    public void Bare_parenthesized_sequence_is_not_an_arrow()
        // Disambiguation guard: a parenthesized comma expression with no
        // trailing `=>` must remain a SequenceExpression evaluating to its
        // last element, never get reinterpreted as arrow parameters.
        => Eval("(1, 2)").AsNumber.Should().Be(2);

    [Spec("ecma262", "https://tc39.es/ecma262/#prod-ArrowFormalParameters", "15.3")]
    [SpecFact]
    public void Rest_param_with_default_leading_param()
        => Eval("var g = (a = 5, ...b) => a + b.length; g()").AsNumber.Should().Be(5);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-function-definitions-runtime-semantics-iteratorbindinginitialization", "10.2.11")]
    [SpecFact]
    public void Function_declaration_rest_param_now_collects_args()
        // The same rest-collection path also fixes ordinary function rest
        // params, which previously parsed but bound `undefined` at runtime.
        => Eval("function f(...a){ return a.length; } f(1,2,3)").AsNumber.Should().Be(3);

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-function-definitions-runtime-semantics-iteratorbindinginitialization", "10.2.11")]
    [SpecFact]
    public void Function_declaration_leading_param_then_rest()
        => Eval("function f(a, ...b){ return a + ':' + b.join(','); } f(1,2,3)")
            .AsString.Should().Be("1:2,3");

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
