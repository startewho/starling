using AwesomeAssertions;
using Starling.Css.Animations;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

/// <summary>
/// Tier 5 item 21 — smooth interpolation for box-shadow, text-shadow, and
/// gradient background-image values. Before this work the interpolator's type
/// switch never paired these raw value trees, so they snapped discretely at
/// 50% progress.
/// </summary>
[Spec("css-backgrounds-3", "https://www.w3.org/TR/css-backgrounds-3/#box-shadow")]
[Spec("css-images-3", "https://www.w3.org/TR/css-images-3/#gradients")]
[TestClass]
public sealed class ShadowGradientInterpolationTests
{
    private static CssShadow Shadow(double x, double y, double blur, double spread, CssColor? color, bool inset = false)
        => new(
            new CssLength(x, CssLengthUnit.Px),
            new CssLength(y, CssLengthUnit.Px),
            new CssLength(blur, CssLengthUnit.Px),
            new CssLength(spread, CssLengthUnit.Px),
            color,
            inset);

    // ---- box-shadow ---------------------------------------------------------

    [TestMethod]
    public void Box_shadow_single_layer_lerps_every_component_at_midpoint()
    {
        var from = new CssBoxShadow([Shadow(0, 0, 0, 0, new CssColor(0, 0, 0, 255))]);
        var to = new CssBoxShadow([Shadow(10, 20, 8, 4, new CssColor(255, 0, 0, 255))]);

        var mid = (CssBoxShadow)Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.5);

