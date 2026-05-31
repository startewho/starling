using System.Diagnostics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using Starling.Common;
using Starling.Css.Cascade;
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

    private const double ChromeHeightCss = 44;

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

    /// <summary>
    /// Lays out a minimal toolbar HTML document at <paramref name="logicalW"/> ×
    /// <see cref="ChromeHeightCss"/> CSS px and returns the root <see cref="BlockBox"/>.
    /// Uses the layout engine directly (no <see cref="BrowserSession"/>), the same
    /// pattern as <see cref="NativePresentDemo"/>.
    /// </summary>
    private static BlockBox BuildChromeBox(float logicalW, string url, bool focused)
    {
        // Show a text caret at the end while the bar is focused. Appended before
        // escaping so it can never be read as markup.
        var shown = focused ? url + "│" : url;

        // HTML-escape the URL for safe inline display.
        var safeUrl = shown
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

        // Focused: lighter field + a blue focus ring, matching the Avalonia bar.
        var field = focused
            ? "background:#454545;border:1px solid #5b9dd9"
            : "background:#3d3d3d;border:1px solid #3d3d3d";

        var html =
            $"<body style=\"margin:0;padding:0;background:#2b2b2b;width:{logicalW}px;height:{ChromeHeightCss}px;display:flex;align-items:center\">" +
            $"<div style=\"flex:1;margin:6px 12px;padding:6px 11px;" +
            $"{field};border-radius:6px;font-family:sans-serif;" +
            $"font-size:13px;color:#e0e0e0;white-space:nowrap;overflow:hidden;" +
            $"text-overflow:ellipsis\">{safeUrl}</div>" +
            "</body>";

        return new Starling.Layout.LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
            .LayoutDocument(HtmlParser.Parse(html),
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
        using var session  = new BrowserSession();

        // macOS accessibility bridge (phase 4) — null off macOS / no content view.
        var a11y = MacAccessibilityBridge.TryCreate(window.Native?.Cocoa ?? 0);

        // ── State ────────────────────────────────────────────────────────────
        float         logicalW   = window.FramebufferSize.X / dpr;
        float         logicalH   = window.FramebufferSize.Y / dpr;
        double        scrollY    = 0;
        LaidOutPage?  page       = null;
        int           lastLayoutVersion = -1;
        var           clock      = Stopwatch.StartNew();
        var           lastFb     = window.FramebufferSize;
        int           presented  = 0;
        int           failures   = 0;

        // Text-input state
        Element? focusedInput = null;

        // URL-bar edit state. When focused, keystrokes edit urlBarText instead of
        // the page and Enter navigates. Click the chrome strip or press Cmd/Ctrl+L
        // to focus; Esc or navigating clears it.
        bool   urlBarFocused = false;
        string urlBarText    = "";

        // Chrome state — rebuilt only when the shown text, focus, or width changes.
        BlockBox? chromeBox     = null;
        string    chromeUrl     = "";
        bool      chromeFocused = false;
        float     chromeWidth   = 0f;

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

        // The native loop drives Update + Render every iteration. needsPresent
        // gates the actual swapchain present so a settled page doesn't re-blend
        // every frame (NS-04). Set true whenever something visible changes; the
        // --frames smoke-test mode always presents so it can reach its count.
        bool needsPresent = true;

        // ── Load the initial page (blocking — safe: we're not on a GPU thread yet) ──
        // Page viewport is the window minus the chrome strip at the top.
        var options = new RenderOptions(new Size((int)logicalW, (int)(logicalH - ChromeHeightCss)));
        var result  = session.NavigateInteractiveAsync(startUrl, options)
                             .GetAwaiter().GetResult();

        if (result.IsErr)
        {
            Console.Error.WriteLine($"browser: navigation failed: {result.Error.Message}");
            return 1;
        }
        page = result.Value;
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
                if (page is null || button != MouseButton.Left) return;

                var pos = m.Position;

                // Click is over the chrome strip — focus + select the URL bar.
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

        // GLFW window handle for the native clipboard (phase 4).
        var glfwHandle = window.Native?.Glfw ?? 0;

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

                // URL bar takes text first when it owns focus.
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

                // Global history chords: Cmd/Ctrl+[ back, +] forward, +R reload,
                // and Alt+Left / Alt+Right back / forward. Handled before URL-bar
                // editing so they work no matter what has focus.
                if (CmdOrCtrl() && key == Key.LeftBracket)  { GoBack();    return; }
                if (CmdOrCtrl() && key == Key.RightBracket) { GoForward(); return; }
                if (CmdOrCtrl() && key == Key.R)            { Reload();    return; }
                if (Alt() && key == Key.Left)               { GoBack();    return; }
                if (Alt() && key == Key.Right)              { GoForward(); return; }

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

        // ── Window events ─────────────────────────────────────────────────────
        window.FramebufferResize += sz =>
        {
            presenter.Configure(Math.Max(1, sz.X), Math.Max(1, sz.Y));
        };

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
            // the new time so a hover transition progresses, and present.
            if (session.HasActiveAnimations(page))
            {
                session.PrepareAnimationFrame(page, clockMs);
                animClockMs = clockMs;
                if (hoverElement is not null)
                    hoverOverrides = BuildHoverOverrides(hoverElement, clockMs);
                needsPresent = true;
            }
        };

        window.Render += _ =>
        {
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

            // Nothing visible changed — skip the present and let the last frame
            // stand. The --frames smoke test always presents so it can reach its
            // target count.
            if (_maxFrames == 0 && !needsPresent) return;

            // Build (or reuse) the chrome BlockBox. While the URL bar is focused it
            // shows the edit buffer; otherwise the loaded page URL.
            var shownUrl = urlBarFocused ? urlBarText : (page.Url ?? "");
            if (chromeBox is null || shownUrl != chromeUrl
                || urlBarFocused != chromeFocused || logicalW != chromeWidth)
            {
                chromeBox     = BuildChromeBox(logicalW, shownUrl, urlBarFocused);
                chromeUrl     = shownUrl;
                chromeFocused = urlBarFocused;
                chromeWidth   = logicalW;
            }

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
                styleOverride:   StyleOverride,
                images:          page.ImageResolver);

            if (ok) { presented++; needsPresent = false; } else failures++;

            if (_maxFrames > 0 && presented >= _maxFrames)
                window.Close();
        };

        window.Run();

        page?.Dispose();
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
            needsPresent = true;
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
            urlBarFocused  = false;
            hoverElement   = null;
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
                if (n is Element p) result[p] = style.ComputeWithAnimations(p, nowMs, ctx);
            return result;

            void Recurse(Element el)
            {
                result[el] = style.ComputeWithAnimations(el, nowMs, ctx);
                for (var child = el.FirstChild; child is not null; child = child.NextSibling)
                    if (child is Element c) Recurse(c);
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

    // ── Per-frame layer-promotion predicate (mirrors WebviewPanel) ────────────

    private static bool IsAnimatingLayerRoot(LaidOutPage page, Starling.Layout.Box.Box box)
    {
        if (box.Element is not { } el) return false;
        if (page.Document.WasRecentlyMutated(el)) return true;
        foreach (var _ in page.Style.AnimationEngine.ActiveProperties(el)) return true;
        foreach (var _ in page.Style.TransitionEngine.ActiveProperties(el)) return true;
        return false;
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
