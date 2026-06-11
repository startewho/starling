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
/// Regression test for the reported selection-flash bug. On the GPU surface path
/// every overlay change is an immediate, synchronous swapchain present. A
/// drag-select used to present TWICE per pointer-move: ClearSelectionOverlays
/// presented a selection-ABSENT frame, then the rebuild presented the highlight —
/// so the selection flickered off/on on every move. UpdateSelection now coalesces
/// the clear and rebuild into a single present (the same _suppressPresent batching
/// ShowPage uses), so a move presents exactly once.
/// </summary>
public class WebviewPanelSelectionTests
{
    [AvaloniaFact]
    public async Task Drag_select_move_presents_once_not_twice()
    {
        var (engine, page) = await LoadAsync(
            "<!doctype html><html><body><p>hello world, this is a paragraph of " +
            "selectable text spanning enough words to place several fragments.</p>" +
            "</body></html>");

        var (window, panel) = ShowPanel(engine, page);
        try
        {
            var span = panel.FullTextSelectionSpanForTest;
            span.Should().NotBeNull("the page should lay out selectable text fragments");

            // First move: builds a selection from nothing.
            var before = panel.OverlayPresentRequestsForTest;
            panel.DragSelectForTest(span!.Value.Anchor, span.Value.Cursor);
            var firstMovePresents = panel.OverlayPresentRequestsForTest - before;

            panel.SelectionOverlayCountForTest.Should().BeGreaterThan(0,
                "the drag should produce selection-highlight rects");
            firstMovePresents.Should().Be(1,
                "a drag-select move must present exactly once — the clear and rebuild coalesce");

            // Second move is the realistic flash case: there is now a PRIOR selection
            // to clear before rebuilding. It must still present exactly once, never a
            // blank-then-filled pair.
            before = panel.OverlayPresentRequestsForTest;
            panel.DragSelectForTest(span.Value.Anchor, span.Value.Cursor);
            var secondMovePresents = panel.OverlayPresentRequestsForTest - before;

            secondMovePresents.Should().Be(1,
                "extending a selection must not flash the prior highlight off then on");
        }
        finally { Teardown(window, panel); }
    }

    [AvaloniaFact]
    public async Task Drag_select_collapsing_to_empty_still_presents_the_cleared_state()
    {
        var (engine, page) = await LoadAsync(
            "<!doctype html><html><body><p>some selectable words here</p></body></html>");

        var (window, panel) = ShowPanel(engine, page);
        try
        {
            var span = panel.FullTextSelectionSpanForTest;
            span.Should().NotBeNull();

            // Establish a selection.
            panel.DragSelectForTest(span!.Value.Anchor, span.Value.Cursor);
            panel.SelectionOverlayCountForTest.Should().BeGreaterThan(0);

            // Collapse it to an empty range (anchor == cursor). The early-return path
            // must still fire exactly one present so the cleared highlight actually
            // disappears on screen — this is why the single present lives in finally.
            var before = panel.OverlayPresentRequestsForTest;
            panel.DragSelectForTest(span.Value.Anchor, span.Value.Anchor);
            var presents = panel.OverlayPresentRequestsForTest - before;

            panel.SelectionOverlayCountForTest.Should().Be(0,
                "an empty range clears the selection");
            presents.Should().Be(1,
                "clearing the selection must still present once so the highlight is erased");
        }
        finally { Teardown(window, panel); }
    }

    private static (Window, WebviewPanel) ShowPanel(StarlingEngine engine, LaidOutPage page)
    {
        GpuTests.SkipUnlessAvailable();
        var panel = new WebviewPanel(
            new ThemeManager(), NullLoggerFactory.Instance, _ => { }, (_, _) => { },
            (p, vp) => engine.RelayoutPage(p, new RenderOptions(vp, FontSize: 16f)));
        var window = new Window { Content = panel, Width = 800, Height = 600 };
        window.Show();
        panel.ShowPage(page);
        window.CaptureRenderedFrame();
        return (window, panel);
    }

    private static async Task<(StarlingEngine, LaidOutPage)> LoadAsync(string html)
    {
        var engine = new StarlingEngine();
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"starling-selection-{System.Guid.NewGuid():N}.html");
        System.IO.File.WriteAllText(path, html);
        var url = "file://" + path.Replace('\\', '/');
        var result = await engine.LayoutPageAsync(
            url, new RenderOptions(new EngineSize(800, 600), FontSize: 16f),
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
