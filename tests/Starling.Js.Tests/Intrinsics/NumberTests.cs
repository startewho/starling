using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

[TestClass]
public class NumberTests
{
    [TestMethod]
    public void Number_constructor_coerces_and_boxes()
    {
        Eval("Number('');").AsNumber.Should().Be(0);
        Eval("Number('0x10');").AsNumber.Should().Be(16);
        Eval("Number(true);").AsNumber.Should().Be(1);
        Eval("Number(null);").AsNumber.Should().Be(0);
        double.IsNaN(Eval("Number(undefined);").AsNumber).Should().BeTrue();
        Eval("var n = new Number(7); n.valueOf();").AsNumber.Should().Be(7);
        Eval("var n = new Number(7); n.toString();").AsString.Should().Be("7");
        Eval("Number.prototype.valueOf.callMe;").IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Number_constants_are_registered()
    {
        Eval("Number.MAX_SAFE_INTEGER;").AsNumber.Should().Be(9007199254740991d);
        Eval("Number.MIN_SAFE_INTEGER;").AsNumber.Should().Be(-9007199254740991d);
        Eval("Number.POSITIVE_INFINITY;").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("Number.NEGATIVE_INFINITY;").AsNumber.Should().Be(double.NegativeInfinity);
        double.IsNaN(Eval("Number.NaN;").AsNumber).Should().BeTrue();
        Eval("Number.MAX_VALUE;").AsNumber.Should().Be(double.MaxValue);
        Eval("Number.MIN_VALUE;").AsNumber.Should().Be(double.Epsilon);
        Eval("Number.EPSILON;").AsNumber.Should().Be(Math.Pow(2, -52));
    }

    [TestMethod]
    public void Number_statics_classify_without_global_coercion()
    {
        Eval("Number.isFinite(3);").AsBool.Should().BeTrue();
        Eval("Number.isFinite('3');").AsBool.Should().BeFalse();
        Eval("Number.isFinite(Infinity);").AsBool.Should().BeFalse();
        Eval("Number.isNaN(NaN);").AsBool.Should().BeTrue();
        Eval("Number.isNaN('NaN');").AsBool.Should().BeFalse();
        Eval("Number.isInteger(1.0);").AsBool.Should().BeTrue();
        Eval("Number.isInteger(1.5);").AsBool.Should().BeFalse();
        Eval("Number.isSafeInteger(9007199254740991);").AsBool.Should().BeTrue();
        Eval("Number.isSafeInteger(9007199254740992);").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Number_parse_methods_match_globals()
    {
        Eval("Number.parseInt('0x10');").AsNumber.Should().Be(16);
        Eval("Number.parseInt('  -42abc', 10);").AsNumber.Should().Be(-42);
        Eval("Number.parseInt('101', 2);").AsNumber.Should().Be(5);
        double.IsNaN(Eval("Number.parseInt('zzz', 10);").AsNumber).Should().BeTrue();
        Eval("Number.parseFloat('  -3.5px');").AsNumber.Should().Be(-3.5);
        Eval("Number.parseFloat('1.25e2!');").AsNumber.Should().Be(125);
        Eval("Number.parseFloat('Infinity');").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("parseFloat('1.7976931348623157e+308');").AsNumber.Should().Be(double.MaxValue);
        double.IsNaN(Eval("Number.parseFloat('abc');").AsNumber).Should().BeTrue();
    }

    [TestMethod]
    public void Number_prototype_formats_values()
    {
        Eval("(255).toString(16);").AsString.Should().Be("ff");
        Eval("(-10).toString(2);").AsString.Should().Be("-1010");
        Eval("(0.1 + 0.2).toFixed(2);").AsString.Should().Be("0.30");
        Eval("(12.345).toFixed(1);").AsString.Should().Be("12.3");
        Eval("(1234).toExponential(2);").AsString.Should().Be("1.23e+3");
        Eval("(12.345).toPrecision(4);").AsString.Should().Be("12.35");
        Eval("(42).toLocaleString();").AsString.Should().Be("42");
        Eval("(42).valueOf();").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Number_high_precision_formatting_matches_reference_cases()
    {
        Eval("(3).toExponential(1);").AsString.Should().Be("3.0e+0");
        Eval("(3).toExponential(50);").AsString.Should().Be("3.00000000000000000000000000000000000000000000000000e+0");
        Eval("(3).toExponential(100);").AsString.Should().Be("3." + new string('0', 100) + "e+0");
        Eval("(3).toFixed(1);").AsString.Should().Be("3.0");
        Eval("(3).toFixed(50);").AsString.Should().Be("3." + new string('0', 50));
        Eval("(3).toFixed(100);").AsString.Should().Be("3." + new string('0', 100));
        Eval("(3).toPrecision(1);").AsString.Should().Be("3");
        Eval("(3).toPrecision(50);").AsString.Should().Be("3." + new string('0', 49));
        Eval("(3).toPrecision(100);").AsString.Should().Be("3." + new string('0', 99));
    }

    [TestMethod]
    public void Number_to_string_uses_ecmascript_exponent_thresholds()
    {
        Eval("String(1e55);").AsString.Should().Be("1e+55");
        Eval("String(1e-6);").AsString.Should().Be("0.000001");
        Eval("String(1e-7);").AsString.Should().Be("1e-7");
        Eval("String(1e20);").AsString.Should().Be("100000000000000000000");
        Eval("String(1e21);").AsString.Should().Be("1e+21");
    }

    [TestMethod]
    public void Computed_numeric_property_keys_use_ecmascript_number_strings()
    {
        var r = Eval(@"
            var o = {
                [1e55]: 'big',
                [0.000001]: 'small'
            };
            o['1e+55'] + ',' + o['0.000001'];");

        r.AsString.Should().Be("big,small");
    }

    [TestMethod]
    public void Number_coercion_overflow_from_numeric_and_string_literals()
    {
        Eval("Number(1e1000);").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("+1e1000;").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("(+1e1000).toString();").AsString.Should().Be("Infinity");
        Eval("Number('1e1000');").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("+'1e1000';").AsNumber.Should().Be(double.PositiveInfinity);
        Eval("(+'1e1000').toString();").AsString.Should().Be("Infinity");
        Eval("Number(-1e1000);").AsNumber.Should().Be(double.NegativeInfinity);
        Eval("-1e1000;").AsNumber.Should().Be(double.NegativeInfinity);
        Eval("(-1e1000).toString();").AsString.Should().Be("-Infinity");
        Eval("Number('-1e1000');").AsNumber.Should().Be(double.NegativeInfinity);
        Eval("-'1e1000';").AsNumber.Should().Be(double.NegativeInfinity);
        Eval("(-'1e1000').toString();").AsString.Should().Be("-Infinity");
    }

    [TestMethod]
    public void Number_methods_throw_on_invalid_receivers_and_ranges()
    {
        Action badReceiver = () => Eval("Number.prototype.valueOf();");
        Action badRadix = () => Eval("(1).toString(1);");
        Action badDigits = () => Eval("(1).toFixed(101);");
        badReceiver.Should().Throw<JsThrow>();
        badRadix.Should().Throw<JsThrow>();
        badDigits.Should().Throw<JsThrow>();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
