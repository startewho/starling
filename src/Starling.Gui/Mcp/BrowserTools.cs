using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Starling.Gui.Mcp;

[McpServerToolType]
public sealed class BrowserTools
{
    private readonly BrowserControlBridge _browser;

    public BrowserTools(BrowserControlBridge browser)
    {
        _browser = browser;
    }

    [McpServerTool(Name = "browser_navigate", UseStructuredContent = true),
     Description("Navigate the visible Starling browser window to a URL.")]
    public Task<BrowserControlResult> BrowserNavigate(
        [Description("The absolute URL to load, for example https://example.com.")]
        string url,
        CancellationToken cancellationToken)
        => _browser.NavigateAsync(url, cancellationToken);

    [McpServerTool(Name = "browser_back", UseStructuredContent = true),
     Description("Navigate the visible Starling browser window back in history.")]
    public Task<BrowserControlResult> BrowserBack(CancellationToken cancellationToken)
        => _browser.BackAsync(cancellationToken);

    [McpServerTool(Name = "browser_forward", UseStructuredContent = true),
     Description("Navigate the visible Starling browser window forward in history.")]
    public Task<BrowserControlResult> BrowserForward(CancellationToken cancellationToken)
        => _browser.ForwardAsync(cancellationToken);

    [McpServerTool(Name = "browser_refresh", UseStructuredContent = true),
     Description("Reload the current page in the visible Starling browser window.")]
    public Task<BrowserControlResult> BrowserRefresh(CancellationToken cancellationToken)
        => _browser.ReloadAsync(cancellationToken);

    [McpServerTool(Name = "browser_screenshot", UseStructuredContent = true),
     Description("Capture the current page in the visible Starling browser window to a PNG "
        + "file. Renders the full scroll extent. The written path is returned in `detail`.")]
    public Task<BrowserControlResult> BrowserScreenshot(
        [Description("Output PNG path. Relative paths resolve against the GUI's working "
            + "directory. Defaults to starling-screenshot.png.")]
        string path,
        CancellationToken cancellationToken)
        => _browser.ScreenshotAsync(path, cancellationToken);

    [McpServerTool(Name = "browser_inspect", UseStructuredContent = true),
     Description("Inspect the current page: URL, title, live-scripting state, and recent "
        + "JS console warnings/errors. Returns the report in `detail`. Optionally include "
        + "the serialized outerHTML and/or dump a full telemetry+HTML report to a logfile.")]
    public Task<BrowserControlResult> BrowserInspect(
        [Description("Include the page's serialized outerHTML in the response (truncated to 100 KB).")]
        bool includeHtml,
        [Description("If set, write a full report (all telemetry logs + complete outerHTML) to this file path.")]
        string? logPath,
        CancellationToken cancellationToken)
        => _browser.InspectAsync(includeHtml, logPath, cancellationToken);

    [McpServerTool(Name = "browser_click", UseStructuredContent = true),
     Description("Left-click a point on the current page in the visible Starling browser window. "
        + "Coordinates are page pixels from the document's top-left — the same space "
        + "browser_screenshot captures (full scroll extent) — so click where you see the target "
        + "in a screenshot. Clicking a text field focuses it (follow with browser_type); clicking "
        + "a link, button, or checkbox activates it. The outcome is returned in `detail`.")]
    public Task<BrowserControlResult> BrowserClick(
        [Description("X coordinate in page pixels from the document's left edge.")]
        double x,
        [Description("Y coordinate in page pixels from the document's top edge.")]
        double y,
        CancellationToken cancellationToken)
        => _browser.ClickAsync(x, y, cancellationToken);

    [McpServerTool(Name = "browser_move", UseStructuredContent = true),
     Description("Move the mouse to a point on the current page, updating hover/cursor state and "
        + "dispatching DOM mouseover/mousemove/mouseout so JS hover handlers run. Coordinates are "
        + "page pixels from the document's top-left (same space as browser_screenshot). What is "
        + "now under the cursor is returned in `detail`.")]
    public Task<BrowserControlResult> BrowserMove(
        [Description("X coordinate in page pixels from the document's left edge.")]
        double x,
        [Description("Y coordinate in page pixels from the document's top edge.")]
        double y,
        CancellationToken cancellationToken)
        => _browser.MoveMouseAsync(x, y, cancellationToken);

    [McpServerTool(Name = "browser_type", UseStructuredContent = true),
     Description("Type text into the currently focused text field — focus one first with "
        + "browser_click. Fires a DOM input event so search-as-you-type and form handlers run. "
        + "Set submit=true to press Enter afterward, submitting the owning form. The field's new "
        + "value is returned in `detail`.")]
    public Task<BrowserControlResult> BrowserType(
        [Description("The literal text to type. Control characters are ignored.")]
        string text,
        [Description("Press Enter after typing to submit the owning form. Defaults to false.")]
        bool submit,
        CancellationToken cancellationToken)
        => _browser.TypeTextAsync(text, submit, cancellationToken);
}
