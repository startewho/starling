using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Spec.Tests.CssWritingModes4;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-writing-modes-4/">CSS Writing Modes Level 4</see>.
/// Covers the <c>writing-mode</c>, <c>direction</c>, <c>text-orientation</c>, and
/// <c>unicode-bidi</c> properties: their accepted keyword values, initial values,
/// and inheritance behaviour. The logical-property axis mapping (margin-inline-start
/// etc.) is covered separately under CssLogical and is not duplicated here.
/// </summary>
[TestClass]
[Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/")]
public sealed class WritingModeTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ParseSingle(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ---- writing-mode (§3.1) -------------------------------------------------

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#block-flow", section: "3.1")]
    [SpecFact]
    public void Writing_mode_horizontal_tb_parses()
        => ParseSingle("writing-mode: horizontal-tb;", PropertyId.WritingMode)
            .Should().Be(new CssKeyword("horizontal-tb"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#block-flow", section: "3.1")]
    [SpecFact]
    public void Writing_mode_vertical_rl_parses()
        => ParseSingle("writing-mode: vertical-rl;", PropertyId.WritingMode)
            .Should().Be(new CssKeyword("vertical-rl"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#block-flow", section: "3.1")]
    [SpecFact]
    public void Writing_mode_vertical_lr_parses()
        => ParseSingle("writing-mode: vertical-lr;", PropertyId.WritingMode)
            .Should().Be(new CssKeyword("vertical-lr"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#block-flow", section: "3.1")]
    [SpecFact]
    public void Writing_mode_sideways_rl_parses()
        => ParseSingle("writing-mode: sideways-rl;", PropertyId.WritingMode)
            .Should().Be(new CssKeyword("sideways-rl"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#block-flow", section: "3.1")]
    [SpecFact]
    public void Writing_mode_sideways_lr_parses()
        => ParseSingle("writing-mode: sideways-lr;", PropertyId.WritingMode)
            .Should().Be(new CssKeyword("sideways-lr"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#block-flow", section: "3.1")]
    [SpecFact]
    public void Writing_mode_initial_is_horizontal_tb()
        => PropertyRegistry.InitialValue(PropertyId.WritingMode)
            .Should().Be(new CssKeyword("horizontal-tb"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#block-flow", section: "3.1")]
    [SpecFact]
    public void Writing_mode_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.WritingMode).Should().BeTrue();

    // ---- direction (§6.2) ----------------------------------------------------

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#direction", section: "6.2")]
    [SpecFact]
    public void Direction_ltr_parses()
        => ParseSingle("direction: ltr;", PropertyId.Direction)
            .Should().Be(new CssKeyword("ltr"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#direction", section: "6.2")]
    [SpecFact]
    public void Direction_rtl_parses()
        => ParseSingle("direction: rtl;", PropertyId.Direction)
            .Should().Be(new CssKeyword("rtl"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#direction", section: "6.2")]
    [SpecFact]
    public void Direction_initial_is_ltr()
        => PropertyRegistry.InitialValue(PropertyId.Direction)
            .Should().Be(new CssKeyword("ltr"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#direction", section: "6.2")]
    [SpecFact]
    public void Direction_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.Direction).Should().BeTrue();

    // ---- text-orientation (§5.1) ---------------------------------------------

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#text-orientation", section: "5.1")]
    [SpecFact]
    public void Text_orientation_mixed_parses()
        => ParseSingle("text-orientation: mixed;", PropertyId.TextOrientation)
            .Should().Be(new CssKeyword("mixed"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#text-orientation", section: "5.1")]
    [SpecFact]
    public void Text_orientation_upright_parses()
        => ParseSingle("text-orientation: upright;", PropertyId.TextOrientation)
            .Should().Be(new CssKeyword("upright"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#text-orientation", section: "5.1")]
    [SpecFact]
    public void Text_orientation_sideways_parses()
        => ParseSingle("text-orientation: sideways;", PropertyId.TextOrientation)
            .Should().Be(new CssKeyword("sideways"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#text-orientation", section: "5.1")]
    [SpecFact]
    public void Text_orientation_initial_is_mixed()
        => PropertyRegistry.InitialValue(PropertyId.TextOrientation)
            .Should().Be(new CssKeyword("mixed"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#text-orientation", section: "5.1")]
    [SpecFact]
    public void Text_orientation_is_inherited()
        => PropertyRegistry.Inherits(PropertyId.TextOrientation).Should().BeTrue();

    // ---- unicode-bidi (§6.3) -------------------------------------------------

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_normal_parses()
        => ParseSingle("unicode-bidi: normal;", PropertyId.UnicodeBidi)
            .Should().Be(new CssKeyword("normal"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_embed_parses()
        => ParseSingle("unicode-bidi: embed;", PropertyId.UnicodeBidi)
            .Should().Be(new CssKeyword("embed"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_isolate_parses()
        => ParseSingle("unicode-bidi: isolate;", PropertyId.UnicodeBidi)
            .Should().Be(new CssKeyword("isolate"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_bidi_override_parses()
        => ParseSingle("unicode-bidi: bidi-override;", PropertyId.UnicodeBidi)
            .Should().Be(new CssKeyword("bidi-override"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_isolate_override_parses()
        => ParseSingle("unicode-bidi: isolate-override;", PropertyId.UnicodeBidi)
            .Should().Be(new CssKeyword("isolate-override"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_plaintext_parses()
        => ParseSingle("unicode-bidi: plaintext;", PropertyId.UnicodeBidi)
            .Should().Be(new CssKeyword("plaintext"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_initial_is_normal()
        => PropertyRegistry.InitialValue(PropertyId.UnicodeBidi)
            .Should().Be(new CssKeyword("normal"));

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Unicode_bidi_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.UnicodeBidi).Should().BeFalse();

    // ---- cascade / inheritance (§3, §6) --------------------------------------

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#text-flow", section: "3")]
    [SpecFact]
    public void Child_inherits_parent_writing_mode_and_direction()
    {
        // §3 defines the block flow direction and §6.2 the inline base direction.
        // Both writing-mode and direction are inherited, so a child with no value
        // of its own picks up the parent's vertical-rl / rtl axes.
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "div { writing-mode: vertical-rl; direction: rtl; }"));

        var childStyle = engine.Compute(child);
        childStyle.Get(PropertyId.WritingMode).Should().Be(new CssKeyword("vertical-rl"));
        childStyle.Get(PropertyId.Direction).Should().Be(new CssKeyword("rtl"));
    }

    [Spec("css-writing-modes-4", "https://www.w3.org/TR/css-writing-modes-4/#unicode-bidi", section: "6.3")]
    [SpecFact]
    public void Child_does_not_inherit_parent_unicode_bidi()
    {
        // unicode-bidi is not inherited: the child falls back to the initial value.
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("span");
        doc.AppendChild(parent);
        parent.AppendChild(child);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(
            "div { unicode-bidi: bidi-override; }"));

        engine.Compute(child).Get(PropertyId.UnicodeBidi).Should().Be(new CssKeyword("normal"));
    }
}
