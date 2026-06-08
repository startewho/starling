using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// Pins the fast @@replace path (built-in, non-sticky, no named captures):
/// literal replacements delegate to a single-pass backend replace, $-patterns
/// and functional replacements go through the span-based builder. The
/// empty-match cases guard against the .NET Regex.Replace delegation diverging
/// from JS's §22.2.6.11 empty-match advance.
/// </summary>
[TestClass]
public class RegExpReplaceFastPathTests
{
    private static string Run(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return JsValue.ToStringValue(new JsVm(new JsRuntime()).Run(chunk));
    }

    [TestMethod] public void LiteralGlobal() => Run("'the quick brown fox'.replace(/o/g, '0')").Should().Be("the quick br0wn f0x");
    [TestMethod] public void LiteralNonGlobalFirstOnly() => Run("'aaa'.replace(/a/, 'b')").Should().Be("baa");
    [TestMethod] public void EmptyPatternGlobal() => Run("'abc'.replace(/(?:)/g, '-')").Should().Be("-a-b-c-");
    [TestMethod] public void StarEmptyGlobal() => Run("'abc'.replace(/x*/g, '-')").Should().Be("-a-b-c-");
    [TestMethod] public void EmptyPatternNonGlobal() => Run("'abc'.replace(/(?:)/, '-')").Should().Be("-abc");
    [TestMethod] public void EmptyStringStar() => Run("''.replace(/x*/g, '-')").Should().Be("-");
    [TestMethod] public void WhitespaceClassGlobal() => Run("'a b c'.replace(/\\s/g, '_')").Should().Be("a_b_c");
    [TestMethod] public void DigitClassGlobal() => Run("'a1b2c3'.replace(/[0-9]/g, '#')").Should().Be("a#b#c#");
    [TestMethod] public void NoMatchReturnsOriginal() => Run("'abc'.replace(/z/g, 'X')").Should().Be("abc");
    [TestMethod] public void TrailingEmptyAfterMatch() => Run("'abc'.replace(/c|$/g, '-')").Should().Be("ab--");

    // $-patterns must keep going through the span-based GetSubstitutionFast.
    [TestMethod] public void DollarAmpersand() => Run("'abc'.replace(/b/, '[$&]')").Should().Be("a[b]c");
    [TestMethod] public void DollarCapture() => Run("'2024-01'.replace(/(\\d+)-(\\d+)/, '$2/$1')").Should().Be("01/2024");
    [TestMethod] public void DollarDollar() => Run("'a'.replace(/a/, '$$')").Should().Be("$");

    // $<name> is literal when the regex has no named captures (the fast-path case).
    // The "<" must not be dropped — "$<x>" stays "$<x>", not "$x>".
    [TestMethod] public void DollarAngleLiteralNoNamedCaptures() => Run("'abc'.replace(/b/, '$<x>')").Should().Be("a$<x>c");
    [TestMethod] public void DollarAngleGlobalLiteral() => Run("'abab'.replace(/a/g, '$<n>')").Should().Be("$<n>b$<n>b");
    [TestMethod] public void DollarAngleUnterminated() => Run("'abc'.replace(/b/, 'x$<')").Should().Be("ax$<c");

    // Functional replacement (fast path, no named captures).
    [TestMethod] public void Functional() => Run("'abc'.replace(/b/, function(m){ return m.toUpperCase(); })").Should().Be("aBc");
    [TestMethod] public void FunctionalGlobalCaptures() => Run("'a1b2'.replace(/([a-z])(\\d)/g, function(m,p1,p2){ return p2+p1; })").Should().Be("1a2b");
}
