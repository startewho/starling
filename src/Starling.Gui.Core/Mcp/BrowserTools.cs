using System.Text.Json;
using System.Text.Json.Nodes;
using Starling.Mcp;

namespace Starling.Gui.Mcp;

/// <summary>
/// MCP tool group that drives the visible Starling browser
/// window. Owns the tool descriptors and dispatches tools/call onto
/// <see cref="IBrowserControlDispatcher"/>.
/// </summary>
public sealed class BrowserTools : IMcpToolGroup
{
    private readonly IBrowserControlDispatcher _browser;

    public BrowserTools(IBrowserControlDispatcher browser)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
    }

    public string GetToolDescriptorsJson() => ToolDescriptorsJson;

    public bool HasTool(string name) => name switch
    {
        "browser_navigate"
            or "browser_back"
            or "browser_forward"
            or "browser_refresh"
            or "browser_screenshot"
            or "browser_screenshot_viewport"
            or "browser_inspect"
            or "browser_console"
            or "browser_network"
            or "browser_click"
            or "browser_click_selector"
            or "browser_move"
            or "browser_scroll"
            or "browser_scroll_to"
            or "browser_press_key"
            or "browser_type"
            or "browser_resize"
            or "browser_wait"
            or "browser_query"
            or "browser_highlight"
            or "browser_select"
            or "browser_focus"
            or "browser_find"
            or "browser_clipboard"
            or "browser_bookmarks"
            or "browser_computed_style" => true,
        _ => false,
    };

    public async Task<McpToolResult> InvokeAsync(string name, JsonElement arguments, CancellationToken ct)
    {
        BrowserControlResult result = name switch
        {
            "browser_navigate" => await _browser.NavigateAsync(
                McpArgumentReader.RequireString(arguments, "url"), ct).ConfigureAwait(false),
            "browser_back" => await _browser.BackAsync(ct).ConfigureAwait(false),
            "browser_forward" => await _browser.ForwardAsync(ct).ConfigureAwait(false),
            "browser_refresh" => await _browser.ReloadAsync(ct).ConfigureAwait(false),
            "browser_screenshot" => await _browser.ScreenshotAsync(
                McpArgumentReader.ReadString(arguments, "path"), ct).ConfigureAwait(false),
            "browser_screenshot_viewport" => await _browser.ScreenshotViewportAsync(
                McpArgumentReader.ReadString(arguments, "path"), ct).ConfigureAwait(false),
            "browser_inspect" => await _browser.InspectAsync(
                McpArgumentReader.ReadBool(arguments, "includeHtml"),
                McpArgumentReader.ReadOptionalString(arguments, "logPath"),
                ct).ConfigureAwait(false),
            "browser_console" => await _browser.ConsoleAsync(
                McpArgumentReader.ReadOptionalString(arguments, "minLevel"),
                McpArgumentReader.ReadIntOr(arguments, "limit", 100),
                ct).ConfigureAwait(false),
            "browser_network" => await _browser.NetworkAsync(
                McpArgumentReader.ReadIntOr(arguments, "limit", 100),
                ct).ConfigureAwait(false),
            "browser_click" => await _browser.ClickAsync(
                McpArgumentReader.RequireDouble(arguments, "x"),
                McpArgumentReader.RequireDouble(arguments, "y"),
                ct).ConfigureAwait(false),
            "browser_click_selector" => await _browser.ClickSelectorAsync(
                McpArgumentReader.RequireString(arguments, "selector"),
                ct).ConfigureAwait(false),
            "browser_move" => await _browser.MoveMouseAsync(
                McpArgumentReader.RequireDouble(arguments, "x"),
                McpArgumentReader.RequireDouble(arguments, "y"),
                ct).ConfigureAwait(false),
            "browser_scroll" => await _browser.ScrollAsync(
                McpArgumentReader.RequireDouble(arguments, "deltaX"),
                McpArgumentReader.RequireDouble(arguments, "deltaY"),
                ct).ConfigureAwait(false),
            "browser_scroll_to" => await _browser.ScrollToAsync(
                McpArgumentReader.ReadOptionalDouble(arguments, "x"),
                McpArgumentReader.ReadOptionalDouble(arguments, "y"),
                McpArgumentReader.ReadOptionalString(arguments, "selector"),
                McpArgumentReader.ReadOptionalString(arguments, "position"),
                ct).ConfigureAwait(false),
            "browser_press_key" => await _browser.PressKeyAsync(
                McpArgumentReader.RequireString(arguments, "key"),
                McpArgumentReader.ReadBool(arguments, "shift"),
                McpArgumentReader.ReadBool(arguments, "ctrl"),
                McpArgumentReader.ReadBool(arguments, "alt"),
                McpArgumentReader.ReadBool(arguments, "meta"),
                ct).ConfigureAwait(false),
            "browser_type" => await _browser.TypeTextAsync(
                McpArgumentReader.RequireString(arguments, "text"),
                McpArgumentReader.ReadBool(arguments, "submit"),
                ct).ConfigureAwait(false),
            "browser_resize" => await _browser.ResizeAsync(
                McpArgumentReader.RequireDouble(arguments, "width"),
                McpArgumentReader.RequireDouble(arguments, "height"),
                ct).ConfigureAwait(false),
            "browser_wait" => await _browser.WaitAsync(
                McpArgumentReader.RequireString(arguments, "state"),
                McpArgumentReader.ReadOptionalString(arguments, "value"),
                McpArgumentReader.ReadIntOr(arguments, "timeoutMs", 5000),
                ct).ConfigureAwait(false),
            "browser_query" => await _browser.QueryAsync(
                McpArgumentReader.RequireString(arguments, "selector"),
                McpArgumentReader.ReadBool(arguments, "includeText"),
                McpArgumentReader.ReadBool(arguments, "includeHtml"),
                McpArgumentReader.ReadIntOr(arguments, "limit", 20),
                ct).ConfigureAwait(false),
            "browser_highlight" => await _browser.HighlightAsync(
                McpArgumentReader.RequireString(arguments, "selector"),
                McpArgumentReader.ReadOptionalString(arguments, "color"),
                ct).ConfigureAwait(false),
            "browser_select" => await _browser.SelectElementAsync(
                McpArgumentReader.RequireString(arguments, "selector"), ct).ConfigureAwait(false),
            "browser_focus" => await _browser.FocusElementAsync(
                McpArgumentReader.RequireString(arguments, "selector"), ct).ConfigureAwait(false),
            "browser_find" => await _browser.FindAsync(
                McpArgumentReader.RequireString(arguments, "query"),
                McpArgumentReader.ReadOptionalString(arguments, "direction") ?? "next",
                ct).ConfigureAwait(false),
            "browser_clipboard" => await _browser.ClipboardAsync(
                McpArgumentReader.RequireString(arguments, "action"),
                McpArgumentReader.ReadOptionalString(arguments, "text"),
                ct).ConfigureAwait(false),
            "browser_bookmarks" => await _browser.BookmarksAsync(
                McpArgumentReader.ReadOptionalString(arguments, "id"),
                ct).ConfigureAwait(false),
            "browser_computed_style" => await _browser.ComputedStyleAsync(
                McpArgumentReader.RequireString(arguments, "selector"), ct).ConfigureAwait(false),
            _ => throw new ArgumentException($"Unknown browser tool: {name}", nameof(name)),
        };

        return new McpToolResult(ResultObject(result), IsError: !result.Ok);
    }

    private static JsonObject ResultObject(BrowserControlResult result) => new()
    {
        ["ok"] = result.Ok,
        ["url"] = result.Url,
        ["title"] = result.Title,
        ["canGoBack"] = result.CanGoBack,
        ["canGoForward"] = result.CanGoForward,
        ["isBusy"] = result.IsBusy,
        ["error"] = result.Error,
        ["detail"] = result.Detail,
    };

    // The tool catalogue is fully static; the descriptors live as a JSON
    // literal so the MCP server can splice them into
    // tools/list and re-parse a fresh tree per request. A JsonNode cannot be
    // re-parented, so a pre-built tree could not be reused.
    private const string ToolDescriptorsJson = """
        [
          {
            "name": "browser_navigate",
            "description": "Navigate the visible Starling browser window to a URL.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "url": {
                  "type": "string",
                  "description": "The absolute URL to load, for example https://example.com."
                }
              },
              "required": ["url"]
            }
          },
          {
            "name": "browser_back",
            "description": "Navigate the visible Starling browser window back in history.",
            "inputSchema": { "type": "object", "properties": {} }
          },
          {
            "name": "browser_forward",
            "description": "Navigate the visible Starling browser window forward in history.",
            "inputSchema": { "type": "object", "properties": {} }
          },
          {
            "name": "browser_refresh",
            "description": "Reload the current page in the visible Starling browser window.",
            "inputSchema": { "type": "object", "properties": {} }
          },
          {
            "name": "browser_screenshot",
            "description": "Capture the current page in the visible Starling browser window to a PNG file (full scroll extent). The written path is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Output PNG path. Relative paths resolve against the GUI's working directory. Defaults to starling-screenshot.png."
                }
              }
            }
          },
          {
            "name": "browser_screenshot_viewport",
            "description": "Capture the currently visible viewport to a PNG file. The written path is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "path": {
                  "type": "string",
                  "description": "Output PNG path. Relative paths resolve against the GUI working directory. Defaults to starling-viewport.png."
                }
              }
            }
          },
          {
            "name": "browser_inspect",
            "description": "Inspect the current page: URL, title, live-scripting state, and recent JS console warnings/errors, returned in `detail`. Optionally include the serialized outerHTML and/or dump a full telemetry+HTML report to a logfile.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "includeHtml": {
                  "type": "boolean",
                  "description": "Include the page's serialized outerHTML in the response (truncated to 100 KB)."
                },
                "logPath": {
                  "type": "string",
                  "description": "If set, write a full report (all telemetry logs + complete outerHTML) to this file path."
                }
              }
            }
          },
          {
            "name": "browser_console",
            "description": "Return recent JavaScript console and script error log entries from the visible browser.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "minLevel": { "type": "string", "description": "Minimum log level: Trace, Debug, Information, Warning, Error, or Critical. Defaults to Trace." },
                "limit": { "type": "integer", "description": "Maximum number of entries to return. Defaults to 100, capped at 500." }
              }
            }
          },
          {
            "name": "browser_network",
            "description": "Return recent network-related spans and log entries from the visible browser.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "limit": { "type": "integer", "description": "Maximum number of entries to return. Defaults to 100, capped at 500." }
              }
            }
          },
          {
            "name": "browser_click",
            "description": "Left-click a point on the current page. Coordinates are page pixels from the document's top-left (same space browser_screenshot captures, full scroll extent). Clicking a text field focuses it (follow with browser_type); a link/button/checkbox is activated. The outcome is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "x": { "type": "number", "description": "X coordinate in page pixels from the document's left edge." },
                "y": { "type": "number", "description": "Y coordinate in page pixels from the document's top edge." }
              },
              "required": ["x", "y"]
            }
          },
          {
            "name": "browser_click_selector",
            "description": "Left-click the center of the first element matching a CSS selector.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "selector": { "type": "string", "description": "A CSS selector. The first rendered match is clicked." }
              },
              "required": ["selector"]
            }
          },
          {
            "name": "browser_move",
            "description": "Move the mouse to a point on the current page, updating hover/cursor state and dispatching DOM mouseover/mousemove/mouseout so JS hover handlers run. Coordinates are page pixels from the document's top-left (same space as browser_screenshot). What is under the cursor is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "x": { "type": "number", "description": "X coordinate in page pixels from the document's left edge." },
                "y": { "type": "number", "description": "Y coordinate in page pixels from the document's top edge." }
              },
              "required": ["x", "y"]
            }
          },
          {
            "name": "browser_scroll",
            "description": "Scroll the visible Starling browser viewport by page-pixel deltas. Positive deltaY scrolls down. Positive deltaX scrolls right. The new offset is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "deltaX": { "type": "number", "description": "Horizontal scroll delta in page pixels. Positive values scroll right." },
                "deltaY": { "type": "number", "description": "Vertical scroll delta in page pixels. Positive values scroll down." }
              },
              "required": ["deltaX", "deltaY"]
            }
          },
          {
            "name": "browser_scroll_to",
            "description": "Scroll the visible Starling browser viewport to an absolute page offset or to an element.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "x": { "type": "number", "description": "Absolute horizontal page offset in page pixels." },
                "y": { "type": "number", "description": "Absolute vertical page offset in page pixels." },
                "selector": { "type": "string", "description": "Optional CSS selector. If set, scrolls to the first rendered matching element." },
                "position": { "type": "string", "description": "For selector scrolls: top, center, or bottom. Defaults to top." }
              }
            }
          },
          {
            "name": "browser_press_key",
            "description": "Press a browser key such as Tab, Enter, Escape, Backspace, Delete, ArrowLeft, PageDown, Home, or End. Use modifier booleans for combinations.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "key": { "type": "string", "description": "Key name, for example Tab, Enter, Escape, ArrowDown, PageDown, or Backspace." },
                "shift": { "type": "boolean", "description": "Hold Shift while pressing the key." },
                "ctrl": { "type": "boolean", "description": "Hold Control while pressing the key." },
                "alt": { "type": "boolean", "description": "Hold Alt while pressing the key." },
                "meta": { "type": "boolean", "description": "Hold Command/Meta while pressing the key." }
              },
              "required": ["key"]
            }
          },
          {
            "name": "browser_type",
            "description": "Type text into the currently focused text field (focus one first with browser_click). Fires a DOM input event so search-as-you-type and form handlers run. Set submit=true to press Enter afterward, submitting the owning form. The field's new value is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "text": { "type": "string", "description": "The literal text to type. Control characters are ignored." },
                "submit": { "type": "boolean", "description": "Press Enter after typing to submit the owning form. Defaults to false." }
              },
              "required": ["text"]
            }
          },
          {
            "name": "browser_resize",
            "description": "Resize the visible Starling browser window. Width and height are device-independent pixels (DIPs). The window's min size is honored — smaller requests clamp up. If the window is maximized/fullscreen it is restored first. The page reflows to the new viewport; the applied size is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "width": { "type": "number", "description": "Window width in DIPs. Clamped to the window's MinWidth." },
                "height": { "type": "number", "description": "Window height in DIPs. Clamped to the window's MinHeight." }
              },
              "required": ["width", "height"]
            }
          },
          {
            "name": "browser_wait",
            "description": "Wait until the browser reaches a state: load, idle, page, selector, text, or url.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "state": { "type": "string", "description": "One of load, idle, page, selector, text, or url." },
                "value": { "type": "string", "description": "Required for selector, text, and url waits." },
                "timeoutMs": { "type": "integer", "description": "Maximum wait time in milliseconds. Defaults to 5000." }
              },
              "required": ["state"]
            }
          },
          {
            "name": "browser_query",
            "description": "Return matched elements for a CSS selector, including bounds and optional text or HTML.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "selector": { "type": "string", "description": "A CSS selector." },
                "includeText": { "type": "boolean", "description": "Include each element's text content." },
                "includeHtml": { "type": "boolean", "description": "Include each element's serialized HTML." },
                "limit": { "type": "integer", "description": "Maximum number of matches to return. Defaults to 20, capped at 100." }
              },
              "required": ["selector"]
            }
          },
          {
            "name": "browser_highlight",
            "description": "Draw a translucent highlight box over every element matching a CSS selector on the current page (non-destructive; clears the previous highlight and resets on navigation). Use it to point out elements visually. The matched count is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "selector": { "type": "string", "description": "A CSS selector, for example \"a.nav\", \"#main h2\", or \"button[type=submit]\"." },
                "color": { "type": "string", "description": "Optional CSS colour for the highlight (for example \"red\", \"#ff8800\", or \"rgba(0,128,255,.4)\"). An opaque colour is dimmed to translucent. Defaults to translucent yellow." }
              },
              "required": ["selector"]
            }
          },
          {
            "name": "browser_select",
            "description": "Select the first element matching a CSS selector: draws a selection box over it and makes its text the active selection (copyable). The selected element and character count are returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "selector": { "type": "string", "description": "A CSS selector. The first element in document order that matches is selected." }
              },
              "required": ["selector"]
            }
          },
          {
            "name": "browser_focus",
            "description": "Focus the first element matching a CSS selector. A text field gets keyboard focus + caret (follow with browser_type); any other element gets DOM focus so :focus styling and JS focus handlers run. The focused element is returned in `detail`.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "selector": { "type": "string", "description": "A CSS selector. The first element in document order that matches is focused." }
              },
              "required": ["selector"]
            }
          },
          {
            "name": "browser_find",
            "description": "Find text on the current page, flash the match, and scroll it into view.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Text to find on the page." },
                "direction": { "type": "string", "description": "next or previous. Defaults to next." }
              },
              "required": ["query"]
            }
          },
          {
            "name": "browser_clipboard",
            "description": "Copy selected text, paste text into the focused field, read the clipboard, or read the current page selection.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "action": { "type": "string", "description": "copy, paste, read, or readSelection." },
                "text": { "type": "string", "description": "Text to paste. If omitted, paste reads from the system clipboard." }
              },
              "required": ["action"]
            }
          },
          {
            "name": "browser_bookmarks",
            "description": "List sidebar bookmarks, or navigate to one by id.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "id": { "type": "string", "description": "Optional bookmark id to open. Omit to list bookmarks." }
              }
            }
          },
          {
            "name": "browser_computed_style",
            "description": "Report the EFFECTIVE painted style for every element matching a CSS selector (up to 10): opacity, transform, color, background, plus hoverOverride and animating flags. Uses the same precedence the painter does (live hover override, else animation/transition sample, else laid-out style). Use it to debug why content renders wrong or goes invisible — for example to catch a hover override unexpectedly shadowing an animation.",
            "inputSchema": {
              "type": "object",
              "properties": {
                "selector": { "type": "string", "description": "A CSS selector. Every matching element's effective style is reported, in document order." }
              },
              "required": ["selector"]
            }
          }
        ]
        """;
}
