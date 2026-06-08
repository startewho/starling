// SPDX-License-Identifier: Apache-2.0
using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

/// <summary>
/// CSS Masking 1 / CSS Backgrounds 3 — -webkit- prefixed aliases.
/// Most real-world sites use -webkit-mask-image, -webkit-background-clip: text,
/// etc. The registry must accept these as aliases of their unprefixed equivalents.
/// </summary>
[TestClass]
[Spec("css-masking-1", "https://www.w3.org/TR/css-masking-1/")]
[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/")]
public sealed class WebkitMaskAliasTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-image
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskImage_url_parses_to_MaskImage()
    {
        var decls = Expand("-webkit-mask-image: url(mask.png);");

        var d = decls.Should().ContainSingle(x => x.Id == PropertyId.MaskImage).Subject;
        d.Value.Should().Be(new CssUrl("mask.png"));
    }

    [TestMethod]
    public void WebkitMaskImage_none_matches_unprefixed()
    {
        var webkit = Expand("-webkit-mask-image: none;");
        var plain = Expand("mask-image: none;");

        var wv = webkit.Single(d => d.Id == PropertyId.MaskImage).Value;
        var pv = plain.Single(d => d.Id == PropertyId.MaskImage).Value;
        wv.Should().Be(pv);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask shorthand
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMask_shorthand_expands_same_longhands_as_unprefixed()
    {
        var webkit = Expand("-webkit-mask: url(m.png) no-repeat center;");
        var plain = Expand("mask: url(m.png) no-repeat center;");

        var wIds = webkit.Select(d => d.Id).OrderBy(x => x).ToList();
        var pIds = plain.Select(d => d.Id).OrderBy(x => x).ToList();
        wIds.Should().Equal(pIds);

        // Both produce the same MaskImage value.
        var wImg = webkit.Single(d => d.Id == PropertyId.MaskImage).Value;
        var pImg = plain.Single(d => d.Id == PropertyId.MaskImage).Value;
        wImg.Should().Be(pImg);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-position
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskPosition_parses_to_MaskPosition()
    {
        var decls = Expand("-webkit-mask-position: 50% 50%;");

        decls.Should().ContainSingle(d => d.Id == PropertyId.MaskPosition);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-size
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskSize_parses_to_MaskSize_and_matches_unprefixed()
    {
        var webkit = Expand("-webkit-mask-size: cover;");
        var plain = Expand("mask-size: cover;");

        var wv = webkit.Single(d => d.Id == PropertyId.MaskSize).Value;
        var pv = plain.Single(d => d.Id == PropertyId.MaskSize).Value;
        wv.Should().Be(pv);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-repeat
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskRepeat_parses_to_MaskRepeat_and_matches_unprefixed()
    {
        var webkit = Expand("-webkit-mask-repeat: no-repeat;");
        var plain = Expand("mask-repeat: no-repeat;");

        var wv = webkit.Single(d => d.Id == PropertyId.MaskRepeat).Value;
        var pv = plain.Single(d => d.Id == PropertyId.MaskRepeat).Value;
        wv.Should().Be(pv);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-origin
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskOrigin_parses_to_MaskOrigin_and_matches_unprefixed()
    {
        var webkit = Expand("-webkit-mask-origin: content-box;");
        var plain = Expand("mask-origin: content-box;");

        var wv = webkit.Single(d => d.Id == PropertyId.MaskOrigin).Value;
        var pv = plain.Single(d => d.Id == PropertyId.MaskOrigin).Value;
        wv.Should().Be(pv);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-clip
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskClip_parses_to_MaskClip_and_matches_unprefixed()
    {
        var webkit = Expand("-webkit-mask-clip: padding-box;");
        var plain = Expand("mask-clip: padding-box;");

        var wv = webkit.Single(d => d.Id == PropertyId.MaskClip).Value;
        var pv = plain.Single(d => d.Id == PropertyId.MaskClip).Value;
        wv.Should().Be(pv);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-composite
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskComposite_parses_to_MaskComposite_and_matches_unprefixed()
    {
        var webkit = Expand("-webkit-mask-composite: add;");
        var plain = Expand("mask-composite: add;");

        var wv = webkit.Single(d => d.Id == PropertyId.MaskComposite).Value;
        var pv = plain.Single(d => d.Id == PropertyId.MaskComposite).Value;
        wv.Should().Be(pv);
    }

    // -----------------------------------------------------------------------
    // -webkit-mask-mode
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitMaskMode_parses_to_MaskMode_and_matches_unprefixed()
    {
        var webkit = Expand("-webkit-mask-mode: alpha;");
        var plain = Expand("mask-mode: alpha;");

        var wv = webkit.Single(d => d.Id == PropertyId.MaskMode).Value;
        var pv = plain.Single(d => d.Id == PropertyId.MaskMode).Value;
        wv.Should().Be(pv);
    }

    // -----------------------------------------------------------------------
    // -webkit-background-clip: text  (the classic gradient-text trick)
    // -----------------------------------------------------------------------

    [TestMethod]
    public void WebkitBackgroundClip_text_parses_to_BackgroundClip_text()
    {
        var webkit = Expand("-webkit-background-clip: text;");
        var plain = Expand("background-clip: text;");

        webkit.Should().ContainSingle(d => d.Id == PropertyId.BackgroundClip);
        var wv = webkit.Single(d => d.Id == PropertyId.BackgroundClip).Value;
        var pv = plain.Single(d => d.Id == PropertyId.BackgroundClip).Value;
        wv.Should().Be(pv);
    }

    [TestMethod]
    public void WebkitBackgroundClip_border_box_matches_unprefixed()
    {
        var webkit = Expand("-webkit-background-clip: border-box;");
        var plain = Expand("background-clip: border-box;");

        var wv = webkit.Single(d => d.Id == PropertyId.BackgroundClip).Value;
        var pv = plain.Single(d => d.Id == PropertyId.BackgroundClip).Value;
        wv.Should().Be(pv);
    }
}
