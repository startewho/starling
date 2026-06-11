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
/// Regression guard for the "everything goes invisible while the mouse moves" bug.
/// Moving the pointer over a container used to re-cascade and override that
/// container's WHOLE subtree on every move. The override shadowed each descendant's
/// animated / laid-out style, so animated or freshly-styled content flashed to its
/// base state (often invisible) until the pointer stopped. The fix prunes the
/// override set to the elements whose :hover cascade actually changes a
/// paint-relevant property, leaving everyone else on their normal style.
/// </summary>
public class WebviewPanelHoverScopeTests
{
    [AvaloniaFact]
    public async Task Hover_overrides_only_the_hover_styled_element_not_its_subtree()
    {
        GpuTests.SkipUnlessAvailable();
        // <section> changes background on :hover; the nested <div> has no
        // :hover-dependent style. Hovering the div makes <section> match
        // `section:hover` as an ancestor, but the div itself must not be pulled
        // into the override set — doing so shadows its style (the invisibility bug).
        var html = "<!doctype html><html><head><style>"
            + "section { background: rgb(0,128,0); padding: 20px; }"
            + "section:hover { background: rgb(0,0,255); }"
            + "div { background: rgb(200,0,0); width: 50px; height: 50px; }"
            + "</style></head><body><section id=\"s\"><div id=\"d\">x</div></section></body></html>";

        var engine = new StarlingEngine();
        var result = await engine.LayoutPageAsync(
            WriteFixture(html), new RenderOptions(new EngineSize(800, 600), FontSize: 16f),
            CancellationToken.None);
        result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
        var page = result.Value;

        var panel = new WebviewPanel(
            new ThemeManager(), NullLoggerFactory.Instance, _ => { }, (_, _) => { },
            (p, vp) => engine.RelayoutPage(p, new RenderOptions(vp, FontSize: 16f)));
        var window = new Window { Content = panel, Width = 800, Height = 600 };
        window.Show();
        panel.ShowPage(page);
        window.CaptureRenderedFrame(); // force a measure/arrange so the canvas has bounds
        try
        {
            var section = page.Document.GetElementById("s")!;
            var div = page.Document.GetElementById("d")!;

            panel.HoverElementForTest(div);

            panel.HoverScopeContainsForTest(section).Should().BeTrue(
                "the :hover-styled container is overridden so its hover background paints");
            panel.HoverScopeContainsForTest(div).Should().BeFalse(
                "a descendant with no :hover rule must not be overridden — shadowing its style is the bug");
            panel.HoverOverrideCountForTest.Should().Be(1,
                "only the genuinely :hover-affected element is overridden, not the whole subtree or ancestors");
        }
        finally
        {
            window.Close();
            panel.Dispose();
        }
    }

    private static string WriteFixture(string html)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"starling-hoverscope-{System.Guid.NewGuid():N}.html");
        System.IO.File.WriteAllText(path, html);
        return "file://" + path.Replace('\\', '/');
    }
}
