using System.Diagnostics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using SixLabors.ImageSharp;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Engine;
using Starling.Gui;
using Starling.Html;
using Starling.Layout.Box;
using Starling.Layout.Text;
using LayoutSize = Starling.Layout.Size;
using Starling.Paint.Compositor;
using Starling.Gui.Core.Rendering;

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

    public NativeBrowserWindow(int maxFrames = 0)
    {
        _maxFrames = maxFrames;
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

        try
        {
            return RunWindow(page1Url);
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
    private static BlockBox BuildChromeBox(float logicalW, string url)
    {
        // HTML-escape the URL for safe inline display.
        var safeUrl = url
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

        var html =
            $"<body style=\"margin:0;padding:0;background:#2b2b2b;width:{logicalW}px;height:{ChromeHeightCss}px;display:flex;align-items:center\">" +
            $"<div style=\"flex:1;margin:6px 12px;padding:6px 12px;" +
            $"background:#3d3d3d;border-radius:6px;font-family:sans-serif;" +
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

        // Chrome state — rebuilt only when url or logicalW changes.
        BlockBox? chromeBox    = null;
        string    chromeUrl    = "";
        float     chromeWidth  = 0f;

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

        // ── Input ─────────────────────────────────────────────────────────────
        var input = window.CreateInput();

        if (input.Mice.Count > 0)
        {
            var mouse = input.Mice[0];

            mouse.Scroll += (_, wheel) =>
            {
                if (page is null) return;
                var maxScroll = Math.Max(0, page.DocumentHeight - (logicalH - ChromeHeightCss));
                scrollY = Math.Clamp(scrollY - wheel.Y * 40, 0, maxScroll);
            };

            mouse.MouseMove += (m, pos) =>
            {
                if (page is null) return;

                // Pointer is over the chrome strip — use default cursor, skip page hit-test.
                if (pos.Y < ChromeHeightCss)
                {
                    SetCursor(m.Cursor, "default");
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
            };

            mouse.MouseDown += (m, button) =>
            {
                if (page is null || button != MouseButton.Left) return;

                var pos = m.Position;

                // Click is over the chrome strip — do nothing for now.
                if (pos.Y < ChromeHeightCss)
                    return;

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
                            var oldPage = page;
                            page = null;
                            renderer.ResetForNavigation();
                            scrollY = 0;
                            focusedInput = null;

                            var navOpts = new RenderOptions(new Size((int)logicalW, (int)(logicalH - ChromeHeightCss)));
                            var navResult = session.NavigateInteractiveAsync(resolved, navOpts)
                                                   .GetAwaiter().GetResult();

                            oldPage.Dispose();

                            if (navResult.IsOk)
                            {
                                page = navResult.Value;
                                lastLayoutVersion = page.Document.LayoutInvalidationVersion;
                                Console.WriteLine($"browser: navigated to {page.Url}");
                            }
                            else
                            {
                                Console.Error.WriteLine($"browser: nav failed: {navResult.Error.Message}");
                            }
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

            keyboard.KeyChar += (_, c) =>
            {
                if (page is null || focusedInput is null) return;
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

            keyboard.KeyDown += (_, key, _) =>
            {
                if (page is null || focusedInput is null) return;

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

            // Animation frame
            if (session.HasActiveAnimations(page))
                session.PrepareAnimationFrame(page, clockMs);
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

            // Build (or reuse) the chrome BlockBox.
            var currentUrl = page.Url ?? "";
            if (chromeBox is null || currentUrl != chromeUrl || logicalW != chromeWidth)
            {
                chromeBox   = BuildChromeBox(logicalW, currentUrl);
                chromeUrl   = currentUrl;
                chromeWidth = logicalW;
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
                images:          page.ImageResolver);

            if (ok) presented++; else failures++;

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
