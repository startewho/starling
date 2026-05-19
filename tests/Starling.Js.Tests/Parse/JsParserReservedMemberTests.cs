using FluentAssertions;
using Tessera.Js.Ast;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Parse;

/// <summary>
/// ES §13.3.2: <c>MemberExpression . IdentifierName</c> — every reserved
/// word and contextual keyword is a valid property-name token after <c>.</c>
/// (and <c>?.</c>). These tests pin the parser fix that lets
/// <c>Promise.resolve(1).catch(e => 2)</c>, <c>obj.finally</c>,
/// <c>arr.with(0, 'x')</c>, etc. parse without the bracket-form workaround.
/// </summary>
public class JsParserReservedMemberTests
{
    // -------- parse-only smoke tests -------------------------------------

    [Fact]
    public void Dot_catch_parses_as_member_expression()
    {
        var expr = ParseExpr("p.catch");
        var me = expr.Should().BeOfType<MemberExpression>().Subject;
        me.Computed.Should().BeFalse();
        me.Property.Should().BeOfType<Identifier>().Which.Name.Should().Be("catch");
    }

    [Fact]
    public void Dot_finally_parses_as_member_expression()
    {
        var me = ParseExpr("obj.finally").Should().BeOfType<MemberExpression>().Subject;
        me.Property.Should().BeOfType<Identifier>().Which.Name.Should().Be("finally");
    }

    [Fact]
    public void Dot_with_parses_as_member_expression()
    {
        var me = ParseExpr("arr.with").Should().BeOfType<MemberExpression>().Subject;
        me.Property.Should().BeOfType<Identifier>().Which.Name.Should().Be("with");
    }

    [Fact]
    public void Optional_chain_dot_catch_parses()
    {
        var me = ParseExpr("p?.catch").Should().BeOfType<MemberExpression>().Subject;
        me.Optional.Should().BeTrue();
        me.Property.Should().BeOfType<Identifier>().Which.Name.Should().Be("catch");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("class")]
    [InlineData("if")]
    [InlineData("else")]
    [InlineData("return")]
    [InlineData("for")]
    [InlineData("while")]
    [InlineData("delete")]
    [InlineData("in")]
    [InlineData("instanceof")]
    [InlineData("typeof")]
    [InlineData("void")]
    [InlineData("new")]
    [InlineData("this")]
    [InlineData("try")]
    [InlineData("throw")]
    [InlineData("switch")]
    [InlineData("case")]
    [InlineData("break")]
    [InlineData("continue")]
    [InlineData("function")]
    [InlineData("var")]
    [InlineData("export")]
    [InlineData("import")]
    [InlineData("yield")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public void Reserved_word_as_dot_property_parses(string name)
    {
        var me = ParseExpr($"o.{name}").Should().BeOfType<MemberExpression>().Subject;
        me.Computed.Should().BeFalse();
        me.Property.Should().BeOfType<Identifier>().Which.Name.Should().Be(name);
    }

    [Theory]
    // contextual keywords are emitted as plain Identifier tokens already,
    // but cover them so future lexer changes don't regress.
    [InlineData("let")]
    [InlineData("const")]
    [InlineData("async")]
    [InlineData("await")]
    [InlineData("static")]
    [InlineData("undefined")]
    public void Contextual_keyword_as_dot_property_parses(string name)
    {
        var me = ParseExpr($"o.{name}").Should().BeOfType<MemberExpression>().Subject;
        me.Property.Should().BeOfType<Identifier>().Which.Name.Should().Be(name);
    }

    // -------- end-to-end execution tests ---------------------------------

    [Fact]
    public void Promise_catch_dot_form_runs_end_to_end()
    {
        var rt = Run(@"
            globalThis.r = 0;
            Promise.resolve(1).catch(function(e) { return 2; }).then(function(v) { globalThis.r = v; });
        ");
        // No rejection — catch is bypassed; resolves with 1.
        rt.GetGlobal("r").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Reading_default_property_via_dot_round_trips()
    {
        var rt = Run(@"
            var o = {};
            o.default = 42;
            globalThis.r = o.default;
        ");
        rt.GetGlobal("r").AsNumber.Should().Be(42);
    }

    [Fact]
    public void Reading_class_property_via_dot_round_trips()
    {
        var rt = Run(@"
            var o = {};
            o.class = 'foo';
            globalThis.r = o.class;
        ");
        rt.GetGlobal("r").AsString.Should().Be("foo");
    }

    [Fact]
    public void Many_reserved_words_round_trip_through_dot_property_assignment()
    {
        // Assign each reserved-word property via bracket form (so this test
        // only exercises the dot-form *read* path under test) then read each
        // back via dot form.
        var rt = Run(@"
            var o = {};
            o['if'] = 0; o['else'] = 1; o['return'] = 2; o['for'] = 3;
            o['while'] = 4; o['delete'] = 5; o['in'] = 6; o['instanceof'] = 7;
            o['typeof'] = 8; o['void'] = 9; o['new'] = 10; o['this'] = 11;
            o['try'] = 12; o['throw'] = 13; o['switch'] = 14; o['case'] = 15;
            o['break'] = 16; o['continue'] = 17; o['function'] = 18; o['var'] = 19;
            o['export'] = 20; o['import'] = 21; o['yield'] = 22; o['true'] = 23;
            o['false'] = 24; o['null'] = 25;
            globalThis.r =
                o.if + ',' + o.else + ',' + o.return + ',' + o.for + ',' +
                o.while + ',' + o.delete + ',' + o.in + ',' + o.instanceof + ',' +
                o.typeof + ',' + o.void + ',' + o.new + ',' + o.this + ',' +
                o.try + ',' + o.throw + ',' + o.switch + ',' + o.case + ',' +
                o.break + ',' + o.continue + ',' + o.function + ',' + o.var + ',' +
                o.export + ',' + o.import + ',' + o.yield + ',' + o.true + ',' +
                o.false + ',' + o.null;
        ");
        rt.GetGlobal("r").AsString
            .Should().Be("0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25");
    }

    // -------------------------------------------------------------- Helpers

    private static Expression ParseExpr(string src) => new JsParser(src).ParseExpression();

    private static JsRuntime Run(string source)
    {
        var rt = new JsRuntime();
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        new JsVm(rt).Run(chunk);
        return rt;
    }
}
