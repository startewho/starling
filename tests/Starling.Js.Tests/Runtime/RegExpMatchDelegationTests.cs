using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Js.Tests.Runtime;

/// <summary>
/// Regression coverage for the RegExp/String <c>match</c> spec gap that made
/// core-js feature-detect Starling's native <c>RegExp.prototype[@@match]</c>
/// as "broken" and install its own recursing polyfill. mcmaster.com's second
/// bundle (jQuery + Backbone + YUI + Handlebars, alongside its core-js shim)
/// then recursed without terminating, surfacing as
/// <c>RangeError: Maximum call stack size exceeded</c>.
///
/// Mirrors core-js's DELEGATES_TO_EXEC feature-detect in
/// internals/fix-regexp-well-known-symbol-logic.js: §22.2.6.8
/// RegExp.prototype[@@match] must obtain every match through
/// RegExpExec(R, S) (§22.2.7.1), i.e. through the receiver's (possibly
/// overridden) exec method — not an internal re-match.
/// </summary>
[TestClass]
public class RegExpMatchDelegationTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    // --- DELEGATES_TO_EXEC: @@match drives RegExpExec -> this exec method ------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexpexec", "22.2.7.1 RegExpExec")]
    [SpecFact]
    public void Symbol_match_calls_the_regexps_own_exec_when_not_global()
    {
        // Non-global @@match must call the (possibly overridden) exec exactly once.
        var src = "var calls = 0;\n"
                + "var re = /a/;\n"
                + "re['exec'] = function () { calls++; return null; };\n"
                + "re[Symbol.match]('aaa');\n"
                + "calls;\n";
        Eval(src).AsNumber.Should().Be(1);
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Symbol_match_uses_overridden_exec_result_when_not_global()
    {
        // The returned array comes from what the exec method returns.
        var src = "var re = /zzz/;\n"
                + "re['exec'] = function (s) { var r = ['hello']; r.index = 0; r.input = s; return r; };\n"
                + "re[Symbol.match]('abc')[0];\n";
        Eval(src).AsString.Should().Be("hello");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Global_symbol_match_collects_via_overridden_exec()
    {
        // Global @@match loops on RegExpExec; each [0] comes from the exec result.
        var src = "var n = 0;\n"
                + "var re = /x/g;\n"
                + "re['exec'] = function (s) {\n"
                + "  if (n >= 2) return null;\n"
                + "  n++;\n"
                + "  var r = ['m' + n]; r.index = n - 1; r.input = s; return r;\n"
                + "};\n"
                + "var out = re[Symbol.match]('xxxx');\n"
                + "out.length + ':' + out[0] + ',' + out[1];\n";
        Eval(src).AsString.Should().Be("2:m1,m2");
    }

    // --- DELEGATES_TO_SYMBOL: String#match delegates to any @@match ------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-string.prototype.match", "22.1.3.13 String.prototype.match")]
    [SpecFact]
    public void String_match_delegates_to_argument_Symbol_match()
    {
        // A plain object exposing [Symbol.match] must be honored by String#match.
        var src = "var O = {};\n"
                + "O[Symbol.match] = function () { return 7; };\n"
                + "'whatever'.match(O);\n";
        Eval(src).AsNumber.Should().Be(7);
    }

    // --- end-to-end: native @@match semantics unchanged -----------------------

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Native_nonglobal_match_returns_capture_array()
    {
        Eval(@"var r = 'a1b2'.match(/([a-z])(\d)/); r[0] + ',' + r[1] + ',' + r[2] + ',' + r.index")
            .AsString.Should().Be("a1,a,1,0");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Native_global_match_returns_all_matches()
    {
        Eval(@"'a1b2c3'.match(/[a-z]\d/g).join('|')").AsString.Should().Be("a1|b2|c3");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Native_global_match_no_hit_is_null()
    {
        Eval(@"'abc'.match(/\d/g) === null").AsBool.Should().BeTrue();
    }

    // --- @@match global fast-path guard ---------------------------------------
    // The fast path may only fire when exec AND the global/unicode getters are
    // still the realm built-ins. Overriding any one of them must fall back to
    // the generic RegExpExec loop so the override is observed.

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Global_symbol_match_honors_overridden_global_getter()
    {
        // An own `global` getter that flips the regex to non-global must steer
        // @@match into the non-global branch (single RegExpExec, returns the
        // full match array — not the global string list).
        var src = "var re = /a/g;\n"
                + "Object.defineProperty(re, 'global', { get: function () { return false; } });\n"
                + "var r = re[Symbol.match]('aaa');\n"
                + "Array.isArray(r) + ':' + r.length + ':' + r[0] + ':' + r.index;\n";
        Eval(src).AsString.Should().Be("true:1:a:0");
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Global_symbol_match_reads_overridden_unicode_getter()
    {
        // A spy `unicode` getter on a genuine RegExp must still be consulted by
        // @@match (the fast path is disqualified the moment unicode is shadowed).
        var src = "var seen = false;\n"
                + "var re = /a/g;\n"
                + "Object.defineProperty(re, 'unicode', { get: function () { seen = true; return false; } });\n"
                + "re[Symbol.match]('aaa');\n"
                + "seen;\n";
        Eval(src).AsBool.Should().BeTrue();
    }

    [Spec("ecma262", "https://tc39.es/ecma262/#sec-regexp.prototype-@@match", "22.2.6.8 RegExp.prototype [ @@match ]")]
    [SpecFact]
    public void Native_global_match_empty_pattern_advances_per_code_unit()
    {
        // The native fast path must advance over an empty match the same way the
        // generic loop does, yielding one "" per index plus the final position.
        Eval(@"'ab'.match(/(?:)/g).join('|')").AsString.Should().Be("||");
    }
}
