using Starling.Js.Bytecode;
using Starling.Js.Parse;
namespace Starling.Js.Tests;

/// <summary>
/// Async function EXPRESSIONS in every expression position, and the
/// immediately-invoked async function expression `async function(){…}(args)`.
/// github's minified bundles use these heavily; the engine previously parsed an
/// async function expression only at the assignment level and returned it
/// without a call/member tail, so `!async function(){}` and async IIFEs failed.
/// </summary>
[TestClass]
public class AsyncFunctionExpressionTests
{
    private static void ParseAndCompile(string source)
        => JsCompiler.Compile(new JsParser(source).ParseProgram(), "<test>");

    [TestMethod]
    public void Async_function_expression_as_unary_operand_parses()
    {
        // Reached via ParseUnary -> ParseCallMember -> ParsePrimary.
        ParseAndCompile("!async function(){ return 1; };");
        ParseAndCompile("void async function(){};");
    }

    [TestMethod]
    public void Immediately_invoked_async_function_attaches_its_call()
    {
        // The call tail must attach instead of orphaning '(' — these are the
        // shapes that failed across github's 28000/behaviors/landing-pages/sessions.
        ParseAndCompile("(async function(){ return 1; }());");        // parenthesized async IIFE
        ParseAndCompile("(async function(){ return 1; }(0));");       // with an arg
        ParseAndCompile("var f = async function(){}();");             // assigned async IIFE
        ParseAndCompile("(async function(){}(), () => {});");         // async IIFE then arrow in a sequence
    }

    [TestMethod]
    public void Async_function_expression_member_and_call_chains()
    {
        ParseAndCompile("(async function(){}).call(null);");
        ParseAndCompile("var p = async function(){}().then(x => x);");
    }
}
