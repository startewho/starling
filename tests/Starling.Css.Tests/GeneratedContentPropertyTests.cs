using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-content-3", "https://www.w3.org/TR/css-content-3/")]
[Spec("css-lists-3", "https://www.w3.org/TR/css-lists-3/")]
[TestClass]
public sealed class GeneratedContentPropertyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [TestMethod]
    public void Content_string_parses_as_css_string()
    {
        var decls = Expand("content: \"hi\";");
        decls.Single(d => d.Id == PropertyId.Content).Value.Should().Be(new CssString("hi"));
    }

    [TestMethod]
    public void Content_none_parses_as_keyword()
    {
        Expand("content: none;").Single(d => d.Id == PropertyId.Content).Value
            .Should().Be(new CssKeyword("none"));
    }

    [TestMethod]
    public void Content_normal_parses_as_keyword()
    {
        Expand("content: normal;").Single(d => d.Id == PropertyId.Content).Value
            .Should().Be(new CssKeyword("normal"));
    }

    [TestMethod]
    public void Content_attr_parses_as_attr_reference()
    {
        var value = Expand("content: attr(data-x);").Single(d => d.Id == PropertyId.Content).Value;
        value.Should().BeOfType<CssAttrReference>().Which.AttrName.Should().Be("data-x");
    }

    [TestMethod]
    public void Content_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.Content).Should().BeFalse();

    [TestMethod]
    public void List_style_type_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.ListStyleType).Should().BeTrue();

    [TestMethod]
    public void List_style_type_initial_is_disc()
        => PropertyRegistry.InitialValue(PropertyId.ListStyleType).Should().Be(new CssKeyword("disc"));

    [TestMethod]
    public void List_style_shorthand_expands_type_position_image()
    {
        var decls = Expand("list-style: square inside;");
        decls.Single(d => d.Id == PropertyId.ListStyleType).Value.Should().Be(new CssKeyword("square"));
        decls.Single(d => d.Id == PropertyId.ListStylePosition).Value.Should().Be(new CssKeyword("inside"));
        decls.Single(d => d.Id == PropertyId.ListStyleImage).Value.Should().Be(new CssKeyword("none"));
    }

    [TestMethod]
    public void List_style_none_sets_type_none()
    {
        Expand("list-style: none;").Single(d => d.Id == PropertyId.ListStyleType).Value
            .Should().Be(new CssKeyword("none"));
    }

    [TestMethod]
    public void List_style_type_longhand_parses()
    {
        Expand("list-style-type: upper-roman;").Single(d => d.Id == PropertyId.ListStyleType).Value
            .Should().Be(new CssKeyword("upper-roman"));
    }

    [TestMethod]
    public void Quotes_parse_accepted()
        => Expand("quotes: \"<\" \">\";").Should().Contain(d => d.Id == PropertyId.Quotes);
}
