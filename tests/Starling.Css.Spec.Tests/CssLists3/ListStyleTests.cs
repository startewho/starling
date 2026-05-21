using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.UserAgent;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssLists3;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-lists-3/">CSS Lists 3</see>.
/// </summary>
[TestClass]
[Spec("css-lists-3", "https://www.w3.org/TR/css-lists-3/")]
public sealed class ListStyleTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    [Spec("css-lists-3", "https://www.w3.org/TR/css-lists-3/#list-style-property", section: "2.5")]
    [SpecFact]
    public void List_style_shorthand_sets_three_longhands()
    {
        var decls = Expand("list-style: lower-roman inside;");
        decls.Single(d => d.Id == PropertyId.ListStyleType).Value.Should().Be(new CssKeyword("lower-roman"));
        decls.Single(d => d.Id == PropertyId.ListStylePosition).Value.Should().Be(new CssKeyword("inside"));
        decls.Single(d => d.Id == PropertyId.ListStyleImage).Value.Should().Be(new CssKeyword("none"));
    }

    [Spec("css-lists-3", "https://www.w3.org/TR/css-lists-3/#list-style-type-property", section: "3.1")]
    [SpecFact]
    public void List_style_type_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.ListStyleType).Should().BeTrue();

    [Spec("css-lists-3", "https://www.w3.org/TR/css-lists-3/#ua-stylesheet", section: "A")]
    [SpecFact]
    public void Ua_makes_ol_li_decimal_and_list_item()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var ol = doc.CreateElement("ol");
        var li = doc.CreateElement("li");
        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(ol);
        ol.AppendChild(li);

        var engine = new StyleEngine(); // UA sheet included by default
        var liStyle = engine.Compute(li);
        liStyle.Get(PropertyId.Display).Should().Be(new CssKeyword("list-item"));
        liStyle.Get(PropertyId.ListStyleType).Should().Be(new CssKeyword("decimal"));
    }

    [Spec("css-lists-3", "https://www.w3.org/TR/css-lists-3/#ua-stylesheet", section: "A")]
    [SpecFact]
    public void Ua_makes_ul_li_disc()
    {
        var doc = new Document();
        var ul = doc.CreateElement("ul");
        var li = doc.CreateElement("li");
        doc.AppendChild(ul);
        ul.AppendChild(li);

        var engine = new StyleEngine();
        engine.Compute(li).Get(PropertyId.ListStyleType).Should().Be(new CssKeyword("disc"));
    }

    [Spec("css-lists-3", "https://www.w3.org/TR/css-lists-3/#ua-stylesheet", section: "A")]
    [SpecFact]
    public void Ua_sheet_parses()
        => UaStyleSheet.Parse().Rules.Should().NotBeEmpty();
}
