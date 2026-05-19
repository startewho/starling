using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

/// <summary>JS-surface tests for the RegExp intrinsic + String.prototype regex paths.</summary>
public class RegExpTests
{
    [Fact]
    public void Construct_with_pattern_and_flags()
    {
        Run("var r = new RegExp('abc', 'gi'); r.source;").AsString.Should().Be("abc");
        Run("var r = new RegExp('abc', 'gi'); r.flags;").AsString.Should().Be("gi");
        Run("var r = new RegExp('abc', 'gi'); r.global;").AsBool.Should().BeTrue();
        Run("var r = new RegExp('abc', 'gi'); r.ignoreCase;").AsBool.Should().BeTrue();
        Run("var r = new RegExp('abc', 'gi'); r.multiline;").AsBool.Should().BeFalse();
    }

    [Fact]
    public void Duplicate_flags_throw_syntax_error()
    {
        var act = () => Run("new RegExp('a', 'gg');");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Invalid_pattern_throws_syntax_error()
    {
        var act = () => Run("new RegExp('[a-');");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Test_returns_boolean()
    {
        Run("new RegExp('foo').test('foobar');").AsBool.Should().BeTrue();
        Run("new RegExp('foo').test('barbaz');").AsBool.Should().BeFalse();
    }

    [Fact]
    public void Run_returns_array_with_index_and_input()
    {
        Run("var r = new RegExp('(\\\\d+)-(\\\\d+)'); var m = r.exec('abc 12-34 xyz'); m[0];").AsString.Should().Be("12-34");
        Run("var r = new RegExp('(\\\\d+)-(\\\\d+)'); var m = r.exec('abc 12-34 xyz'); m[1];").AsString.Should().Be("12");
        Run("var r = new RegExp('(\\\\d+)-(\\\\d+)'); var m = r.exec('abc 12-34 xyz'); m[2];").AsString.Should().Be("34");
        Run("var r = new RegExp('(\\\\d+)-(\\\\d+)'); var m = r.exec('abc 12-34 xyz'); m.index;").AsNumber.Should().Be(4);
        Run("var r = new RegExp('(\\\\d+)-(\\\\d+)'); var m = r.exec('abc 12-34 xyz'); m.input;").AsString.Should().Be("abc 12-34 xyz");
    }

    [Fact]
    public void Run_no_match_returns_null()
    {
        Run("new RegExp('foo').exec('bar');").IsNull.Should().BeTrue();
    }

    [Fact]
    public void Named_groups_appear_on_groups_object()
    {
        Run("var r = new RegExp('(?<year>\\\\d{4})-(?<month>\\\\d{2})'); r.exec('2024-01').groups.year;").AsString.Should().Be("2024");
        Run("var r = new RegExp('(?<year>\\\\d{4})-(?<month>\\\\d{2})'); r.exec('2024-01').groups.month;").AsString.Should().Be("01");
    }

    [Fact]
    public void Backreference_matches_repeated_chars()
    {
        Run("new RegExp('(.)\\\\1').test('aa');").AsBool.Should().BeTrue();
        Run("new RegExp('(.)\\\\1').test('ab');").AsBool.Should().BeFalse();
    }

    [Fact]
    public void Lookaheads_constrain_matches()
    {
        Run("new RegExp('foo(?=bar)').test('foobar');").AsBool.Should().BeTrue();
        Run("new RegExp('foo(?=bar)').test('foobaz');").AsBool.Should().BeFalse();
        Run("new RegExp('foo(?!bar)').test('foobaz');").AsBool.Should().BeTrue();
        Run("new RegExp('foo(?!bar)').test('foobar');").AsBool.Should().BeFalse();
    }

    [Fact]
    public void IgnoreCase_flag_folds_letters()
    {
        Run("new RegExp('abc', 'i').test('ABC');").AsBool.Should().BeTrue();
    }

    [Fact]
    public void Multiline_anchors_match_per_line()
    {
        Run("new RegExp('^foo$', 'm').test('hello\\nfoo');").AsBool.Should().BeTrue();
    }

    [Fact]
    public void DotAll_flag_makes_dot_match_newline()
    {
        Run("new RegExp('a.b', 's').test('a\\nb');").AsBool.Should().BeTrue();
        Run("new RegExp('a.b').test('a\\nb');").AsBool.Should().BeFalse();
    }

    [Fact]
    public void Global_flag_advances_lastIndex()
    {
        Run("var r = new RegExp('a', 'g'); r.exec('aaa'); r.lastIndex;").AsNumber.Should().Be(1);
        Run("var r = new RegExp('a', 'g'); r.exec('aaa'); r.exec('aaa'); r.lastIndex;").AsNumber.Should().Be(2);
    }

    [Fact]
    public void Sticky_flag_only_matches_at_lastIndex()
    {
        Run("var r = new RegExp('foo', 'y'); r.lastIndex = 3; r.test('barfoo');").AsBool.Should().BeTrue();
        Run("var r = new RegExp('foo', 'y'); r.lastIndex = 0; r.test('barfoo');").AsBool.Should().BeFalse();
    }

    // ---------------- String back-fills ----------------

    [Fact]
    public void StringMatch_with_global_regex_returns_all_matches()
    {
        var arr = Run("var a = 'hello'.match(new RegExp('l', 'g')); a[0] + a[1] + a.length;");
        arr.AsString.Should().Be("ll2");
    }

    [Fact]
    public void StringMatch_with_non_global_regex_returns_single_match_array()
    {
        Run("'hello'.match(new RegExp('l'))[0];").AsString.Should().Be("l");
    }

    [Fact]
    public void StringReplace_uses_regex_capture_in_substitution()
    {
        Run("'abc123'.replace(new RegExp('(\\\\d+)'), '[$1]');").AsString.Should().Be("abc[123]");
    }

    [Fact]
    public void StringReplace_with_global_regex_replaces_all()
    {
        Run("'a-b-c'.replace(new RegExp('-', 'g'), '_');").AsString.Should().Be("a_b_c");
    }

    [Fact]
    public void StringReplaceAll_with_global_regex_replaces_all()
    {
        Run("'a-b-c'.replaceAll(new RegExp('-', 'g'), '_');").AsString.Should().Be("a_b_c");
    }

    [Fact]
    public void StringReplaceAll_with_non_global_regex_throws()
    {
        var act = () => Run("'a-b-c'.replaceAll(new RegExp('-'), '_');");
        act.Should().Throw<JsThrow>();
    }

    [Fact]
    public void StringSearch_returns_first_match_index()
    {
        Run("'foo'.search(new RegExp('o'));").AsNumber.Should().Be(1);
        Run("'foo'.search(new RegExp('x'));").AsNumber.Should().Be(-1);
    }

    [Fact]
    public void StringReplace_with_function_replacer()
    {
        Run("'foo bar baz'.replace(new RegExp('\\\\b(\\\\w)', 'g'), function(c) { return c.toUpperCase(); });")
            .AsString.Should().Be("Foo Bar Baz");
    }

    [Fact]
    public void StringSplit_with_regex_preserves_captures()
    {
        Run("var a = 'a1b2c'.split(new RegExp('(\\\\d)')); a.length;").AsNumber.Should().Be(5);
        Run("var a = 'a1b2c'.split(new RegExp('(\\\\d)')); a[0] + a[1] + a[2] + a[3] + a[4];").AsString.Should().Be("a1b2c");
    }

    [Fact]
    public void Match_with_string_pattern_wraps_in_regex()
    {
        Run("'hello'.match('l')[0];").AsString.Should().Be("l");
    }

    private static JsValue Run(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
