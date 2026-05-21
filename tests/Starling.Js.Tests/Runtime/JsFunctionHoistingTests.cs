using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Function declarations nested inside a function *expression*, arrow, or IIFE
/// must be hoisted and bound as locals of that function — exactly as for a
/// function-declaration body (§14.1.18 / §10.2.11 FunctionDeclarationInstantiation).
/// Regression for the bug where such declarations were never bound, so calls to
/// them compiled to (undefined) global lookups — breaking all IIFE/webpack code.
/// </summary>
[TestClass]
public class JsFunctionHoistingTests
{
    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Decl_inside_function_expression_is_callable()
        => Eval("var f=function(){function g(x){return x+1;}return g(41);}; f()").AsNumber.Should().Be(42);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Decl_inside_iife_is_callable()
        => Eval("(function(){function g(x){return x+1;}return g(41);})()").AsNumber.Should().Be(42);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Decl_inside_arrow_body_is_callable()
        => Eval("(()=>{function g(x){return x+1;}return g(41);})()").AsNumber.Should().Be(42);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Decl_in_expression_is_hoisted_before_its_textual_position()
        => Eval("(function(){var a=g(41);function g(x){return x+1;}return a;})()").AsNumber.Should().Be(42);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Decl_in_expression_closes_over_outer_var()
        => Eval("(function(){var x=0;function set(){x=5;}set();return x;})()").AsNumber.Should().Be(5);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Webpack_shape_require_passed_into_module_via_call()
        => Eval(@"(function(mods){
                    function req(i){var m={exports:{}};mods[i].call(m.exports,m,m.exports,req);return m.exports;}
                    return req(0);
                  })([function(module,exports,req){exports.v=42;}]).v").AsNumber.Should().Be(42);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Sibling_function_can_call_a_later_defined_sibling_inside_a_function_scope()
        // Forward reference between sibling function declarations: `a` calls `b`
        // which is declared AFTER it. All sibling names must be hoisted before
        // any body is compiled, else `b` compiles to an (undefined) global.
        // (Real-world: Google Analytics' gtag.js does exactly this.)
        => Eval("(function(){ function a(){ return b(); } function b(){ return 9; } return a(); })()")
            .AsNumber.Should().Be(9);

    [SpecFact]
    [Spec("ecma262", "https://tc39.es/ecma262/#sec-functiondeclarationinstantiation", "10.2.11 FunctionDeclarationInstantiation")]
    public void Multiple_siblings_forward_reference_a_last_defined_sibling()
        => Eval("(function(){ function m(){return n();} function o(){return n();} function n(){return 5;} return m()+o(); })()")
            .AsNumber.Should().Be(10);

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
