using System.Diagnostics;
using System.Text;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using Starling.Common;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Selectors;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Engine;
using Starling.Gui;
using Starling.Html;
using Starling.Layout.Box;
using Starling.Layout.Text;
using LayoutSize = Starling.Layout.Size;
using Starling.Paint.Compositor;
using Starling.Gui.Core.Accessibility;
using Starling.Gui.Core.Rendering;
using Starling.Shell.Native.Mac;

namespace Starling.Shell.Native;

/// <summary>
/// Phase-3 native browser window: real navigation, scroll, hover-cursor,
/// click, and keyboard-text wired to the Starling engine via
/// <see cref="BrowserSession"/>, presented zero-copy through
/// <see cref="NativeViewportRenderer"/> with engine-rendered chrome above
/// the page in a single composited frame.
/// </summary>
internal sealed class NativeBrowserWindow : IDisposable
{
    // ── Chrome ───────────────────────────────────────────────────────────────

    // Chrome is two stacked rows: a tab strip on top, the URL bar below. The page
    // viewport is the window minus the whole chrome, so ChromeHeightCss stays the
    // single offset every hit-test / present uses.
    private const double TabStripHeightCss = 32;
    private const double UrlBarHeightCss   = 44;
    private const double ChromeHeightCss   = TabStripHeightCss + UrlBarHeightCss;
    private const double NewTabBtnW        = 28;

    // Context menu geometry (one shared definition for render + click hit-test).
    private const double MenuItemH = 26;
    private const double MenuW     = 200;

    // ── Demo HTML written to temp files ─────────────────────────────────────

    private const string Page1Html = """
        <!DOCTYPE html>
        <html>
        <head><title>Starling Browser – Page 1</title></head>
        <body style="margin:0;font-family:sans-serif;background:#f0f4ff;padding:24px">
          <h1 style="color:#1a1a6e;font-size:36px">Starling Native Browser</h1>
          <p style="font-size:18px">Phase 3: real interaction wired to the Starling engine.</p>
          <p style="font-size:16px"><a href="PAGE2URL" style="color:#2244cc">Go to page 2 &rarr;</a></p>
          <label for="q" style="font-size:16px">Search: </label>
          <input id="q" type="text" placeholder="type here"
                 style="font-size:16px;padding:4px 8px;border:1px solid #aac;border-radius:4px;width:220px"/>
          <div style="height:8px"></div>
          <p style="font-size:14px;color:#555">Scroll down to see more content.</p>
          <div style="height:1200px;background:linear-gradient(to bottom,#e8efff,#c0cfee);
               padding:16px;font-size:14px;color:#333">
            <p>Tall scrollable section. Scroll down with the mouse wheel.</p>
            <p style="margin-top:600px">Bottom of the tall section.</p>
          </div>
        </body>
        </html>
        """;

    private const string Page2Html = """
        <!DOCTYPE html>
        <html>
        <head><title>Starling – Page 2</title></head>
        <body style="margin:0;font-family:sans-serif;background:#fff0e8;padding:24px">
          <h1 style="color:#6e1a00;font-size:36px">Page 2</h1>
          <p style="font-size:18px">You navigated here by clicking the link.</p>
          <p style="font-size:16px"><a href="PAGE1URL" style="color:#cc2200">&larr; Back to page 1</a></p>
          <p style="font-size:14px;color:#555">Page 2 has no tall content.</p>
        </body>
        </html>
        """;

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly int _maxFrames;
    private readonly string? _startUrl;

    public NativeBrowserWindow(int maxFrames = 0, string? startUrl = null)
    {
        _maxFrames = maxFrames;
        _startUrl  = startUrl;
    }

    public void Dispose() { }

    // ── Entry point ──────────────────────────────────────────────────────────

    public int Run()
    {
        // Write the two demo pages to temp files so the engine can load them
        // as file:// URLs.
        var page1Path = Path.Combine(Path.GetTempPath(), $"starling_p1_{Environment.ProcessId}.html");
        var page2Path = Path.Combine(Path.GetTempPath(), $"starling_p2_{Environment.ProcessId}.html");
        var page1Url  = "file://" + page1Path.Replace('\\', '/');
        var page2Url  = "file://" + page2Path.Replace('\\', '/');

        File.WriteAllText(page1Path, Page1Html.Replace("PAGE2URL", page2Url));
        File.WriteAllText(page2Path, Page2Html.Replace("PAGE1URL", page1Url));

        // A --url argument opens that address at launch (normalized like the URL
        // bar, so `--url example.com` becomes https://example.com). Otherwise the
        // built-in demo page loads.
        var startUrl = _startUrl is { Length: > 0 }
            ? UrlBarInputNormalizer.Normalize(_startUrl) ?? _startUrl
            : page1Url;

        try
        {
            return RunWindow(startUrl);
        }
        finally
        {
            try { File.Delete(page1Path); } catch { /* best-effort */ }
            try { File.Delete(page2Path); } catch { /* best-effort */ }
        }
    }

    // ── Chrome layout ────────────────────────────────────────────────────────