        mid.Layers.Should().HaveCount(1);
        var layer = mid.Layers[0];
        layer.OffsetX.Value.Should().BeApproximately(5, 1e-9);
        layer.OffsetY.Value.Should().BeApproximately(10, 1e-9);
        layer.Blur.Value.Should().BeApproximately(4, 1e-9);
        layer.Spread.Value.Should().BeApproximately(2, 1e-9);
        layer.Inset.Should().BeFalse();
        layer.Color!.R.Should().BeInRange(126, 130);
        layer.Color.G.Should().Be(0);
        layer.Color.B.Should().Be(0);
        layer.Color.A.Should().Be(255);
    }

    [TestMethod]
    public void Box_shadow_raw_value_list_endpoints_lerp_instead_of_snapping()
    {
        // The cascade stores box-shadow as a raw value tree, not a typed
        // CssBoxShadow — exactly what TransitionEngine hands the interpolator.
        var from = new CssValueList([
            new CssLength(0, CssLengthUnit.Px),
            new CssLength(0, CssLengthUnit.Px),
            new CssLength(2, CssLengthUnit.Px),
            new CssColor(0, 0, 0, 255),
        ]);
        var to = new CssValueList([
            new CssLength(8, CssLengthUnit.Px),
            new CssLength(8, CssLengthUnit.Px),
            new CssLength(10, CssLengthUnit.Px),
            new CssColor(0, 0, 0, 255),
        ]);

        var mid = Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.5);

        var shadow = mid.Should().BeOfType<CssBoxShadow>().Subject;
        shadow.Layers[0].OffsetX.Value.Should().BeApproximately(4, 1e-9);
        shadow.Layers[0].Blur.Value.Should().BeApproximately(6, 1e-9);
    }

    [TestMethod]
    public void Box_shadow_shorter_list_pads_with_neutral_shadows()
    {
        // 1 layer → 3 layers: the missing from-layers act as all-zero
        // transparent shadows with the paired layer's inset flag.
        var from = new CssBoxShadow([Shadow(0, 0, 0, 0, new CssColor(255, 0, 0, 255))]);
        var to = new CssBoxShadow([
            Shadow(10, 0, 0, 0, new CssColor(255, 0, 0, 255)),
            Shadow(20, 20, 10, 0, new CssColor(0, 0, 255, 255)),
            Shadow(0, 0, 0, 8, new CssColor(0, 255, 0, 255), inset: true),
        ]);

        var mid = (CssBoxShadow)Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.5);

        mid.Layers.Should().HaveCount(3);
        mid.Layers[0].OffsetX.Value.Should().BeApproximately(5, 1e-9);
        // Layer 2 lerps from the neutral shadow: half offsets, half blur,
        // alpha fading in from transparent.
        mid.Layers[1].OffsetX.Value.Should().BeApproximately(10, 1e-9);
        mid.Layers[1].OffsetY.Value.Should().BeApproximately(10, 1e-9);
        mid.Layers[1].Blur.Value.Should().BeApproximately(5, 1e-9);
        mid.Layers[1].Color!.A.Should().BeInRange(126, 130);
        // Layer 3 keeps the paired layer's inset flag on the padded side.
        mid.Layers[2].Inset.Should().BeTrue();
        mid.Layers[2].Spread.Value.Should().BeApproximately(4, 1e-9);
    }

    [TestMethod]
    public void Box_shadow_inset_mismatch_falls_back_to_discrete()
    {
        var from = new CssBoxShadow([Shadow(0, 0, 0, 0, new CssColor(0, 0, 0, 255))]);
        var to = new CssBoxShadow([Shadow(10, 10, 0, 0, new CssColor(0, 0, 0, 255), inset: true)]);

        var below = Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.4);
        var above = Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.6);

        below.Should().BeSameAs(from);
        above.Should().BeSameAs(to);
    }

    [TestMethod]
    public void Box_shadow_blur_never_goes_negative()
    {
        // Both endpoint blurs are valid (>= 0); every in-range sample of the
        // lerp must stay >= 0 too.
        var from = new CssBoxShadow([Shadow(0, 0, 0, 0, new CssColor(0, 0, 0, 255))]);
        var to = new CssBoxShadow([Shadow(0, 0, 12, 0, new CssColor(0, 0, 0, 255))]);

        for (var p = 0.05; p < 1.0; p += 0.05)
        {
            var mid = (CssBoxShadow)Interpolator.Interpolate(PropertyId.BoxShadow, from, to, p);
            mid.Layers[0].Blur.Value.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [TestMethod]
    public void Box_shadow_current_color_on_both_ends_keeps_the_sentinel()
    {
        var from = new CssBoxShadow([Shadow(0, 0, 0, 0, null)]);
        var to = new CssBoxShadow([Shadow(10, 0, 0, 0, null)]);

        var mid = (CssBoxShadow)Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.5);

        mid.Layers[0].Color.Should().BeNull();
        mid.Layers[0].OffsetX.Value.Should().BeApproximately(5, 1e-9);
    }

    [TestMethod]
    public void Box_shadow_none_keyword_pads_against_a_real_shadow()
    {
        var from = new CssKeyword("none");
        var to = new CssValueList([
            new CssLength(0, CssLengthUnit.Px),
            new CssLength(6, CssLengthUnit.Px),
            new CssLength(8, CssLengthUnit.Px),
            new CssColor(0, 0, 0, 200),
        ]);

        var mid = (CssBoxShadow)Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.5);

        mid.Layers.Should().HaveCount(1);
        mid.Layers[0].OffsetY.Value.Should().BeApproximately(3, 1e-9);
        mid.Layers[0].Blur.Value.Should().BeApproximately(4, 1e-9);
        mid.Layers[0].Color!.A.Should().BeInRange(98, 102); // 200 → midpoint vs transparent
    }

    [TestMethod]
    public void Interpolated_box_shadow_round_trips_through_the_paint_parser()
    {
        // EmitBoxShadows re-parses the effective value; a typed intermediate
        // must pass straight through.
        var from = new CssBoxShadow([Shadow(0, 0, 0, 0, new CssColor(0, 0, 0, 255))]);
        var to = new CssBoxShadow([Shadow(10, 10, 4, 0, new CssColor(0, 0, 0, 255))]);

        var mid = Interpolator.Interpolate(PropertyId.BoxShadow, from, to, 0.5);

        CssBoxShadowParser.Parse(mid).Should().BeSameAs(mid);
    }

    // ---- text-shadow --------------------------------------------------------

    [TestMethod]
    public void Text_shadow_lerps_layers_and_pads_shorter_lists()
    {
        var from = new CssTextShadow([new CssTextShadowLayer(0, 0, 0, new CssColor(255, 0, 0, 255))]);
        var to = new CssTextShadow([
            new CssTextShadowLayer(4, 4, 2, new CssColor(255, 0, 0, 255)),
            new CssTextShadowLayer(8, 0, 6, new CssColor(0, 0, 255, 255)),
        ]);

        var mid = (CssTextShadow)Interpolator.Interpolate(PropertyId.TextShadow, from, to, 0.5);

        mid.Layers.Should().HaveCount(2);
        mid.Layers[0].OffsetX.Should().BeApproximately(2, 1e-9);
        mid.Layers[0].Blur.Should().BeApproximately(1, 1e-9);
        mid.Layers[1].OffsetX.Should().BeApproximately(4, 1e-9);
        mid.Layers[1].Blur.Should().BeApproximately(3, 1e-9);
        mid.Layers[1].Color!.A.Should().BeInRange(126, 130);
    }

    // ---- gradients ----------------------------------------------------------

    private static CssGradient TwoStopLinear(double angle, CssColor c0, CssColor c1, double pos0 = 0, double pos1 = 100)
        => new(
            CssGradientKind.Linear,
            Repeating: false,
            Stops:
            [
                new CssColorStop(c0, new CssGradientStopPosition(pos0, IsPercent: true)),
                new CssColorStop(c1, new CssGradientStopPosition(pos1, IsPercent: true)),
            ],
            Line: CssGradientLine.FromAngle(angle));

    [TestMethod]
    public void Linear_gradient_two_stop_lerps_colors_positions_and_angle()
    {
        var from = TwoStopLinear(0, new CssColor(255, 0, 0, 255), new CssColor(0, 0, 0, 255), 0, 80);
        var to = TwoStopLinear(90, new CssColor(0, 0, 255, 255), new CssColor(0, 0, 0, 255), 20, 100);

        var mid = (CssGradient)Interpolator.Interpolate(PropertyId.BackgroundImage, from, to, 0.5);

        mid.Kind.Should().Be(CssGradientKind.Linear);
        mid.Line!.AngleDegrees.Should().BeApproximately(45, 1e-9);
        mid.Stops.Should().HaveCount(2);
        mid.Stops[0].Position!.Value.Value.Should().BeApproximately(10, 1e-9);
        mid.Stops[1].Position!.Value.Value.Should().BeApproximately(90, 1e-9);
        mid.Stops[0].Color.R.Should().BeInRange(126, 130);
        mid.Stops[0].Color.B.Should().BeInRange(126, 130);
    }

    [TestMethod]
    public void Radial_gradient_same_shape_lerps_center_and_stops()
    {
        var fromRadial = new CssGradient(
            CssGradientKind.Radial, false,
            [
                new CssColorStop(new CssColor(255, 255, 255, 255), new CssGradientStopPosition(0, true)),
                new CssColorStop(new CssColor(0, 0, 0, 255), new CssGradientStopPosition(100, true)),
            ],
            Position: new CssGradientPosition(0.0, 0.0));
        var toRadial = fromRadial with { Position = new CssGradientPosition(1.0, 0.5) };

        var mid = (CssGradient)Interpolator.Interpolate(PropertyId.BackgroundImage, fromRadial, toRadial, 0.5);

        mid.Kind.Should().Be(CssGradientKind.Radial);
        mid.Position!.Value.FractionX.Should().BeApproximately(0.5, 1e-9);
        mid.Position.Value.FractionY.Should().BeApproximately(0.25, 1e-9);
    }

    [TestMethod]
    public void Conic_gradient_lerps_the_from_angle()
    {
        var stops = new[]
        {
            new CssColorStop(new CssColor(255, 0, 0, 255), new CssGradientStopPosition(0, true)),
            new CssColorStop(new CssColor(0, 0, 255, 255), new CssGradientStopPosition(100, true)),
        };
        var from = new CssGradient(CssGradientKind.Conic, false, stops, CssGradientLine.FromAngle(0));
        var to = new CssGradient(CssGradientKind.Conic, false, stops, CssGradientLine.FromAngle(180));

        var mid = (CssGradient)Interpolator.Interpolate(PropertyId.BackgroundImage, from, to, 0.25);

        mid.Kind.Should().Be(CssGradientKind.Conic);
        mid.Line!.AngleDegrees.Should().BeApproximately(45, 1e-9);
    }

    [TestMethod]
    public void Gradient_stop_count_mismatch_falls_back_to_discrete()
    {
        var from = TwoStopLinear(0, new CssColor(255, 0, 0, 255), new CssColor(0, 0, 255, 255));
        var to = new CssGradient(
            CssGradientKind.Linear, false,
            [
                new CssColorStop(new CssColor(255, 0, 0, 255), new CssGradientStopPosition(0, true)),
                new CssColorStop(new CssColor(0, 255, 0, 255), new CssGradientStopPosition(50, true)),
                new CssColorStop(new CssColor(0, 0, 255, 255), new CssGradientStopPosition(100, true)),
            ],
            Line: CssGradientLine.FromAngle(0));

        Interpolator.Interpolate(PropertyId.BackgroundImage, from, to, 0.4).Should().BeSameAs(from);
        Interpolator.Interpolate(PropertyId.BackgroundImage, from, to, 0.6).Should().BeSameAs(to);
    }

    [TestMethod]
    public void Gradient_kind_or_repeating_mismatch_falls_back_to_discrete()
    {
        var stops = new[]
        {
            new CssColorStop(new CssColor(255, 0, 0, 255), new CssGradientStopPosition(0, true)),
            new CssColorStop(new CssColor(0, 0, 255, 255), new CssGradientStopPosition(100, true)),
        };
        var linear = new CssGradient(CssGradientKind.Linear, false, stops, CssGradientLine.FromAngle(0));
        var radial = new CssGradient(CssGradientKind.Radial, false, stops);
        var repeating = linear with { Repeating = true };

        Interpolator.Interpolate(PropertyId.BackgroundImage, linear, radial, 0.6).Should().BeSameAs(radial);
        Interpolator.Interpolate(PropertyId.BackgroundImage, linear, repeating, 0.4).Should().BeSameAs(linear);
    }

    [TestMethod]
    public void Url_image_endpoint_falls_back_to_discrete()
    {
        var from = new CssUrl("sprite.png");
        var to = TwoStopLinear(0, new CssColor(255, 0, 0, 255), new CssColor(0, 0, 255, 255));

        Interpolator.Interpolate(PropertyId.BackgroundImage, from, to, 0.4).Should().BeSameAs(from);
        Interpolator.Interpolate(PropertyId.BackgroundImage, from, to, 0.6).Should().BeSameAs(to);
    }

    [TestMethod]
    public void Multi_layer_background_image_lerps_layer_wise_when_compatible()
    {
        var fromLayers = new CssValueList([
            TwoStopLinear(0, new CssColor(255, 0, 0, 255), new CssColor(0, 0, 0, 255)),
            TwoStopLinear(90, new CssColor(0, 255, 0, 255), new CssColor(0, 0, 0, 255)),
        ]);
        var toLayers = new CssValueList([
            TwoStopLinear(90, new CssColor(0, 0, 255, 255), new CssColor(0, 0, 0, 255)),
            TwoStopLinear(270, new CssColor(0, 255, 0, 255), new CssColor(0, 0, 0, 255)),
        ]);

        var mid = Interpolator.Interpolate(PropertyId.BackgroundImage, fromLayers, toLayers, 0.5);

        var list = mid.Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().HaveCount(2);
        ((CssGradient)list.Values[0]).Line!.AngleDegrees.Should().BeApproximately(45, 1e-9);
        ((CssGradient)list.Values[1]).Line!.AngleDegrees.Should().BeApproximately(180, 1e-9);
    }

    [TestMethod]
    public void Multi_layer_count_mismatch_falls_back_to_discrete()
    {
        var one = TwoStopLinear(0, new CssColor(255, 0, 0, 255), new CssColor(0, 0, 0, 255));
        var two = new CssValueList([
            TwoStopLinear(0, new CssColor(255, 0, 0, 255), new CssColor(0, 0, 0, 255)),
            TwoStopLinear(90, new CssColor(0, 255, 0, 255), new CssColor(0, 0, 0, 255)),
        ]);

        Interpolator.Interpolate(PropertyId.BackgroundImage, (CssValue)one, two, 0.6).Should().BeSameAs(two);
    }

    [TestMethod]
    public void Raw_gradient_function_endpoints_lerp_via_the_parser_cache()
    {
        // TransitionEngine hands the interpolator raw CssFunctionValue trees;
        // verify the parse-and-lerp path end to end on real parser output.
        static CssValue ParseValue(string source)
        {
            var sheet = new Parser.CssParser("a{background-image:" + source + "}").ParseStyleSheet();
            var decl = ((Parser.StyleRule)sheet.Rules.Single()).Declarations.Single();
            return CssValueParser.Parse(decl.Value);
        }

        var from = ParseValue("linear-gradient(0deg, red 0%, black 100%)");
        var to = ParseValue("linear-gradient(90deg, blue 0%, black 100%)");

        var mid = Interpolator.Interpolate(PropertyId.BackgroundImage, from, to, 0.5);

        var g = mid.Should().BeOfType<CssGradient>().Subject;
        g.Line!.AngleDegrees.Should().BeApproximately(45, 1e-9);
        g.Stops[0].Color.R.Should().BeInRange(126, 130);
        g.Stops[0].Color.B.Should().BeInRange(126, 130);
    }

    // ---- whitelist + transition integration ---------------------------------

    [TestMethod]
    public void Shadow_and_background_image_properties_are_animatable()
    {
        Interpolator.IsAnimatable(PropertyId.BoxShadow).Should().BeTrue();
        Interpolator.IsAnimatable(PropertyId.TextShadow).Should().BeTrue();
        Interpolator.IsAnimatable(PropertyId.BackgroundImage).Should().BeTrue();
    }

    [TestMethod]
    public void Box_shadow_transition_produces_a_mid_value_at_half_duration()
    {
        static Func<PropertyId, CssValue?> Props() => id => id switch
        {
            PropertyId.TransitionProperty => new CssKeyword("all"),
            PropertyId.TransitionDuration => new CssTime(200, CssTimeUnit.Milliseconds),
            PropertyId.TransitionDelay => new CssTime(0, CssTimeUnit.Seconds),
            PropertyId.TransitionTimingFunction => new CssKeyword("linear"),
            _ => null,
        };

        var engine = new TransitionEngine();
        var el = new Element("div");
        var from = new CssBoxShadow([Shadow(0, 0, 0, 0, new CssColor(0, 0, 0, 255))]);
        var to = new CssBoxShadow([Shadow(16, 16, 8, 0, new CssColor(0, 0, 0, 255))]);

        engine.OnComputedValueChanged(el, PropertyId.BoxShadow, from, Props());
        engine.OnComputedValueChanged(el, PropertyId.BoxShadow, to, Props());
        engine.ActiveCount.Should().Be(1);

        engine.Tick(100); // half of the 200ms duration, linear easing

        var mid = engine.GetEffective(el, PropertyId.BoxShadow)
            .Should().BeOfType<CssBoxShadow>().Subject;
        mid.Should().NotBeSameAs(to);
        mid.Layers[0].OffsetX.Value.Should().BeApproximately(8, 1e-6);
        mid.Layers[0].Blur.Value.Should().BeApproximately(4, 1e-6);
    }
}
