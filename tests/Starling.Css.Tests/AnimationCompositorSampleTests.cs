using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;

namespace Starling.Css.Tests;

/// <summary>
/// <see cref="Starling.Css.Animations.AnimationCompositor.Sample"/> — the
/// sampling-only compose the live GUI uses on pure animation ticks. It must
/// produce the same animated values as the full <c>Compose</c> over the same
/// base, and it must NOT poison the transition snapshots when the base carries
/// baked mid-tween values (the box's last-relayout style).
/// </summary>
[TestClass]
public sealed class AnimationCompositorSampleTests
{
    private static StyleEngine MakeEngine(string css)
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        var sheet = new CssParser(css).ParseStyleSheet(StyleOrigin.Author);
        engine.AddStyleSheet(sheet);
        return engine;
    }

    private static Element MakeDiv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        doc.AppendChild(html);
        var body = doc.CreateElement("body");
        html.AppendChild(body);
        var div = doc.CreateElement("div");
        body.AppendChild(div);
        return div;
    }

    [TestMethod]
    public void Sample_matches_full_compose_for_an_active_animation()
    {
        var engine = MakeEngine("""
            @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
            div { animation: fade 1000ms linear; opacity: 0.25; }
        """);
        var el = MakeDiv();

        // Prime playback through the full compose (the relayout path).
        var baked = engine.ComputeWithAnimations(el, nowMs: 0);
        engine.AnimationEngine.Tick(500);

        var full = engine.ComputeWithAnimations(el, nowMs: 500);
        var sampled = engine.Compositor.Sample(el, baked);

        ((CssNumber)sampled.Get(PropertyId.Opacity)).Value
            .Should().BeApproximately(((CssNumber)full.Get(PropertyId.Opacity)).Value, 1e-6);
    }

    [TestMethod]
    public void Sample_over_a_baked_base_does_not_restart_a_transition()
    {
        var engine = MakeEngine("""
            div { transition: opacity 1000ms linear; opacity: 1; }
            div.faded { opacity: 0; }
        """);
        var el = MakeDiv();

        // Start a 1 -> 0 transition through the full compose.
        _ = engine.ComputeWithAnimations(el, nowMs: 0);
        el.SetAttribute("class", "faded");
        _ = engine.ComputeWithAnimations(el, nowMs: 0);

        // Midpoint: the relayout path bakes opacity 0.5 into the box style.
        engine.TransitionEngine.Tick(500);
        var baked = engine.ComputeWithAnimations(el, nowMs: 500);
        ((CssNumber)baked.Get(PropertyId.Opacity)).Value.Should().BeApproximately(0.5, 1e-6);

        // Pure ticks sample over the baked base. If Sample fed the trigger
        // detection, the 0.5 would look like a new static value and restart
        // the tween toward it; instead the transition must keep heading to 0.
        _ = engine.Compositor.Sample(el, baked);
        _ = engine.Compositor.Sample(el, baked);
        engine.TransitionEngine.Tick(750);
        var s = engine.Compositor.Sample(el, baked);
        ((CssNumber)s.Get(PropertyId.Opacity)).Value.Should().BeApproximately(0.25, 1e-6);

        engine.TransitionEngine.Tick(1000);
        var done = engine.ComputeWithAnimations(el, nowMs: 1000);
        ((CssNumber)done.Get(PropertyId.Opacity)).Value.Should().BeApproximately(0, 1e-6);
    }

    [TestMethod]
    public void Sample_returns_the_base_unchanged_when_nothing_is_active()
    {
        var engine = MakeEngine("div { opacity: 0.5; }");
        var el = MakeDiv();
        var baseStyle = engine.Compute(el);
        engine.Compositor.Sample(el, baseStyle).Should().BeSameAs(baseStyle);
    }
}