    private static string EscapeHtml(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Width of one tab in the strip: the available width (minus the new-tab
    /// button) split evenly across the open tabs, clamped to a sane range. Both
    /// the renderer and the click hit-test use this so they never drift.
    /// </summary>
    private static double TabWidth(float logicalW, int tabCount) =>
        Math.Clamp((logicalW - NewTabBtnW) / Math.Max(1, tabCount), 60, 220);

    /// <summary>
    /// Lays out the chrome as one HTML document — a tab strip stacked over the URL
    /// bar — at <paramref name="logicalW"/> × <see cref="ChromeHeightCss"/> CSS px.
    /// Uses the layout engine directly, the same pattern as the page demo.
    /// </summary>
    private static BlockBox BuildChrome(
        float logicalW, List<string> tabLabels, int activeIndex,
        double tabWidth, string url, bool urlFocused)
    {
        var multi = tabLabels.Count > 1;
        var sb = new StringBuilder();
        sb.Append($"<body style=\"margin:0;padding:0;background:#1f1f1f;width:{logicalW}px;" +
                  $"height:{ChromeHeightCss}px;font-family:sans-serif\">");

        // Tab strip.
        sb.Append($"<div style=\"display:flex;height:{TabStripHeightCss}px;align-items:stretch\">");
        for (var i = 0; i < tabLabels.Count; i++)
        {
            var bg = i == activeIndex ? "#3d3d3d" : "#2b2b2b";
            var fg = i == activeIndex ? "#ffffff" : "#bbbbbb";
            sb.Append($"<div style=\"width:{tabWidth:F0}px;background:{bg};color:{fg};font-size:12px;" +
                      "padding:0 8px;display:flex;align-items:center;border-right:1px solid #1f1f1f;" +
                      "white-space:nowrap;overflow:hidden\">");
            sb.Append($"<span style=\"flex:1;overflow:hidden;text-overflow:ellipsis\">{EscapeHtml(tabLabels[i])}</span>");
            if (multi) sb.Append("<span style=\"margin-left:6px;color:#888\">&#215;</span>");
            sb.Append("</div>");
        }
        sb.Append($"<div style=\"width:{NewTabBtnW:F0}px;color:#bbb;font-size:18px;" +
                  "display:flex;align-items:center;justify-content:center\">+</div>");
        sb.Append("</div>");

        // URL bar. A caret is appended (before escaping) while it is focused.
        var shown = urlFocused ? url + "│" : url;
        var field = urlFocused
            ? "background:#454545;border:1px solid #5b9dd9"
            : "background:#3d3d3d;border:1px solid #3d3d3d";
        sb.Append($"<div style=\"height:{UrlBarHeightCss}px;background:#2b2b2b;display:flex;align-items:center\">");
        sb.Append($"<div style=\"flex:1;margin:6px 12px;padding:6px 11px;{field};border-radius:6px;" +
                  "font-size:13px;color:#e0e0e0;white-space:nowrap;overflow:hidden;text-overflow:ellipsis\">" +
                  $"{EscapeHtml(shown)}</div>");
        sb.Append("</div></body>");

        return new Starling.Layout.LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
            .LayoutDocument(HtmlParser.Parse(sb.ToString()),
                new LayoutSize(logicalW, ChromeHeightCss));
    }

    // ── Window loop ──────────────────────────────────────────────────────────

    private int RunWindow(string startUrl)
    {
        Silk.NET.Windowing.Glfw.GlfwWindowing.Use();
        Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();

        var opts = WindowOptions.Default with
        {
            Title = "Starling (browser)",
            Size = new Vector2D<int>(900, 700),
            API = GraphicsAPI.None,
            VSync = false,
            ShouldSwapAutomatically = false,
        };

        using var window   = Window.Create(opts);
        window.Initialize();

        var dpr = window.Size.X > 0 ? (float)window.FramebufferSize.X / window.Size.X : 1f;
        Console.WriteLine($"browser: fb={window.FramebufferSize} logical={window.Size} dpr={dpr}");

        var presenter = GpuSurfacePresenter.CreateForWindow(window);
        if (presenter is null)
        {
            Console.Error.WriteLine("browser: no GPU adapter / surface; cannot create presenter.");
            return 1;
        }
        using var _p = presenter;
        using var renderer = new NativeViewportRenderer();

        // macOS accessibility bridge (phase 4) — null off macOS / no content view.
        var a11y = MacAccessibilityBridge.TryCreate(window.Native?.Cocoa ?? 0);

        // GLFW window handle for the native clipboard (phase 4). Declared up here
        // so the menu/clipboard local functions can capture it definitely-assigned.
        var glfwHandle = window.Native?.Glfw ?? 0;

        // ── State ────────────────────────────────────────────────────────────
        float         logicalW   = window.FramebufferSize.X / dpr;
        float         logicalH   = window.FramebufferSize.Y / dpr;
        var           clock      = Stopwatch.StartNew();
        var           lastFb     = window.FramebufferSize;
        int           presented  = 0;
        int           failures   = 0;

        // Tabs. Each Tab owns its session + view state; the live locals below
        // mirror the active tab and are swapped in/out by SwitchTab so every input
        // handler can keep using `page` / `session` / `scrollY` unchanged.
        var          tabs        = new List<Tab>();
        int          activeIndex = 0;
        BrowserSession session    = null!;   // assigned from the first tab below
        double        scrollY     = 0;
        LaidOutPage?  page        = null;
        int           lastLayoutVersion = -1;

        // Text-input state
        Element? focusedInput = null;

        // URL-bar edit state. When focused, keystrokes edit urlBarText instead of
        // the page and Enter navigates. Click the chrome strip or press Cmd/Ctrl+L
        // to focus; Esc or navigating clears it.
        bool   urlBarFocused = false;
        string urlBarText    = "";

        // Chrome state — rebuilt only when its signature (tabs / active index /
        // labels / URL text / focus / width) changes.
        BlockBox? chromeBox = null;
        string    chromeSig = "";

        // Hover + animation styling (NS-04). hoverElement is the innermost element
        // under the pointer; hoverOverrides maps each affected element to its
        // :hover computed style; hoverScope tracks which elements the current hover
        // touches so the next change can register reverse transitions. animClockMs
        // is the shared animation/transition clock, read by the styleOverride at
        // paint time. Mirrors WebviewPanel.
        Element?                            hoverElement   = null;
        Dictionary<Element, ComputedStyle>? hoverOverrides = null;
        HashSet<Element>                    hoverScope     = new();
        long                                animClockMs    = 0;

        // Find-in-page (NS-03). Cmd/Ctrl+F opens; keystrokes edit findQuery; Enter
        // and Shift+Enter step matches; Esc closes. The current match is drawn by
        // an overlay document composited over the page in document space.
        bool      findActive     = false;
        string    findQuery      = "";
        List<BoxHitTester.PlacedFragment>? findFragments = null;
        int       findCursor     = -1;  // index of the current match fragment, -1 none
        int       findMatchTotal = 0;
        BlockBox? findOverlay    = null;

        // Context menu (NS-03). Right-click opens a menu of actions whose items
        // depend on what is under the pointer (a link adds Open/Copy Link). Drawn
        // as a screen-fixed overlay; the next click runs an item or dismisses.
        bool      menuActive = false;
        double    menuX = 0, menuY = 0;
        var       menuItems  = new List<(string Label, Action Run)>();
        BlockBox? menuOverlay = null;

        // IME preedit (NS-01). On macOS with STARLING_IME_PREEDIT=1 the MacImeBridge
        // feeds the active composition string here; it is drawn underlined at the
        // focused field. Committed text still arrives via the GLFW char callback.
        string preedit = "";

        // Devtools (NS-03): F12 toggles a read-only DOM-tree inspector panel on the
        // right, an engine-rendered screen-fixed overlay rebuilt when the DOM or
        // window size changes.
        bool      devtoolsActive  = false;
        BlockBox? devtoolsOverlay = null;

        // "Something visible changed this frame" signal, set wherever state that
        // affects the picture changes. It does NOT gate the present today — the
        // loop presents every frame so the macOS surface stays flushed (gating it
        // left the window gray). It is kept up to date for a future damage-based
        // present that skips the re-raster while still flushing the surface.
        bool needsPresent = true;

        // ── Load the initial page (blocking — safe: we're not on a GPU thread yet) ──
        // Page viewport is the window minus the chrome strip at the top.
        var firstSession = new BrowserSession();
        var options = new RenderOptions(new Size((int)logicalW, (int)(logicalH - ChromeHeightCss)));
        var result  = firstSession.NavigateInteractiveAsync(startUrl, options)
                                  .GetAwaiter().GetResult();

        if (result.IsErr)
        {
            Console.Error.WriteLine($"browser: navigation failed: {result.Error.Message}");
            firstSession.Dispose();
            return 1;
        }
        tabs.Add(new Tab(firstSession)
        {
            Page = result.Value,
            LastLayoutVersion = result.Value.Document.LayoutInvalidationVersion,
        });
        activeIndex       = 0;
        session           = firstSession;
        page              = result.Value;
        lastLayoutVersion = page.Document.LayoutInvalidationVersion;
        Console.WriteLine($"browser: loaded {page.Url}  height={page.DocumentHeight:F0}px");

        // Push the accessibility tree to the OS after a (re)layout or navigation.
        void PushA11y()
        {
            if (a11y is null || page is null) return;
            var tree = AccessibilityTreeBuilder.Build(page.Root, page.Document);
            a11y.Update(tree, ChromeHeightCss, scrollY, logicalH);
        }

        PushA11y();

        // ── Input ─────────────────────────────────────────────────────────────
        var input = window.CreateInput();

        if (input.Mice.Count > 0)
        {
            var mouse = input.Mice[0];

            mouse.Scroll += (_, wheel) =>
            {
                if (page is null) return;
                var maxScroll = Math.Max(0, page.DocumentHeight - (logicalH - ChromeHeightCss));
                var newScroll = Math.Clamp(scrollY - wheel.Y * 40, 0, maxScroll);
                if (newScroll != scrollY) { scrollY = newScroll; needsPresent = true; }
            };

            mouse.MouseMove += (m, pos) =>
            {
                if (page is null) return;

                // Pointer is over the chrome strip — use default cursor, drop any
                // page hover, skip the page hit-test.
                if (pos.Y < ChromeHeightCss)
                {
                    SetCursor(m.Cursor, "default");
                    UpdateHover(null);
                    return;
                }

                var pageX = pos.X;
                var pageY = (pos.Y - ChromeHeightCss) + scrollY;

                var hit = BoxHitTester.HitTest(
                    page.Root,
                    pageX,
                    pageY,
                    viewportX: 0,
                    viewportY: scrollY,
                    scrollOffsets: null);
                var cursor = BoxHitTester.ResolveCursor(hit);
                SetCursor(m.Cursor, cursor);

                // Drive CSS :hover from the innermost element under the pointer.
                Element? hoverEl = null;
                for (var b = hit.Box; b is not null; b = b.Parent)
                    if (b.Element is { } e) { hoverEl = e; break; }
                UpdateHover(hoverEl);
            };

            mouse.MouseDown += (m, button) =>
            {
                if (page is null) return;

                var pos = m.Position;

                // An open context menu eats the next click: run the item under it,
                // else dismiss.
                if (menuActive)
                {
                    var n = menuItems.Count;
                    if (button == MouseButton.Left
                        && pos.X >= menuX && pos.X <= menuX + MenuW
                        && pos.Y >= menuY && pos.Y < menuY + n * MenuItemH)
                    {
                        var idx = (int)((pos.Y - menuY) / MenuItemH);
                        CloseMenu();
                        if (idx >= 0 && idx < n) menuItems[idx].Run();
                    }
                    else
                    {
                        CloseMenu();
                    }
                    return;
                }

                // Right-click opens the context menu.
                if (button == MouseButton.Right)
                {
                    OpenMenuAt(pos.X, pos.Y);
                    return;
                }

                if (button != MouseButton.Left) return;

                // Click is over the tab strip — switch / close / new tab.
                if (pos.Y < TabStripHeightCss)
                {
                    var n = tabs.Count;
                    var tw = TabWidth(logicalW, n);
                    var x = pos.X;
                    if (x >= n * tw && x < n * tw + NewTabBtnW) { NewTab(startUrl); return; }
                    var idx = (int)(x / tw);
                    if (idx >= 0 && idx < n)
                    {
                        // Right ~18px of a tab is its close affordance when >1 tab.
                        if (n > 1 && x - idx * tw > tw - 18) CloseTab(idx);
                        else SwitchTab(idx);
                    }
                    return;
                }

                // Click is over the URL bar — focus + select it.
                if (pos.Y < ChromeHeightCss)
                {
                    if (focusedInput is not null)
                    {
                        page.Scripting?.DispatchEvent(focusedInput,
                            new FocusEvent("blur", new EventInit(Bubbles: false)));
                        focusedInput = null;
                        page.Document.FocusedElement = null;
                    }
                    urlBarFocused = true;
                    urlBarText    = page.Url ?? "";
                    needsPresent  = true;
                    return;
                }

                // Click landed in the page — drop URL-bar focus.
                if (urlBarFocused) { urlBarFocused = false; needsPresent = true; }

                var pageX = pos.X;
                var pageY = (pos.Y - ChromeHeightCss) + scrollY;

                var hit = BoxHitTester.HitTest(
                    page.Root,
                    pageX, pageY,
                    viewportX: 0,
                    viewportY: scrollY,
                    scrollOffsets: null);

                // Focus / text input
                if (hit.Box?.Element is { } hitEl
                    && HtmlFormControls.IsTextControl(hitEl))
                {
                    focusedInput = hitEl;
                    page.Document.FocusedElement = hitEl;
                    page.Scripting?.DispatchEvent(hitEl,
                        new FocusEvent("focus", new EventInit(Bubbles: false)));
                    return;
                }

                // Blur any previously focused input
                if (focusedInput is not null)
                {
                    page.Scripting?.DispatchEvent(focusedInput,
                        new FocusEvent("blur", new EventInit(Bubbles: false)));
                    focusedInput = null;
                    page.Document.FocusedElement = null;
                }

                // Link navigation
                if (hit.LinkAnchor is { } anchor)
                {
                    var href = anchor.GetAttribute("href");
                    if (!string.IsNullOrEmpty(href))
                    {
                        var resolved = LinkResolver.Resolve(href, page.Url);
                        if (resolved is not null)
                        {
                            Navigate(resolved);
                            return;
                        }
                    }
                }

                // Generic click event
                if (hit.Box?.Element is { } clickTarget && page.Scripting is { } scripting)
                {
                    scripting.DispatchEvent(clickTarget, new MouseEvent("click",
                        new EventInit(Bubbles: true, Cancelable: true))
                    {
                        ClientX = pageX,
                        ClientY = pageY,
                        Button = 0,
                    });
                }
            };
        }

        if (input.Keyboards.Count > 0)
        {
            var keyboard = input.Keyboards[0];

            // Cmd (macOS) or Ctrl (elsewhere) held — the clipboard chord modifier.
            bool CmdOrCtrl() =>
                keyboard.IsKeyPressed(Key.SuperLeft) || keyboard.IsKeyPressed(Key.SuperRight)
                || keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);

            keyboard.KeyChar += (_, c) =>
            {
                if (page is null) return;
                if (CmdOrCtrl()) return; // a clipboard/shortcut chord, not text

                // Find-in-page takes text first when open.
                if (findActive)
                {
                    findQuery += c;
                    findCursor = -1;
                    RunFind(1);
                    return;
                }

                // URL bar takes text next when it owns focus.
                if (urlBarFocused)
                {
                    urlBarText += c;
                    needsPresent = true;
                    return;
                }

                if (focusedInput is null) return;
                var current = HtmlFormControls.Value(focusedInput);
                HtmlFormControls.SetValue(focusedInput, current + c);
                page.Scripting?.DispatchEvent(focusedInput,
                    new InputEvent("input", new EventInit(Bubbles: true))
                    {
                        Data = c.ToString(),
                        InputType = "insertText",
                    });
                RefreshLayout();
            };

            bool Alt() =>
                keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);

            keyboard.KeyDown += (_, key, _) =>
            {
                if (page is null) return;

                // An open context menu swallows Escape (dismiss).
                if (menuActive)
                {
                    if (key == Key.Escape) CloseMenu();
                    return;
                }

                // F12 toggles the devtools DOM inspector.
                if (key == Key.F12)
                {
                    devtoolsActive  = !devtoolsActive;
                    devtoolsOverlay = null;
                    needsPresent    = true;
                    return;
                }

                // Global history chords: Cmd/Ctrl+[ back, +] forward, +R reload,
                // and Alt+Left / Alt+Right back / forward. Handled before URL-bar
                // editing so they work no matter what has focus.
                if (CmdOrCtrl() && key == Key.LeftBracket)  { GoBack();    return; }
                if (CmdOrCtrl() && key == Key.RightBracket) { GoForward(); return; }
                if (CmdOrCtrl() && key == Key.R)            { Reload();    return; }
                if (Alt() && key == Key.Left)               { GoBack();    return; }
                if (Alt() && key == Key.Right)              { GoForward(); return; }

                // Cmd/Ctrl+N opens a new browser window (a fresh process).
                if (CmdOrCtrl() && key == Key.N) { LaunchNewWindow(); return; }

                // Tab chords: Cmd/Ctrl+T new, +W close, +1..9 select, +Tab next.
                if (CmdOrCtrl() && key == Key.T)   { NewTab(startUrl);           return; }
                if (CmdOrCtrl() && key == Key.W)   { CloseTab(activeIndex);      return; }
                if (CmdOrCtrl() && key == Key.Tab) { SwitchTab((activeIndex + 1) % tabs.Count); return; }
                if (CmdOrCtrl() && key >= Key.Number1 && key <= Key.Number9)
                {
                    SwitchTab((int)(key - Key.Number1));
                    return;
                }

                // Cmd/Ctrl+F — open find-in-page.
                if (CmdOrCtrl() && key == Key.F) { OpenFind(); return; }

                // Find-in-page editing owns the keyboard while it is open.
                if (findActive)
                {
                    needsPresent = true;
                    if (CmdOrCtrl() && key == Key.V)
                    {
                        var clip = NativeClipboard.Get(glfwHandle);
                        if (!string.IsNullOrEmpty(clip)) { findQuery += clip; findCursor = -1; RunFind(1); }
                        return;
                    }
                    if (CmdOrCtrl()) return;
                    switch (key)
                    {
                        case Key.Backspace:
                            if (findQuery.Length > 0) { findQuery = findQuery[..^1]; findCursor = -1; RunFind(1); }
                            break;
                        case Key.Escape:
                            CloseFind();
                            break;
                        case Key.Enter:
                        case Key.KeypadEnter:
                            var back = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
                            RunFind(back ? -1 : 1);
                            break;
                    }
                    return;
                }

                // Cmd/Ctrl+L — focus + select the URL bar (standard browser chord).
                if (CmdOrCtrl() && key == Key.L)
                {
                    if (focusedInput is not null)
                    {
                        page.Scripting?.DispatchEvent(focusedInput,
                            new FocusEvent("blur", new EventInit(Bubbles: false)));
                        focusedInput = null;
                        page.Document.FocusedElement = null;
                    }
                    urlBarFocused = true;
                    urlBarText    = page.Url ?? "";
                    needsPresent  = true;
                    return;
                }

                // URL-bar editing owns the keyboard while it has focus.
                if (urlBarFocused)
                {
                    needsPresent = true;
                    if (CmdOrCtrl())
                    {
                        if (key == Key.V)
                        {
                            var clip = NativeClipboard.Get(glfwHandle);
                            if (!string.IsNullOrEmpty(clip)) urlBarText += clip;
                        }
                        else if (key is Key.C or Key.X)
                        {
                            NativeClipboard.Set(glfwHandle, urlBarText);
                            if (key == Key.X) urlBarText = "";
                        }
                        else if (key == Key.A)
                        {
                            // Select-all is implicit (we have no caret model yet);
                            // the next typed char replaces. Treat as a no-op clear.
                        }
                        return;
                    }

                    switch (key)
                    {
                        case Key.Backspace:
                            if (urlBarText.Length > 0) urlBarText = urlBarText[..^1];
                            break;
                        case Key.Escape:
                            urlBarFocused = false;
                            break;
                        case Key.Enter:
                        case Key.KeypadEnter:
                            var target = UrlBarInputNormalizer.Normalize(urlBarText);
                            urlBarFocused = false;
                            if (target is not null) Navigate(target);
                            else Console.Error.WriteLine($"browser: '{urlBarText}' is not a URL");
                            break;
                    }
                    return;
                }

                if (focusedInput is null) return;

                // Clipboard chords (phase 4): copy / cut / paste on the focused input.
                if (CmdOrCtrl() && key is Key.C or Key.X or Key.V)
                {
                    var val = HtmlFormControls.Value(focusedInput);
                    if (key == Key.C)
                    {
                        NativeClipboard.Set(glfwHandle, val);
                    }
                    else if (key == Key.X)
                    {
                        NativeClipboard.Set(glfwHandle, val);
                        HtmlFormControls.SetValue(focusedInput, "");
                        page.Scripting?.DispatchEvent(focusedInput,
                            new InputEvent("input", new EventInit(Bubbles: true)) { InputType = "deleteByCut" });
                        RefreshLayout();
                    }
                    else // V
                    {
                        var clip = NativeClipboard.Get(glfwHandle);
                        if (!string.IsNullOrEmpty(clip))
                        {
                            HtmlFormControls.SetValue(focusedInput, val + clip);
                            page.Scripting?.DispatchEvent(focusedInput,
                                new InputEvent("input", new EventInit(Bubbles: true)) { Data = clip, InputType = "insertFromPaste" });
                            RefreshLayout();
                        }
                    }
                    return;
                }

                if (key == Key.Backspace)
                {
                    var v = HtmlFormControls.Value(focusedInput);
                    if (v.Length > 0)
                    {
                        HtmlFormControls.SetValue(focusedInput, v[..^1]);
                        page.Scripting?.DispatchEvent(focusedInput,
                            new InputEvent("input", new EventInit(Bubbles: true))
                            {
                                InputType = "deleteContentBackward",
                            });
                        RefreshLayout();
                    }
                }
                else if (key is Key.Enter or Key.KeypadEnter)
                {
                    // Best-effort blur + change
                    page.Scripting?.DispatchEvent(focusedInput,
                        new Event("change", new EventInit(Bubbles: true)));
                    page.Scripting?.DispatchEvent(focusedInput,
                        new FocusEvent("blur", new EventInit(Bubbles: false)));
                    focusedInput = null;
                    page.Document.FocusedElement = null;
                }
            };
        }

        // IME preedit driver (NS-01, experimental, opt-in). Off by default — the
        // commit-style path already works; this adds the inline composing display.
        if (Environment.GetEnvironmentVariable("STARLING_IME_PREEDIT") == "1")
            MacImeBridge.Install(
                s  => { preedit = s;  needsPresent = true; },
                () => { preedit = ""; needsPresent = true; });

        // ── Window events ─────────────────────────────────────────────────────
        window.FramebufferResize += sz =>
        {
            presenter.Configure(Math.Max(1, sz.X), Math.Max(1, sz.Y));
        };

        // Animation pacing. Cap continuous animation/transition frames to a target
        // rate so a fast page does not spin the present at hundreds of fps.
        // STARLING_REDUCE_MOTION freezes animation entirely — heavy pages (a tall
        // page with a page-wide animation) then render once and stay responsive
        // instead of re-rasterizing every frame.
        var          reduceMotion      = Environment.GetEnvironmentVariable("STARLING_REDUCE_MOTION") == "1";
        long         lastAnimMs        = long.MinValue;
        const double animFrameBudgetMs = 1000.0 / 60; // 60 fps

        window.Update += _ =>
        {
            if (page is null) return;

            page.Document.DecayRecentMutations();

            var clockMs = clock.ElapsedMilliseconds;
            page.Scripting?.PumpFrame(clockMs);

            // Relayout if DOM changed in a layout-relevant way
            var lv = page.Document.LayoutInvalidationVersion;
            if (lv != lastLayoutVersion)
            {
                lastLayoutVersion = lv;
                RefreshLayout();
            }

            // Animation frame: advance the clock, re-sample the hovered scope at
            // the new time so a hover transition progresses, and present. Paced to
            // the frame budget; reduced-motion skips it entirely.
            if (!reduceMotion
                && clockMs - lastAnimMs >= animFrameBudgetMs
                && session.HasActiveAnimations(page))
            {
                lastAnimMs = clockMs;
                session.PrepareAnimationFrame(page, clockMs);
                animClockMs = clockMs;
                if (hoverElement is not null)
                    hoverOverrides = BuildHoverOverrides(hoverElement, clockMs);
                needsPresent = true;
            }
        };

        window.Render += _ =>
        {
            var frameStart = clock.ElapsedMilliseconds;

            var fb = window.FramebufferSize;
            if (fb.X <= 0 || fb.Y <= 0) return;

            if (fb != lastFb)
            {
                dpr = window.Size.X > 0 ? (float)fb.X / window.Size.X : 1f;
                logicalW = fb.X / dpr;
                logicalH = fb.Y / dpr;
                lastFb = fb;
                RefreshLayout();
            }

            if (page is null) return;

            // Present every frame. The present is what keeps the on-screen surface
            // live — skipping it (an earlier "only present when changed"
            // optimization) left the macOS swapchain unflushed and the whole window
            // gray. The loop is instead paced by a short sleep AFTER the present
            // (below), once the frame is already on screen, so the CPU stays bounded
            // without ever starving the display.

            // Build (or reuse) the chrome BlockBox. The URL-bar row shows the find
            // query while find is open, else the edit buffer when focused, else the
            // active page URL.
            string shownUrl;
            bool   urlFocusVisual;
            if (findActive)
            {
                var status = findQuery.Length == 0
                    ? ""
                    : findMatchTotal == 0
                        ? "  (no matches)"
                        : $"  ({findMatchTotal} match{(findMatchTotal == 1 ? "" : "es")})";
                shownUrl = $"Find: {findQuery}{status}";
                urlFocusVisual = true;
            }
            else
            {
                shownUrl = urlBarFocused ? urlBarText : (page.Url ?? "");
                urlFocusVisual = urlBarFocused;
            }

            var labels = new List<string>(tabs.Count);
            for (var i = 0; i < tabs.Count; i++) labels.Add(LabelOf(i));
            var sig = $"{activeIndex}|{string.Join((char)1, labels)}|{shownUrl}|{urlFocusVisual}|{logicalW}";
            if (chromeBox is null || sig != chromeSig)
            {
                chromeBox = BuildChrome(
                    logicalW, labels, activeIndex,
                    TabWidth(logicalW, tabs.Count), shownUrl, urlFocusVisual);
                chromeSig = sig;
            }

            // Screen-fixed overlay: a context menu wins over the devtools panel.
            if (devtoolsActive && devtoolsOverlay is null) devtoolsOverlay = BuildDevtoolsOverlay();
            var screenOverlay = menuActive ? menuOverlay
                              : devtoolsActive ? devtoolsOverlay
                              : null;

            var ok = renderer.PresentComposited(
                presenter,
                surfaceWidth:    fb.X,
                surfaceHeight:   fb.Y,
                scale:           dpr,
                chromeRoot:      chromeBox,
                chromeHeightCss: ChromeHeightCss,
                pageRoot:        page.Root,
                scrollX:         0,
                scrollY:         scrollY,
                pageAnimating:   box => IsAnimatingLayerRoot(page, box),
                styleOverride:     StyleOverride,
                images:            page.ImageResolver,
                overlayRoot:       findActive ? findOverlay : (preedit.Length > 0 ? BuildPreeditOverlay() : null),
                screenOverlayRoot: screenOverlay);

            if (ok) { presented++; needsPresent = false; } else failures++;

            if (_maxFrames > 0 && presented >= _maxFrames)
                window.Close();

            // Pace to ~60 Hz AFTER presenting, so a cheap frame doesn't spin a CPU
            // core. The frame is already on screen, so this never starves the
            // display. A heavy frame that already took longer than the budget just
            // doesn't sleep. Skipped in --frames smoke mode.
            if (_maxFrames == 0)
            {
                const long frameBudgetMs = 16;
                var spent = clock.ElapsedMilliseconds - frameStart;
                if (spent < frameBudgetMs)
                    System.Threading.Thread.Sleep((int)(frameBudgetMs - spent));
            }
        };

        window.Run();

        SaveActive();
        foreach (var t in tabs) t.Dispose();
        Console.WriteLine(
            $"BROWSER OK: {presented} frames presented zero-copy ({failures} surface-reconfig frames)");
        return presented > 0 ? 0 : 1;

        // ── Local helpers ────────────────────────────────────────────────────
        void RefreshLayout()
        {
            if (page is null) return;
            var reOpts = new RenderOptions(new Size((int)logicalW, (int)(logicalH - ChromeHeightCss)));
            var successor = session.RelayoutCurrent(page, reOpts);
            page.Dispose();
            page = successor;
            lastLayoutVersion = page.Document.LayoutInvalidationVersion;
            // Fragment geometry changed — drop the stale find index and highlight,
            // and the devtools panel (size / DOM may have changed).
            findFragments   = null;
            findOverlay     = null;
            devtoolsOverlay = null;
            needsPresent    = true;
            PushA11y();
        }

        // The page viewport is the window minus the chrome strip at the top.
        RenderOptions NavOpts() =>
            new(new Size((int)logicalW, (int)(logicalH - ChromeHeightCss)));

        // Swap a freshly-laid-out page in on success. On failure the old page
        // stays visible (and the bar reverts to its URL) — a blank window on a
        // typo or dead link would be worse. Blocking on the window thread matches
        // the established pattern; async navigation is NS-03 follow-up.
        void ApplyNav(Result<LaidOutPage, RenderError> navResult)
        {
            if (page is null) return;
            if (navResult.IsErr)
            {
                Console.Error.WriteLine($"browser: nav failed: {navResult.Error.Message}");
                return;
            }

            var oldPage = page;
            renderer.ResetForNavigation();
            scrollY        = 0;
            focusedInput   = null;
            preedit        = "";
            urlBarFocused  = false;
            findActive      = false;
            findOverlay     = null;
            findFragments   = null;
            devtoolsOverlay = null;
            menuActive      = false;
            menuOverlay     = null;
            hoverElement    = null;
            hoverOverrides = null;
            hoverScope.Clear();
            page           = navResult.Value;
            oldPage.Dispose();
            lastLayoutVersion = page.Document.LayoutInvalidationVersion;
            needsPresent = true;
            Console.WriteLine($"browser: navigated to {page.Url}");
            PushA11y();
        }

        void Navigate(string url) =>
            ApplyNav(session.NavigateInteractiveAsync(url, NavOpts()).GetAwaiter().GetResult());

        void GoBack()
        {
            if (session.History.CanGoBack)
                ApplyNav(session.BackInteractiveAsync(NavOpts()).GetAwaiter().GetResult());
        }

        void GoForward()
        {
            if (session.History.CanGoForward)
                ApplyNav(session.ForwardInteractiveAsync(NavOpts()).GetAwaiter().GetResult());
        }

        void Reload() =>
            ApplyNav(session.ReloadInteractiveAsync(NavOpts()).GetAwaiter().GetResult());

        // ── Tabs ───────────────────────────────────────────────────────────────

        // Copy the live view state back into the active Tab record.
        void SaveActive()
        {
            if (tabs.Count == 0) return;
            var t = tabs[activeIndex];
            t.Page              = page;
            t.ScrollY           = scrollY;
            t.FocusedInput      = focusedInput;
            t.HoverElement      = hoverElement;
            t.HoverOverrides    = hoverOverrides;
            t.HoverScope        = hoverScope;
            t.LastLayoutVersion = lastLayoutVersion;
        }

        // Load the active Tab record into the live locals. Layer caches key on the
        // document, so a tab switch resets them. URL-bar focus does not survive a
        // switch.
        void LoadActive()
        {
            var t = tabs[activeIndex];
            session           = t.Session;
            page              = t.Page;
            scrollY           = t.ScrollY;
            focusedInput      = t.FocusedInput;
            hoverElement      = t.HoverElement;
            hoverOverrides    = t.HoverOverrides;
            hoverScope        = t.HoverScope;
            lastLayoutVersion = t.LastLayoutVersion;
            urlBarFocused     = false;
            preedit           = "";
            findActive        = false;
            findOverlay       = null;
            findFragments     = null;
            devtoolsOverlay   = null;
            menuActive        = false;
            menuOverlay       = null;
            renderer.ResetForNavigation();
            needsPresent      = true;
            PushA11y();
        }

        void SwitchTab(int i)
        {
            if (i < 0 || i >= tabs.Count || i == activeIndex) return;
            SaveActive();
            activeIndex = i;
            LoadActive();
        }

        void NewTab(string url)
        {
            SaveActive();
            var s = new BrowserSession();
            var r = s.NavigateInteractiveAsync(url, NavOpts()).GetAwaiter().GetResult();
            if (r.IsErr)
            {
                Console.Error.WriteLine($"browser: new-tab nav failed: {r.Error.Message}");
                s.Dispose();
                return;
            }
            tabs.Add(new Tab(s)
            {
                Page = r.Value,
                LastLayoutVersion = r.Value.Document.LayoutInvalidationVersion,
            });
            activeIndex = tabs.Count - 1;
            LoadActive();
        }

        void CloseTab(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            if (tabs.Count == 1) { window.Close(); return; } // closing the last tab closes the window

            var closingActive = i == activeIndex;
            if (!closingActive) SaveActive(); // keep the still-active tab's live state

            var t = tabs[i];
            tabs.RemoveAt(i);
            t.Dispose();

            if (i < activeIndex) activeIndex--;
            else if (activeIndex >= tabs.Count) activeIndex = tabs.Count - 1;
            LoadActive();
        }

        // The active tab's label tracks the live page; others use their saved page.
        string LabelOf(int i)
        {
            var p = i == activeIndex ? page : tabs[i].Page;
            if (!string.IsNullOrWhiteSpace(p?.Title)) return p!.Title!;
            var u = p?.Url;
            return string.IsNullOrEmpty(u) ? "New Tab" : u;
        }

        // ── Find-in-page ─────────────────────────────────────────────────────

        void OpenFind()
        {
            if (page is null) return;
            findActive    = true;
            findQuery     = "";
            findCursor    = -1;
            findMatchTotal = 0;
            findOverlay   = null;
            findFragments = BoxHitTester.CollectFragments(page.Root);
            urlBarFocused = false;
            needsPresent  = true;
        }

        void CloseFind()
        {
            findActive    = false;
            findOverlay   = null;
            findFragments = null;
            needsPresent  = true;
        }

        // A semi-transparent highlight box at the matched fragment, in document
        // space, composited over the page (so it scrolls with the page).
        BlockBox BuildFindOverlay(BoxHitTester.PlacedFragment f, float contentW, double docH)
        {
            var html =
                $"<body style=\"margin:0;padding:0;position:relative;width:{contentW}px;height:{docH}px\">" +
                $"<div style=\"position:absolute;left:{f.X}px;top:{f.Y}px;" +
                $"width:{f.Width}px;height:{f.Height}px;background:rgba(255,213,0,0.45)\"></div></body>";
            return new Starling.Layout.LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
                .LayoutDocument(HtmlParser.Parse(html), new LayoutSize(contentW, (float)Math.Max(1, docH)));
        }

        // Step to the next (dir +1) or previous (dir -1) fragment whose text
        // contains the query, scroll it into view, and build its highlight.
        void RunFind(int dir)
        {
            findOverlay = null;
            if (page is null || findQuery.Length == 0) { findMatchTotal = 0; needsPresent = true; return; }
            findFragments ??= BoxHitTester.CollectFragments(page.Root);
            var frags = findFragments;
            var n = frags.Count;

            findMatchTotal = 0;
            foreach (var fr in frags)
                if (fr.Text.Contains(findQuery, StringComparison.OrdinalIgnoreCase)) findMatchTotal++;

            if (n == 0 || findMatchTotal == 0) { findCursor = -1; needsPresent = true; return; }

            var start = findCursor + dir;
            for (var step = 0; step < n; step++)
            {
                var idx = ((start + dir * step) % n + n) % n;
                if (!frags[idx].Text.Contains(findQuery, StringComparison.OrdinalIgnoreCase)) continue;

                findCursor = idx;
                var f = frags[idx];
                var viewportH = logicalH - ChromeHeightCss;
                var maxScroll = Math.Max(0, page.DocumentHeight - viewportH);
                scrollY = Math.Clamp(f.Y - viewportH / 3, 0, maxScroll);
                findOverlay = BuildFindOverlay(f, logicalW, page.DocumentHeight);
                break;
            }
            needsPresent = true;
        }

        // Underlined preedit drawn at the focused field's position, in document
        // space so it scrolls with the page. Approximate placement (field start);
        // glyph-accurate caret tracking is a refinement.
        BlockBox? BuildPreeditOverlay()
        {
            if (page is null || focusedInput is null || preedit.Length == 0) return null;
            if (FindAbs(page.Root, focusedInput, 0, 0) is not { } pos) return null;

            var html =
                $"<body style=\"margin:0;padding:0;position:relative;" +
                $"width:{logicalW}px;height:{page.DocumentHeight}px\">" +
                $"<div style=\"position:absolute;left:{pos.X + 4}px;top:{pos.Y + 2}px;" +
                "background:#fff3c4;color:#000;font-size:13px;text-decoration:underline;" +
                $"padding:0 2px;white-space:nowrap\">{EscapeHtml(preedit)}</div></body>";
            return new Starling.Layout.LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
                .LayoutDocument(HtmlParser.Parse(html),
                    new LayoutSize(logicalW, (float)Math.Max(1, page.DocumentHeight)));
        }

        // ── Context menu ─────────────────────────────────────────────────────

        void CloseMenu()
        {
            menuActive  = false;
            menuOverlay = null;
            needsPresent = true;
        }

        BlockBox BuildMenuOverlay()
        {
            var sb = new StringBuilder();
            sb.Append($"<body style=\"margin:0;padding:0;position:relative;" +
                      $"width:{logicalW}px;height:{logicalH}px\">");
            sb.Append($"<div style=\"position:absolute;left:{menuX}px;top:{menuY}px;width:{MenuW}px;" +
                      "background:#2b2b2b;border:1px solid #555;border-radius:4px;" +
                      "font-family:sans-serif;font-size:13px;color:#e0e0e0;overflow:hidden\">");
            foreach (var it in menuItems)
                sb.Append($"<div style=\"height:{MenuItemH}px;padding:0 12px;" +
                          $"display:flex;align-items:center\">{EscapeHtml(it.Label)}</div>");
            sb.Append("</div></body>");
            return new Starling.Layout.LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
                .LayoutDocument(HtmlParser.Parse(sb.ToString()), new LayoutSize(logicalW, logicalH));
        }

        void OpenMenuAt(double x, double y)
        {
            if (page is null) return;
            menuItems.Clear();

            // Link under the pointer (page area only) adds link actions.
            if (y >= ChromeHeightCss)
            {
                var hit = BoxHitTester.HitTest(
                    page.Root, x, (y - ChromeHeightCss) + scrollY,
                    viewportX: 0, viewportY: scrollY, scrollOffsets: null);
                if (hit.LinkAnchor is { } a)
                {
                    var href = a.GetAttribute("href");
                    var target = string.IsNullOrEmpty(href) ? null : LinkResolver.Resolve(href, page.Url);
                    if (target is not null)
                    {
                        menuItems.Add(("Open Link", () => Navigate(target)));
                        menuItems.Add(("Copy Link Address", () => NativeClipboard.Set(glfwHandle, target)));
                    }
                }
            }

            if (session.History.CanGoBack)    menuItems.Add(("Back", GoBack));
            if (session.History.CanGoForward) menuItems.Add(("Forward", GoForward));
            menuItems.Add(("Reload", Reload));

            var n = menuItems.Count;
            menuX = Math.Clamp(x, 0, Math.Max(0, logicalW - MenuW));
            menuY = Math.Clamp(y, 0, Math.Max(0, logicalH - n * MenuItemH));
            menuOverlay  = BuildMenuOverlay();
            menuActive   = true;
            needsPresent = true;
        }

        // ── Devtools (read-only DOM inspector) ───────────────────────────────

        BlockBox BuildDevtoolsOverlay()
        {
            const double panelW = 380;
            var panelX  = Math.Max(0, logicalW - panelW);
            var panelH  = logicalH - ChromeHeightCss;
            var maxRows = Math.Max(0, (int)((panelH - 30) / 16));

            var sb = new StringBuilder();
            sb.Append($"<body style=\"margin:0;padding:0;position:relative;" +
                      $"width:{logicalW}px;height:{logicalH}px\">");
            sb.Append($"<div style=\"position:absolute;left:{panelX}px;top:{ChromeHeightCss}px;" +
                      $"width:{panelW}px;height:{panelH}px;background:#1b1b1b;border-left:1px solid #444;" +
                      "font-family:monospace;font-size:12px;color:#cfcfcf;overflow:hidden\">");
            sb.Append("<div style=\"padding:4px 10px;color:#888;border-bottom:1px solid #333\">DOM</div>");

            var rows = 0;
            void Walk(Element el, int depth)
            {
                if (rows >= maxRows) return;
                var tag = el.TagName.ToLowerInvariant();
                var label = "<" + tag;
                if (!string.IsNullOrEmpty(el.Id)) label += "#" + el.Id;
                var cls = el.GetAttribute("class");
                if (!string.IsNullOrWhiteSpace(cls))
                    label += "." + string.Join('.', cls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                label += ">";
                sb.Append($"<div style=\"padding:1px 10px 1px {10 + depth * 14}px;" +
                          $"white-space:nowrap;overflow:hidden\">{EscapeHtml(label)}</div>");
                rows++;
                for (var c = el.FirstChild; c is not null; c = c.NextSibling)
                    if (c is Element ce) Walk(ce, depth + 1);
            }
            if (page?.Document.DocumentElement is { } root) Walk(root, 0);

            sb.Append("</div></body>");
            return new Starling.Layout.LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
                .LayoutDocument(HtmlParser.Parse(sb.ToString()), new LayoutSize(logicalW, logicalH));
        }

        // ── Hover + animation styling (NS-04, mirrors WebviewPanel) ────────────

        // Re-cascade the hovered element's subtree plus its ancestor chain under a
        // :hover context, sampling any triggered transition at nowMs. Returns the
        // per-element override styles the painter overlays, or null when nothing
        // is hovered.
        Dictionary<Element, ComputedStyle>? BuildHoverOverrides(Element? hovered, long nowMs)
        {
            if (hovered is null || page is null) return null;
            var style = page.Style;
            var ctx = new SelectorMatchContext { HoveredElement = hovered };
            var result = new Dictionary<Element, ComputedStyle>();

            Recurse(hovered);
            // Hovering an element also hovers its ancestors (`.ancestor:hover`).
            for (var n = hovered.ParentNode; n is not null; n = n.ParentNode)
                if (n is Element p && Compose(p) is { } ps) result[p] = ps;

            // Only elements whose :hover cascade actually changes a paint-relevant
            // property are overridden. Without this, hovering a big container (or the
            // body while the pointer sweeps) re-cascaded and overrode its WHOLE
            // subtree on every move — shadowing each element's animated style with the
            // hover sample, so animated/just-styled content flashed to its base state
            // (invisible) until the pointer stopped. Pruning to the genuinely
            // :hover-affected elements lets everyone else keep their animated style.
            return result.Count == 0 ? null : result;

            // The override style for `el` when :hover changes its paint, else null.
            // Relevance is judged from the STATIC cascade (with vs. without :hover) so a
            // running animation's per-frame sample doesn't make every animating element
            // look "changed"; the stored style still carries the animation sample.
            ComputedStyle? Compose(Element el)
            {
                var hoverStatic = style.Compute(el, ctx);
                // Prune the element when nothing the painter emits changes under
                // :hover (SamePaint value-compares, so identical cascades match).
                if (SamePaint(style.Compute(el), hoverStatic)) return null;
                return style.ComputeWithAnimations(el, nowMs, ctx);
            }

            void Recurse(Element el)
            {
                if (Compose(el) is { } s) result[el] = s;
                for (var child = el.FirstChild; child is not null; child = child.NextSibling)
                    if (child is Element c) Recurse(c);
            }

            // True when a and b paint identically for the properties the painter
            // emits (mirrors WebviewPanel.SamePaintProperties). Lets :hover relevance
            // be judged without re-rendering on CssValue identity churn.
            static bool SamePaint(ComputedStyle a, ComputedStyle b)
            {
                if (a.GetColor(PropertyId.Color) != b.GetColor(PropertyId.Color)) return false;
                if (a.GetColor(PropertyId.BackgroundColor) != b.GetColor(PropertyId.BackgroundColor)) return false;
                if (a.GetColor(PropertyId.BorderTopColor) != b.GetColor(PropertyId.BorderTopColor)) return false;
                if (a.GetColor(PropertyId.BorderRightColor) != b.GetColor(PropertyId.BorderRightColor)) return false;
                if (a.GetColor(PropertyId.BorderBottomColor) != b.GetColor(PropertyId.BorderBottomColor)) return false;
                if (a.GetColor(PropertyId.BorderLeftColor) != b.GetColor(PropertyId.BorderLeftColor)) return false;
                // Value-compare: two independent cascade runs (with vs. without
                // :hover) produce non-identical CssKeyword instances for the same
                // keyword, so ReferenceEquals would report a spurious change and
                // pull every element into the override set (the invisibility bug).
                if (!Equals(a.Get(PropertyId.TextDecoration), b.Get(PropertyId.TextDecoration))) return false;
                if (!Equals(a.Get(PropertyId.Transform), b.Get(PropertyId.Transform))) return false;
                if (!Equals(a.Get(PropertyId.Opacity), b.Get(PropertyId.Opacity))) return false;
                return true;
            }
        }

        // Pointer moved onto a new element (or off the page). Advance the clock,
        // rebuild the overrides, and re-cascade elements leaving the hover scope so
        // their reverse transition registers.
        void UpdateHover(Element? newHover)
        {
            if (page is null || ReferenceEquals(newHover, hoverElement)) return;
            hoverElement = newHover;

            var nowMs = clock.ElapsedMilliseconds;
            session.PrepareAnimationFrame(page, nowMs); // tick before re-cascade
            animClockMs = nowMs;

            var newOverrides = BuildHoverOverrides(hoverElement, nowMs);
            var newScope = newOverrides is null ? null : new HashSet<Element>(newOverrides.Keys);

            if (hoverScope.Count > 0)
            {
                var newCtx = hoverElement is null
                    ? null
                    : new SelectorMatchContext { HoveredElement = hoverElement };
                foreach (var el in hoverScope)
                {
                    if (newScope is not null && newScope.Contains(el)) continue;
                    page.Style.ComputeWithAnimations(el, nowMs, newCtx);
                }
            }

            hoverScope     = newScope ?? new HashSet<Element>();
            hoverOverrides = newOverrides;
            needsPresent   = true;
        }

        // Per-box style the painter overlays: the :hover override if any, else the
        // sampled animation/transition style, else null (use the box's own style).
        ComputedStyle? StyleOverride(Box box)
        {
            if (hoverOverrides is not null && ResolveOverride(box, hoverOverrides) is { } ov)
                return ov;
            if (page is null || box.Element is not { } el) return null;
            foreach (var _ in page.Style.AnimationEngine.ActiveProperties(el))
                return page.Style.ComputeWithAnimations(el, animClockMs);
            foreach (var _ in page.Style.TransitionEngine.ActiveProperties(el))
                return page.Style.ComputeWithAnimations(el, animClockMs);
            return null;
        }

        // Look up a box's hover override; text/anonymous boxes inherit from the
        // nearest ancestor box whose element is in the map.
        static ComputedStyle? ResolveOverride(Box box, Dictionary<Element, ComputedStyle> overrides)
        {
            if (box.Element is { } el && overrides.TryGetValue(el, out var direct))
                return direct;
            for (var p = box.Parent; p is not null; p = p.Parent)
                if (p.Element is { } pel && overrides.TryGetValue(pel, out var inherited))
                    return inherited;
            return null;
        }
    }

    // ── Tab ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// One browser tab: its own <see cref="BrowserSession"/> (independent history
    /// + connection pool) plus the per-tab view state the window mirrors into its
    /// live locals while the tab is active.
    /// </summary>
    private sealed class Tab : IDisposable
    {
        public Tab(BrowserSession session) => Session = session;

        public BrowserSession Session { get; }
        public LaidOutPage?   Page;
        public double         ScrollY;
        public Element?       FocusedInput;
        public Element?       HoverElement;
        public Dictionary<Element, ComputedStyle>? HoverOverrides;
        public HashSet<Element> HoverScope = new();
        public int            LastLayoutVersion = -1;

        public void Dispose()
        {
            Page?.Dispose();
            Session.Dispose();
        }
    }

    // ── Per-frame layer-promotion predicate (mirrors WebviewPanel) ────────────

    /// <summary>Finds an element's box and its absolute document position by
    /// accumulating parent-relative frames. Used to place the IME preedit at the
    /// focused field.</summary>
    private static (double X, double Y)? FindAbs(Starling.Layout.Box.Box box, Element el, double ox, double oy)
    {
        var x = ox + box.Frame.X;
        var y = oy + box.Frame.Y;
        if (ReferenceEquals(box.Element, el)) return (x, y);
        foreach (var child in box.Children)
            if (FindAbs(child, el, x, y) is { } found) return found;
        return null;
    }

    private static bool IsAnimatingLayerRoot(LaidOutPage page, Starling.Layout.Box.Box box)
    {
        if (box.Element is not { } el) return false;
        if (page.Document.WasRecentlyMutated(el)) return true;
        foreach (var _ in page.Style.AnimationEngine.ActiveProperties(el)) return true;
        foreach (var _ in page.Style.TransitionEngine.ActiveProperties(el)) return true;
        return false;
    }

    // ── Multi-window (process per window) ─────────────────────────────────────

    /// <summary>
    /// Opens another browser window by relaunching this shell as a fresh process.
    /// A new OS process owns its own GLFW window and main thread, which sidesteps
    /// macOS's requirement that windowing stay on the main thread (per-thread
    /// windows are not an option there). Each window is independent — its own tabs,
    /// history, and session.
    /// </summary>
    private static void LaunchNewWindow()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;

            var psi = new System.Diagnostics.ProcessStartInfo { FileName = exe, UseShellExecute = false };

            // Under `dotnet run`/`dotnet App.dll`, ProcessPath is the dotnet host —
            // re-pass the managed entry dll so the child runs this app, not the host.
            var argv0 = Environment.GetCommandLineArgs() is { Length: > 0 } a ? a[0] : "";
            if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                && argv0.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                psi.ArgumentList.Add(argv0);

            psi.ArgumentList.Add("--browser");
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"browser: new window failed: {ex.Message}");
        }
    }

    // ── Cursor mapping ────────────────────────────────────────────────────────

    private static void SetCursor(ICursor cursor, string cssKeyword)
    {
        var sc = cssKeyword switch
        {
            "pointer"      => StandardCursor.Hand,
            "text"         => StandardCursor.IBeam,
            "crosshair"    => StandardCursor.Crosshair,
            "wait"         => StandardCursor.Wait,
            "not-allowed"  => StandardCursor.NotAllowed,
            "ew-resize"    => StandardCursor.HResize,
            "ns-resize"    => StandardCursor.VResize,
            "nwse-resize"  => StandardCursor.NwseResize,
            "nesw-resize"  => StandardCursor.NeswResize,
            "move"         => StandardCursor.ResizeAll,
            _              => StandardCursor.Default,
        };
        try
        {
            cursor.StandardCursor = sc;
        }
        catch
        {
            // Cursor API is best-effort; suppress if the backend doesn't support it.
        }
    }
}
