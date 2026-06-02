namespace Starling.Gui.Mcp;

public interface IBrowserControlDispatcher
{
    Task<BrowserControlResult> NavigateAsync(string url, CancellationToken ct);
    Task<BrowserControlResult> BackAsync(CancellationToken ct);
    Task<BrowserControlResult> ForwardAsync(CancellationToken ct);
    Task<BrowserControlResult> ReloadAsync(CancellationToken ct);
    Task<BrowserControlResult> ScreenshotAsync(string path, CancellationToken ct);
    // Task<BrowserControlResult> ScreenshotViewportAsync(string path, CancellationToken ct);
    Task<BrowserControlResult> InspectAsync(bool includeHtml, string? logPath, CancellationToken ct);
    Task<BrowserControlResult> ConsoleAsync(string? minLevel, int limit, CancellationToken ct);
    Task<BrowserControlResult> NetworkAsync(int limit, CancellationToken ct);
    Task<BrowserControlResult> ClickAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> ClickSelectorAsync(string selector, CancellationToken ct);
    Task<BrowserControlResult> MoveMouseAsync(double x, double y, CancellationToken ct);
    Task<BrowserControlResult> ScrollAsync(double deltaX, double deltaY, CancellationToken ct);
    Task<BrowserControlResult> ScrollToAsync(double? x, double? y, string? selector, string? position, CancellationToken ct);
    Task<BrowserControlResult> PressKeyAsync(string key, bool shift, bool ctrl, bool alt, bool meta, CancellationToken ct);
    Task<BrowserControlResult> TypeTextAsync(string text, bool submit, CancellationToken ct);
    Task<BrowserControlResult> ResizeAsync(double width, double height, CancellationToken ct);
    Task<BrowserControlResult> WaitAsync(string state, string? value, int timeoutMs, CancellationToken ct);
    Task<BrowserControlResult> QueryAsync(string selector, bool includeText, bool includeHtml, int limit, CancellationToken ct);
    Task<BrowserControlResult> HighlightAsync(string selector, string? color, CancellationToken ct);
    Task<BrowserControlResult> SelectElementAsync(string selector, CancellationToken ct);
    Task<BrowserControlResult> FocusElementAsync(string selector, CancellationToken ct);
    Task<BrowserControlResult> FindAsync(string query, string direction, CancellationToken ct);
    Task<BrowserControlResult> ClipboardAsync(string action, string? text, CancellationToken ct);
    Task<BrowserControlResult> BookmarksAsync(string? id, CancellationToken ct);
    Task<BrowserControlResult> ComputedStyleAsync(string selector, CancellationToken ct);
}
