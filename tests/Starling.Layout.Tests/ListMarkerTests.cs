using AwesomeAssertions;
using Starling.Layout.Tree;
using Starling.Spec;

namespace Starling.Layout.Tests;

[TestClass]
[Spec("css-counter-styles-3", "https://www.w3.org/TR/css-counter-styles-3/")]
public sealed class ListMarkerTests
{
    [TestMethod]
    [DataRow("disc", 1, "•")]
    [DataRow("circle", 1, "◦")]
    [DataRow("square", 1, "▪")]
    public void Glyph_markers_render_their_bullet(string type, int ordinal, string expected)
        => ListMarker.Render(type, ordinal).Should().Be(expected);

    [TestMethod]
    public void None_renders_nothing()
        => ListMarker.Render("none", 3).Should().BeNull();

    [TestMethod]
    [DataRow(1, "1.")]
    [DataRow(42, "42.")]
    public void Decimal_renders_number_with_dot(int ordinal, string expected)
        => ListMarker.Render("decimal", ordinal).Should().Be(expected);

    [TestMethod]
    [DataRow(1, "01.")]
    [DataRow(9, "09.")]
    [DataRow(10, "10.")]
    public void Decimal_leading_zero_pads_to_two_digits(int ordinal, string expected)
        => ListMarker.Render("decimal-leading-zero", ordinal).Should().Be(expected);

    [TestMethod]
    [DataRow(1, "a.")]
    [DataRow(26, "z.")]
    [DataRow(27, "aa.")]
    [DataRow(28, "ab.")]
    public void Lower_alpha_is_bijective_base26(int ordinal, string expected)
        => ListMarker.Render("lower-alpha", ordinal).Should().Be(expected);

    [TestMethod]
    [DataRow(1, "A.")]
    [DataRow(27, "AA.")]
    public void Upper_alpha_is_bijective_base26(int ordinal, string expected)
        => ListMarker.Render("upper-alpha", ordinal).Should().Be(expected);

    [TestMethod]
    [DataRow(1, "i.")]
    [DataRow(4, "iv.")]
    [DataRow(9, "ix.")]
    [DataRow(40, "xl.")]
    [DataRow(1994, "mcmxciv.")]
    public void Lower_roman_uses_subtractive_notation(int ordinal, string expected)
        => ListMarker.Render("lower-roman", ordinal).Should().Be(expected);

    [TestMethod]
    [DataRow(4, "IV.")]
    [DataRow(2024, "MMXXIV.")]
    public void Upper_roman_uses_subtractive_notation(int ordinal, string expected)
        => ListMarker.Render("upper-roman", ordinal).Should().Be(expected);

    [TestMethod]
    public void Roman_out_of_range_falls_back_to_decimal()
        => ListMarker.Render("upper-roman", 4000).Should().Be("4000.");

    [TestMethod]
    public void Lower_alpha_zero_falls_back_to_decimal()
        => ListMarker.Render("lower-alpha", 0).Should().Be("0.");

    [TestMethod]
    public void Unknown_type_renders_nothing()
        => ListMarker.Render("hebrew", 1).Should().BeNull();
}
