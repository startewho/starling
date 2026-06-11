using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Engine;
using Starling.Gui.Controls;
using Starling.Gui.Theme;
using EngineSize = SixLabors.ImageSharp.Size;

namespace Starling.Gui.Headless.Tests;

/// <summary>
/// End-to-end reproduction of the reported bug: while the mouse sweeps over the
/// page, animated content lost its styling / went invisible until the pointer
/// stopped. The cause was the hover re-cascade overriding the WHOLE hovered
/// subtree every move, shadowing each element's animated style with the static
/// :hover sample. This drives a real <see cref="WebviewPanel"/> with a live,
/// infinitely-animating element inside a container that has a :hover rule, then
/// hovers the container — the animating element must keep being driven by the
/// animation (not pulled into the hover override set), which is exactly what the
/// MCP browser_computed_style tool reports.
/// </summary>
public class WebviewPanelHoverAnimationTests
{
    [AvaloniaFact]
    public async Task Animating_element_keeps_its_animation_while_an_ancestor_is_hovered()
    {
        // #box pulses forever (always an active animation). Its container changes
        // background on :hover. Hovering the container makes the container match
        // section:hover, but #box has no :hover rule of its own — pre-fix it was
        // still swept into the override set and frozen at its static style.
        var (engine, page) = await LoadInteractiveAsync(
            "<!doctype html><html><head><style>" +
            "#wrap { background: rgb(0,128,0); padding: 30px; }" +
            "#wrap:hover { background: rgb(0,0,255); }" +
            "#box { width: 80px; height: 80px; background: red;" +
            " animation: pulse 2s linear infinite; }" +
            "@keyframes pulse { 0% { opacity: 1 } 50% { opacity: 0.2 } 100% { opacity: 1 } }" +
            "</style></head><body><div id=\"wrap\"><div id=\"box\"></div></div></body></html>");

        var (window, panel) = ShowPanel(engine, page);
        try
        {
            var wrap = page.Document.GetElementById("wrap")!;
            var box = page.Document.GetElementById("box")!;

            // Advance the animation clock so the pulse is mid-flight (opacity < 1),
            // the same way the live timer would each frame.
            engine.HasActiveAnimations(page).Should().BeTrue(
                "the infinite pulse animation should be primed and in flight");
            engine.PrepareAnimationFrame(page, 500);

            // Simulate the pointer landing on the container mid-sweep.
            panel.HoverElementForTest(wrap);

            // The container's own :hover background change is overridden (so it
            // repaints blue) ...
            panel.HoverScopeContainsForTest(wrap).Should().BeTrue(
                "the :hover-styled container should be in the override set");

            // ... but the animating child must NOT be — overriding it would shadow
            // its animated opacity with the static sample (the invisibility bug).
            panel.HoverScopeContainsForTest(box).Should().BeFalse(
                "the animating element has no :hover rule and must keep its animated style");

            // The effective painted style the MCP browser_computed_style tool
            // reports: #box is still driven by the animation, not a hover override.
            var report = panel.InspectComputedStyle("#box");
            report.Ok.Should().BeTrue();
            report.Detail.Should().Contain("animating=yes",
                "the pulse animation is still driving #box while the ancestor is hovered");
            report.Detail.Should().Contain("hoverOverride=no",
                "#box must not be shadowed by a hover override mid-move");
        }
        finally { Teardown(window, panel); }
    }

    private static (Window, WebviewPanel) ShowPanel(StarlingEngine engine, LaidOutPage page)
    {
        GpuTests.SkipUnlessAvailable();
        var panel = new WebviewPanel(
            new ThemeManager(), NullLoggerFactory.Instance, _ => { }, (_, _) => { },
            (p, vp) => engine.RelayoutPage(p, new RenderOptions(vp, FontSize: 16f)),
            prepareAnimationFrame: (pg, ms) => engine.PrepareAnimationFrame(pg, ms),
            hasActiveAnimations: engine.HasActiveAnimations);
        var window = new Window { Content = panel, Width = 400, Height = 300 };
        window.Show();
        panel.ShowPage(page);
        window.CaptureRenderedFrame();
        return (window, panel);
    }

    private static async Task<(StarlingEngine, LaidOutPage)> LoadInteractiveAsync(string html)
    {
        var engine = new StarlingEngine();
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"starling-hoveranim-{System.Guid.NewGuid():N}.html");
        System.IO.File.WriteAllText(path, html);
        var url = "file://" + path.Replace('\\', '/');
        // onFirstPaint marks the load interactive, which primes declarative
        // @keyframes animations into the live AnimationEngine.
        var result = await engine.LayoutPageAsync(
            url, new RenderOptions(new EngineSize(400, 300), FontSize: 16f),
            CancellationToken.None, onFirstPaint: _ => { });
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
        return (engine, result.Value);
    }

    private static void Teardown(Window window, WebviewPanel panel)
    {
        window.Close();
        panel.Dispose();
    }
}
