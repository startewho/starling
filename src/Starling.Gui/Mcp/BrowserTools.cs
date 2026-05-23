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
}
