using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// §12.10: the <c>?.</c> OptionalChainingPunctuator does NOT apply when the dot
/// is immediately followed by a decimal digit, so a conditional like
/// <c>c ? .5 : .25</c> still parses (ternary + leading-dot numbers). Regular
/// optional chaining (<c>a?.b</c>, <c>a?.[i]</c>, <c>a?.(x)</c>) is unaffected.
/// Real-world minified bundles (mcmaster.com) emit <c>cond?.5:…</c>.
/// </summary>
[TestClass]
public class JsOptionalChainingDigitTests
{
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#prod-OptionalChainingPunctuator", "12.10 Punctuators")]
    public void Conditional_with_leading_dot_numbers_is_not_optional_chaining()
        => Eval("var c=true; c?.5:.25").AsNumber.Should().Be(0.5);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#prod-OptionalChainingPunctuator", "12.10 Punctuators")]
    public void Conditional_false_branch_leading_dot_number()
        => Eval("var c=false; c?.5:.25").AsNumber.Should().Be(0.25);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_property_access_still_works()
        => Eval("var a={b:7}; a?.b").AsNumber.Should().Be(7);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_computed_access_still_works()
        => Eval("var a=[9]; a?.[0]").AsNumber.Should().Be(9);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_call_still_works()
        => Eval("var a={f:function(){return 3;}}; a?.f()").AsNumber.Should().Be(3);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_chain_short_circuits_on_nullish()
        => Eval("var a=null; typeof (a?.b)").AsString.Should().Be("undefined");

    // Regression (angular.dev `e.features?.forEach(...)`): an optional METHOD
    // call must short-circuit the whole call — including evaluating the
    // arguments — when the base is nullish, instead of loading the method off
    // the nullish base and throwing "not a function".
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_method_call_short_circuits_on_nullish_base()
        => Eval("var a={b:null}; typeof (a.b?.forEach(function(x){return x;}))").AsString.Should().Be("undefined");

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_method_call_invokes_on_present_base()
        => Eval("var s=0; var a={b:[1,2,3]}; a.b?.forEach(function(x){s+=x;}); s").AsNumber.Should().Be(6);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_method_call_does_not_evaluate_args_on_nullish_base()
        => Eval("var n=0; var a={b:null}; a.b?.forEach((n=n+1)); n").AsNumber.Should().Be(0);

    // Regression (angular.dev `yR?.(e)`): an optional CALL must short-circuit to
    // undefined when the callee itself is nullish.
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_function_call_short_circuits_on_nullish_callee()
        => Eval("var f=null; typeof (f?.(1,2))").AsString.Should().Be("undefined");

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-optional-chains", "13.3.9 Optional Chains")]
    public void Optional_function_call_invokes_when_present()
        => Eval("var f=function(x){return x*2;}; f?.(5)").AsNumber.Should().Be(10);

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
