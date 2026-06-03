using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// BUG B4 — §13.3 'MemberExpression : MemberExpression [ Expression ]' uses
/// <c>Expression</c>, which INCLUDES the comma/sequence operator. So a computed
/// member / index key may itself be a SequenceExpression: <c>a[b, c]</c>,
/// <c>obj[x, y]</c>, <c>arr[i, j] = 1</c>, and the optional-chain form
/// <c>a?.[b, c]</c>. The comma operator evaluates each operand left-to-right and
/// yields its LAST value, so the bracket key resolves to that last value.
/// </summary>
[TestClass]
public class ComputedMemberCommaTests
{
    [TestMethod]
    public void Computed_index_with_comma_uses_last_value()
        // `a[0, 2]` — comma yields 2, so the index is 2 → a[2] === 30.
        => Eval("let a=[10,20,30]; a[0,2]").AsNumber.Should().Be(30);

    [TestMethod]
    public void Computed_key_with_comma_uses_last_value()
        // `o[k1, k2]` — comma yields k2 ("x"), so o["x"] === 1.
        => Eval("let o={x:1}; let k1=\"z\",k2=\"x\"; o[k1,k2]").AsNumber.Should().Be(1);

    [TestMethod]
    public void Optional_computed_index_with_comma_uses_last_value()
        // `a?.[0, 2]` — optional chain, comma yields 2 → a[2] === 30.
        => Eval("let a=[10,20,30]; a?.[0,2]").AsNumber.Should().Be(30);

    [TestMethod]
    public void Computed_assignment_target_with_comma_uses_last_value()
        // `m[0, 1] = 7` — comma yields 1, so the write lands on m[1].
        => Eval("let m=[0,0,0]; m[0,1]=7; m[1]").AsNumber.Should().Be(7);

    // ----- Regression: a normal single-index key still works -----

    [TestMethod]
    public void Plain_single_index_still_works()
        => Eval("let a=[10,20,30]; a[1]").AsNumber.Should().Be(20);

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
