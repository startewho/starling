using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

[TestClass]
public class GlobalsTests
{
    [TestMethod]
    public void Global_parseInt_handles_radix_and_prefix_edges()
    {
        Eval("parseInt('0x10');").AsNumber.Should().Be(16);
        Eval("parseInt('  -42abc', 10);").AsNumber.Should().Be(-42);
        Eval("parseInt('+11', 2);").AsNumber.Should().Be(3);
        Eval("parseInt('08');").AsNumber.Should().Be(8);
        Eval("parseInt('ff', 16);").AsNumber.Should().Be(255);
        double.IsNaN(Eval("parseInt('2', 2);").AsNumber).Should().BeTrue();
    }

    [TestMethod]
    public void Global_parseFloat_scans_decimal_prefix()
    {
        Eval("parseFloat('  .5px');").AsNumber.Should().Be(0.5);
        Eval("parseFloat('-1.25e2!');").AsNumber.Should().Be(-125);
        Eval("parseFloat('+Infinity and beyond');").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("parseFloat('1.e2');").AsNumber.Should().Be(100);
        double.IsNaN(Eval("parseFloat('nope');").AsNumber).Should().BeTrue();
    }

    [TestMethod]
    public void Global_isNaN_and_isFinite_coerce_arguments()
    {
        Eval("isNaN(NaN);").AsBool.Should().BeTrue();
        Eval("isNaN('NaN');").AsBool.Should().BeTrue();
        Eval("isNaN('42');").AsBool.Should().BeFalse();
        Eval("isFinite('42');").AsBool.Should().BeTrue();
        Eval("isFinite(Infinity);").AsBool.Should().BeFalse();
        Eval("isFinite('nope');").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Global_escape_matches_legacy_annex_b_encoding()
    {
        Eval("escape('');").AsString.Should().Be("");
        Eval("escape('\\u0100\\u0101\\u0102');").AsString.Should().Be("%u0100%u0101%u0102");
        Eval("escape('\\uD834\\uDF06');").AsString.Should().Be("%uD834%uDF06");
        Eval("escape('\\x00\\x01\\x02\\x03');").AsString.Should().Be("%00%01%02%03");
        Eval("escape(',');").AsString.Should().Be("%2C");
        Eval("escape(':;<=>?');").AsString.Should().Be("%3A%3B%3C%3D%3E%3F");
        Eval("escape('`');").AsString.Should().Be("%60");
        Eval("escape('ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789@*_+-./');").AsString
            .Should().Be("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789@*_+-./");
    }

    [TestMethod]
    public void Global_unescape_matches_legacy_annex_b_decoding()
    {
        Eval("unescape('');").AsString.Should().Be("");
        Eval("unescape('%40');").AsString.Should().Be("@");
        Eval("unescape('%40_');").AsString.Should().Be("@_");
        Eval("unescape('%40%40');").AsString.Should().Be("@@");
        Eval("unescape('%u0040');").AsString.Should().Be("@");
        Eval("unescape('%u0040%u0040');").AsString.Should().Be("@@");
        Eval("unescape('%U0000');").AsString.Should().Be("%U0000");
        Eval("unescape('%u00');").AsString.Should().Be("%u00");
        Eval("unescape('%0G0');").AsString.Should().Be("%0G0");
        Eval("unescape('%');").AsString.Should().Be("%");
        Eval("unescape('%0');").AsString.Should().Be("%0");
        Eval("unescape('%2A');").AsString.Should().Be("*");
        Eval("unescape('%uFFFF');").AsString.Should().Be("\uffff");
    }

    [TestMethod]
    public void EncodeURI_preserves_reserved_but_component_encodes_them()
    {
        Eval("encodeURI('https://x.test/a b/c?x=1&y=2#h');").AsString.Should().Be("https://x.test/a%20b/c?x=1&y=2#h");
        Eval("encodeURIComponent('a b/c?');").AsString.Should().Be("a%20b%2Fc%3F");
        Eval("encodeURIComponent('é✓');").AsString.Should().Be("%C3%A9%E2%9C%93");
        Eval("encodeURI(';,/?:@&=+$#');").AsString.Should().Be(";,/?:@&=+$#");
    }

    [TestMethod]
    public void DecodeURI_preserves_reserved_escapes_but_component_decodes_all()
    {
        Eval("decodeURIComponent('a%20b%2Fc%3F');").AsString.Should().Be("a b/c?");
        Eval("decodeURIComponent('%C3%A9%E2%9C%93');").AsString.Should().Be("é✓");
        Eval("decodeURI('https://x/a%20b?x=1%262');").AsString.Should().Be("https://x/a b?x=1%262");
        Eval("decodeURI('%3Fq%3D1%26x%3D2');").AsString.Should().Be("%3Fq%3D1%26x%3D2");
    }

    [TestMethod]
    public void DecodeURI_rejects_malformed_percent_sequences()
    {
        Action lonePercent = () => Eval("decodeURIComponent('%');");
        Action truncatedUtf8 = () => Eval("decodeURIComponent('%E0%A4%A');");
        Action overlongUtf8 = () => Eval("decodeURIComponent('%C0%AF');");
        lonePercent.Should().Throw<JsThrow>();
        truncatedUtf8.Should().Throw<JsThrow>();
        overlongUtf8.Should().Throw<JsThrow>();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
