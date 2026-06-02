namespace Starling.Gui.Mcp;

public interface IBrowserController
{
    Task<BrowserControlResult> NavigateFromToolAsync(string url, CancellationToken ct);
    Task<BrowserControlResult> BackFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ForwardFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ReloadFromToolAsync(CancellationToken ct);
    Task<BrowserControlResult> ScreenshotFromToolAsync(string path, CancellationToken ct);
    // Task<BrowserControlResult> ScreenshotViewportFromToolAsync(string path, CancellationToken ct);
    Task<BrowserControlResult> InspectFromToolAsync(bool includeHtml, string? logPath, CancellationToken ct);
    Task<BrowserControlResult> ConsoleFromToolAsync(string? minLevel, int limit, CancellationToken ct);
    Task<BrowserControlResult> NetworkFromToolAsync(int limit, CancellationToken ct);
    Task<BrowserControlResult> ClickFromToolAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> ClickSelectorFromToolAsync(string selector, CancellationToken ct);
    Task<BrowserControlResult> MoveMouseFromToolAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> ScrollFromToolAsync(double deltaX, double deltaY, CancellationToken ct);
    Task<BrowserControlResult> ScrollToFromToolAsync(double? x, double? y, string? selector, string? position, CancellationToken ct);
    Task<BrowserControlResult> PressKeyFromToolAsync(string key, bool shift, bool ctrl, bool alt, bool meta, CancellationToken ct);
    Task<BrowserControlResult> TypeTextFromToolAsync(string text, bool submit, CancellationToken ct);
    Task<BrowserControlResult> ResizeFromToolAsync(double width, double height, CancellationToken ct);
    Task<BrowserControlResult> WaitFromToolAsync(string state, string? value, int timeoutMs, CancellationToken ct);
    Task<BrowserControlResult> QueryFromToolAsync(string selector, bool includeText, bool includeHtml, int limit, CancellationToken ct);
    Task<BrowserControlResult> HighlightFromToolAsync(string selector, string? color, CancellationToken ct);
    Task<BrowserControlResult> SelectElementFromToolAsync(string selector, CancellationToken ct);
    Task<BrowserControlResult> FocusElementFromToolAsync(string selector, CancellationToken ct);
    Task<BrowserControlResult> FindFromToolAsync(string query, string direction, CancellationToken ct);
    Task<BrowserControlResult> ClipboardFromToolAsync(string action, string? text, CancellationToken ct);
    Task<BrowserControlResult> BookmarksFromToolAsync(string? id, CancellationToken ct);
    Task<BrowserControlResult> ComputedStyleFromToolAsync(string selector, CancellationToken ct);
}
