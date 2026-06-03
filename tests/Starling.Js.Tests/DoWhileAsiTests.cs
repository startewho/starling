using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// BUG B2 — do-while automatic semicolon insertion (ASI). Per ES2024
/// §12.10.1 rule 3 (the do-while-specific ASI rule), a semicolon is ALWAYS
/// inserted after the closing <c>)</c> of a do-while — even with no line
/// terminator and even when the next token is not <c>}</c> or EOF. So minified
/// code like <c>do x();while(c)return 1</c> must parse without a syntax error.
/// </summary>
[TestClass]
public class DoWhileAsiTests
{
    [TestMethod]
    public void DoWhile_then_return_no_separator_minified()
        // `return` immediately follows `)` with no `;` and no newline — the
        // exact shape from the bug report (`do x();while(c)return 1`). `x` is a
        // local no-op so the body runs once, then `c` is false and `return 1`.
        => Eval("function f(){var c=false;function x(){}do x();while(c)return 1}f();")
            .AsNumber.Should().Be(1);

    [TestMethod]
    public void DoWhile_then_break_no_separator_inside_loop()
        // `break` immediately follows `)`; the do-while ASI lets the break end
        // the iteration of the OUTER loop after one pass.
        => Eval("""
            function f(){
                var n=0;
                for(;;){ n++; do{}while(false)break }
                return n;
            }
            f();
            """).AsNumber.Should().Be(1);

    [TestMethod]
    public void DoWhile_explicit_semicolon_still_works()
        // Regression: the ordinary explicit-`;` form must still parse and run.
        => Eval("function f(){var i=0;do{i++;}while(i<3);return i;}f();")
            .AsNumber.Should().Be(3);

    [TestMethod]
    public void DoWhile_then_statement_on_next_line_via_newline_asi()
        // Newline form: ASI on a line terminator after `)`.
        => Eval("""
            function f(){
                var i=0;
                do { i++; } while(i<2)
                return i;
            }
            f();
            """).AsNumber.Should().Be(2);

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
