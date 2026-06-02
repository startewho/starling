using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using System.Text;
using Starling.Gui.Chrome;
using Starling.Gui.Core.Rendering;
using Starling.Gui.Theme;
using Starling.Paint.Backend;
using System.Diagnostics;
using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Selectors;
using Starling.Dom;
using Starling.Dom.Events;
using Starling.Engine;
using Starling.Gui.Diagnostics;
using Starling.Html;
using AvColor = Avalonia.Media.Color;
using DomDocument = Starling.Dom.Document;
using DomElement = Starling.Dom.Element;
using DomNode = Starling.Dom.Node;
using EngineSize = SixLabors.ImageSharp.Size;
using LayoutBox = Starling.Layout.Box.Box;

namespace Starling.Gui.Controls;

/// <summary>
/// The page surface. A ScrollViewer wraps a Canvas that hosts the page
/// bitmap plus absolute-positioned overlays for hover highlight, drag-select,
/// and find-match flash. Interaction (hover, link activation, drag-select,
/// and Command-F) is re-derived from the laid-out box tree via
/// <see cref="BoxHitTester"/>.
/// </summary>
internal sealed class WebviewPanel : UserControl, IDisposable
{
    private readonly ThemeManager _tm;
    private readonly IDiagnostics _diag;
    private readonly Action<string> _onLinkActivated;
    private readonly Action<string, bool> _onStatus;
    private readonly IRenderSession _renderSession;

    // Given the current page and a new viewport size, reflows the page (reusing
    // its document/resources, no network) and returns the successor — or null
    // to skip (e.g. a navigation is in flight). Supplied by the host so the
    // panel can react to its own resize without owning navigation policy.
    private readonly Func<LaidOutPage, EngineSize, LaidOutPage?>? _relayout;

    // Coalesces the burst of viewport changes during a drag-resize into a
    // single re-layout once the size settles.
    private readonly DispatcherTimer _relayoutTimer;

    private readonly ScrollViewer _scroll;
    private readonly Image _pageImage;
    private readonly Canvas _pageCanvas;

    // Zero-copy present path: when the selected render session supports a GPU
    // surface, the page is composited straight onto a swapchain embedded in the page
    // region. The bitmap image path is used only when the session has no surface.
    private SurfaceFrameTarget? _surfaceTarget;
    private readonly PageSurfaceHost? _pageSurfaceHost;

    private bool _useSurface;

    // Set while a batch of overlay/page mutations runs (notably ShowPage) so the
    // surface path coalesces them into ONE present at the end instead of firing a
    // full layer-tree present per overlay change. Without it a single relayout tick
    // fired ~4 redundant full presents (one per Clear*/RenderCaret) of the same frame.
    private bool _suppressPresent;
    private readonly TextBlock _placeholder;
    private readonly Border _placeholderHost;

    // Find bar — hidden until invoked via Cmd-F or the URL-bar find button.
    private readonly Border _findBar;
    private readonly TextBox _findEntry;

    private readonly List<Control> _selectionOverlays = new();
    private readonly List<Control> _findOverlays = new();
    private readonly List<(double Y, string Text)> _findIndex = new();
    private int _findCursor;

    private LaidOutPage? _currentPage;

    // Baseline of Document.LayoutInvalidationVersion captured when the current
    // page was last laid out. The live loop relayouts only when this advances —
    // i.e. only on mutations a built-in style/layout pass cares about — so a
    // rAF burst that writes nothing but data-* / aria-* / js* attributes no
    // longer forces a full reflow every frame. See LiveTick.
    private int _lastLayoutInvalidationVersion;
    private List<BoxHitTester.PlacedFragment> _fragments = [];

    private double _currentScale = 1.0;

    // The innermost element the pointer is currently over (or null). Drives the
    // CSS :hover re-cascade for any element — distinct from a hovered link anchor,
    // which only feeds the cursor and status-bar href.
    private DomElement? _hoverElement;

    private Dictionary<DomElement, ComputedStyle>? _hoverOverrides;

    // Elements whose computed style the current hover affects (the hovered
    // element's subtree + its ancestor chain). Tracked so the next hover change
    // can register the reverse transition for elements that leave the scope.
    private HashSet<DomElement> _hoverScope = [];

    // Per-element scroll offsets for `overflow: scroll | auto` containers
    // (the in-page sidebar nav, code blocks, etc.). The painter translates
    // each container's subtree by -offset on every frame; the wheel handler
    // mutates entries and triggers a repaint. Keyed by DomElement (stable
    // across in-place relayouts), reset when the Document changes.
    private readonly Dictionary<DomElement, (double X, double Y)> _scrollOffsets = new();
    private DomDocument? _scrollOffsetsDocument;

    private (double X, double Y)? _selectAnchor;
    private bool _selecting;
    private string _selectionText = string.Empty;

    // Text-input editing state. _focusedInput is the <input>/<textarea> element
    // currently receiving keystrokes; _caretIndex is the insertion point within
    // its value; _caretOverlay is the blinking caret drawn over the page bitmap.
    private DomElement? _focusedInput;
    private int _caretIndex;
    private Border? _caretOverlay;
    private bool _caretOn;
    private string _valueAtFocus = string.Empty;
    private readonly DispatcherTimer _caretBlinkTimer;

    // The element a synthetic mouse-move (MoveTo) last hovered, so the next move
    // can fire mouseout on it / mouseover on the new target. Only the programmatic
    // MoveTo path tracks this — real pointer moves drive CSS :hover via _hoverElement.
    private DomElement? _mouseTarget;

    // Live-page event loop: while the current page carries a live JS context
    // (Starling.Engine.PageScripting), this timer pumps its microtasks / timers /
    // rAF / fetch completions against wall-clock time and re-renders any DOM the
    // page mutates. _boundScripting tracks which context the stopwatch's clock
    // belongs to so a relayout (same context) doesn't reset it.
    private readonly DispatcherTimer _liveTimer;
    private readonly Stopwatch _liveStopwatch = new();
    private PageScripting? _boundScripting;

    // Live animation loop (declarative @keyframes/transitions + WAAPI). The host
    // supplies hooks to advance the page's animation clock and to query whether
    // any animation is in flight; the live timer then repaints each frame.
    private readonly Action<LaidOutPage, long>? _prepareAnimationFrame;
    private readonly Func<LaidOutPage, bool>? _hasActiveAnimations;
    private long _animClockMs;
    private bool _animating;

    // Frame-rate tracking. Every presented frame bumps the monotonic
    // "gui.frames.presented" counter (its rate in the Aspire dashboard is the
    // live FPS), and once a second we log the computed FPS so it can be queried
    // from structured logs. The clock is independent of _liveStopwatch, which
    // restarts whenever a new JS context binds. After an idle gap (the live
    // timer stops when nothing animates) the window resets so the first frame
    // back does not report a misleadingly low average.
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();
    private long _fpsWindowStartMs;
    private long _fpsLastFrameMs;
    private int _fpsFrames;
    private const long FpsWindowMs = 1000;
    private const long FpsIdleResetMs = 250;

    public WebviewPanel(
        ThemeManager tm,
        IDiagnostics diag,
        Action<string> onLinkActivated,
        Action<string, bool> onStatus,
        Func<LaidOutPage, EngineSize, LaidOutPage?>? relayout = null,
        Action<LaidOutPage, long>? prepareAnimationFrame = null,
        Func<LaidOutPage, bool>? hasActiveAnimations = null)
    {
        _tm = tm;
        _diag = diag;
        _renderSession = RenderSessionFactory.Create(diag);
        _onLinkActivated = onLinkActivated;
        _onStatus = onStatus;
        _relayout = relayout;
        _prepareAnimationFrame = prepareAnimationFrame;
        _hasActiveAnimations = hasActiveAnimations;
        _relayoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _relayoutTimer.Tick += (_, _) => RelayoutToViewport();

        _pageImage = new Image
        {
            // The backend renders the visible viewport region at device px and
            // BitmapBridge leaves the WriteableBitmap at 96 DPI, so its logical
            // size equals its device-pixel size. The image is given the CSS
            // (DIP) viewport Width/Height and Stretch.Uniform downscales the
            // device-pixel bitmap to fit — a crisp 1:1 device mapping on Retina.
            // (Stretch.None would draw the device-pixel bitmap at its full DIP
            // size, i.e. scale× too big on any non-1.0 RenderScaling display.)
            // It is repositioned to the scroll offset on every scroll.
            Stretch = Stretch.Uniform,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
            IsVisible = false,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_pageImage, 0);
        Canvas.SetTop(_pageImage, 0);

        _placeholder = new TextBlock
        {
            Text = "Type a URL above and press Enter to render a page.",
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        _placeholderHost = new Border
        {
            Child = _placeholder,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
            Padding = new Thickness(24),
        };

        _pageCanvas = new Canvas
        {
            // Focusable so the page surface can receive keyboard/text input once
            // an editable field is focused (links/selection only need pointers).
            Focusable = true,
        };
        _pageCanvas.Children.Add(_pageImage);
        _pageCanvas.PointerMoved += OnPointerMoved;
        _pageCanvas.PointerExited += OnPointerExited;
        _pageCanvas.PointerPressed += OnPointerPressed;
        _pageCanvas.PointerReleased += OnPointerReleased;
        _pageCanvas.PointerCaptureLost += (_, _) => EndSelectionDrag(cancel: true);
        _pageCanvas.KeyDown += OnPageKeyDown;
        _pageCanvas.KeyUp += OnPageKeyUp;
        _pageCanvas.TextInput += OnPageTextInput;
        // Wheel-handling tunnels through the ScrollViewer host: if the
        // pointer is over an `overflow: scroll | auto` subtree with room to
        // scroll in the wheel direction, consume the delta locally so the
        // outer ScrollViewer doesn't also scroll the page. Otherwise let it
        // bubble normally.
        _pageCanvas.PointerWheelChanged += OnPageWheel;

        // ~60 Hz live-page pump (started only while a page has a JS context).
        _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _liveTimer.Tick += (_, _) => LiveTick();

        // Caret blink — toggles the overlay's visibility on a fixed cadence
        // while a field is focused; stopped (and the overlay removed) on blur.
        _caretBlinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(530) };
        _caretBlinkTimer.Tick += (_, _) =>
        {
            if (_caretOverlay is null) return;
            _caretOn = !_caretOn;
            _caretOverlay.IsVisible = _caretOn;
            RefreshOverlays();
        };

        _scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _placeholderHost,
        };
        // Re-render the visible viewport region whenever the user scrolls or
        // the viewport size changes. The page is no longer rasterized whole.
        // A non-zero ViewportDelta means the visible area itself resized
        // (window resize, sidebar/DevTools toggle), so schedule a debounced
        // re-layout and let the page reflow to the new width.
        _scroll.ScrollChanged += OnScrollChanged;

        // ScrollChanged alone misses bare window resizes that don't move the
        // scroll offset — Avalonia coalesces a same-offset layout pass into a
        // zero-delta ScrollChanged (or skips it). Listening to SizeChanged on
        // the ScrollViewer catches the resize directly and triggers the same
        // debounced relayout so the page reflows to the new viewport width.
        _scroll.SizeChanged += (_, _) => ScheduleRelayout();

        _findEntry = new TextBox
        {
            PlaceholderText = "Find in page",
            FontSize = _tm.Metrics.FsSm,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
        };
        _findEntry.KeyDown += OnFindEntryKeyDown;
        _findEntry.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                _findCursor = 0;
                FindNext();
            }
        };

        var findNext = new IconButton(_tm, Icons.Enter, "Find next");
        findNext.Clicked += (_, _) => FindNext();
        var findClose = new IconButton(_tm, Icons.Close, "Close find");
        findClose.Clicked += (_, _) => HideFindBar();

        var findGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 6,
            Margin = new Thickness(12, 6),
        };
        findGrid.Children.Add(_findEntry);
        Grid.SetColumn(_findEntry, 0);
        findGrid.Children.Add(findNext);
        Grid.SetColumn(findNext, 1);
        findGrid.Children.Add(findClose);
        Grid.SetColumn(findClose, 2);

        _findBar = new Border
        {
            Child = findGrid,
            BorderThickness = new Thickness(0, 0, 0, 1),
            IsVisible = false,
        };

        var rootGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        rootGrid.Children.Add(_findBar);
        Grid.SetRow(_findBar, 0);
        rootGrid.Children.Add(_scroll);
        Grid.SetRow(_scroll, 1);

        // Zero-copy GPU present surface. Stand it up only where it can work: macOS
        // with a render session that supports surface targets. The actual surface
        // target is created lazily in OnSurfaceReady.
        if (OperatingSystem.IsMacOS() && _renderSession.SupportsSurfaceTargets)
        {
            _pageSurfaceHost = new PageSurfaceHost
            {
                Scale = _currentScale,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                IsHitTestVisible = false,
                IsVisible = false,
            };
            _pageSurfaceHost.SurfaceReady += OnSurfaceReady;
            rootGrid.Children.Add(_pageSurfaceHost);
            Grid.SetRow(_pageSurfaceHost, 1);
        }

        Content = rootGrid;
        ApplyTheme();
        _tm.Changed += (_, _) => ApplyTheme();
    }

    private void ApplyTheme()
    {
        var t = _tm.Tokens;
        _scroll.Background = new SolidColorBrush(t.WebBg);
        _pageCanvas.Background = new SolidColorBrush(t.WebBg);
        _placeholderHost.Background = new SolidColorBrush(t.WebBg);
        _placeholder.Foreground = new SolidColorBrush(t.Muted);
        _placeholder.FontSize = _tm.Metrics.FsLg;
        _findBar.Background = new SolidColorBrush(t.Panel);
        _findBar.BorderBrush = new SolidColorBrush(t.Border);
        _findEntry.Background = new SolidColorBrush(t.Surface);
        _findEntry.Foreground = new SolidColorBrush(t.Text);
    }

    /// <summary>
    /// Renders <paramref name="page"/> through the paint pipeline and shows
    /// the resulting bitmap. The page-sized <see cref="_pageCanvas"/> provides
    /// the scroll extent (virtual content), while <see cref="_pageImage"/> is
    /// sized to the visible viewport and re-rendered for the current scroll
    /// offset. Only the on-screen region is rasterized each frame.
    /// </summary>
    public void ShowPage(LaidOutPage page, bool preserveScroll = false, bool deferRender = false)
    {
        // Keep the scroll position across a reflow (resize re-layout) so the
        // user doesn't jump to the top; a fresh navigation resets to the top.
        var prevOffset = _scroll.Offset;

        // A relayout of the same document (resize / edit) keeps the focused
        // field; a fresh navigation produces a new document whose
        // FocusedElement is null, so the caret is dropped below.
        var keepFocus = _focusedInput is not null
                        && ReferenceEquals(page.Document.FocusedElement, _focusedInput);

        // Pause the live-page pump across the swap; rebound at the end against the
        // incoming page's JS context. (All on the UI thread, so the timer can't
        // tick mid-swap, but stopping keeps _boundScripting from briefly naming a
        // page we're about to dispose.)
        _liveTimer.Stop();

        _currentPage?.Dispose();
        _currentPage = page;

        // Re-baseline the live-loop relayout signal to this freshly laid-out
        // document. A relayout doesn't mutate the DOM, so the version is stable
        // across the swap; the next tick relayouts only if script advances it.
        _lastLayoutInvalidationVersion = page.Document.LayoutInvalidationVersion;

        // New laid-out page. On a navigation (a different Document) drop every
        // cache — flat pixels and the persistent per-layer compositor caches — so
        // nothing from the previous page survives. On an in-place relayout (same
        // Document: resize / edit / animation) keep the per-layer caches: they are
        // keyed by slice content hash, so an unchanged layer re-blits from cache
        // and only genuinely changed layers re-raster. The flat scroll cache is
        // dropped either way because its version-keyed pixels would otherwise blit
        // stale. _scrollOffsetsDocument still holds the PREVIOUS
        // document here (it is updated below), so it tells navigation from relayout.
        var isNavigation = !ReferenceEquals(_scrollOffsetsDocument, page.Document);
        if (isNavigation)
        {
            _renderSession.ResetForNavigation();
        }


        // Coalesce the surface present across this whole swap. ShowPage mutates
        // several overlays (selection, find, highlight, caret), and on the surface
        // path each mutation's RefreshOverlays would otherwise fire a full layer-tree
        // present of the same frame — ~4 redundant presents per relayout tick. Suppress
        // them and present exactly once at the end (the live loop passes deferRender
        // and presents once itself at the end of its tick).
        var prevSuppressPresent = _suppressPresent;
        _suppressPresent = true;
        try
        {
            // Reset interaction state.
            ClearSelection();
            ClearFindHighlight();
            ClearHighlightOverlays();
            _hoverElement = null;
            _hoverOverrides = null;
            _hoverScope.Clear();

            // Per-element scroll offsets survive in-place relayouts (window resize
            // keeps the same Document) but reset on navigation. Tracking the
            // Document reference lets us distinguish the two without an explicit
            // signal from MainWindow.
            if (!ReferenceEquals(_scrollOffsetsDocument, page.Document))
            {
                _scrollOffsets.Clear();
                _scrollOffsetsDocument = page.Document;
            }

            _currentScale = GetRenderScale();
            _pageSurfaceHost?.UpdateScale(_currentScale);

            // Page-sized canvas = scroll extent. The bitmap overlaid on it is only
            // viewport-sized; ShowPage resets to the top, then renders that region.
            _pageImage.IsVisible = true;
            _pageCanvas.Width = page.Root.Frame.Width;
            _pageCanvas.Height = page.Root.Frame.Height;
            _scroll.Content = _pageCanvas;

            if (preserveScroll)
            {
                var maxX = Math.Max(0, _pageCanvas.Width - _scroll.Viewport.Width);
                var maxY = Math.Max(0, _pageCanvas.Height - _scroll.Viewport.Height);
                _scroll.Offset = new Vector(
                    Math.Clamp(prevOffset.X, 0, maxX),
                    Math.Clamp(prevOffset.Y, 0, maxY));
            }
            else
            {
                _scroll.Offset = new Vector(0, 0);
            }

            using (_diag.Span("gui", "show_page.hit_index"))
                _fragments = BoxHitTester.CollectFragments(page.Root);
            RebuildFindIndex();

            // Re-establish (or drop) the text caret against the new box tree. The
            // caret overlay belongs to the prior layout's positions, so it is always
            // rebuilt here.
            CaretLog($"ShowPage: keepFocus={keepFocus} idx={_caretIndex} foc={_focusedInput?.LocalName} " +
                     $"docFoc={page.Document.FocusedElement?.LocalName} vp={page.Viewport.Width}x{page.Viewport.Height} preserveScroll={preserveScroll}");
            RemoveCaretOverlay();
            if (keepFocus)
            {
                _pageCanvas.Focus();
                RenderCaret();
            }
            else
            {
                _focusedInput = null;
                _caretIndex = 0;
                _caretBlinkTimer.Stop();
            }

            BindLiveScripting();
        }
        finally
        {
            _suppressPresent = prevSuppressPresent;
        }

        // One present for the whole swap, after the caret/overlays are in place so
        // the frame includes them. The live animation loop defers (deferRender:true)
        // and presents exactly once at the end of its own tick instead.
        if (!deferRender)
            RenderViewportRegion();
    }

    /// <summary>
    /// The currently-visible page-coordinate rectangle: the scroll offset plus
    /// the ScrollViewer viewport size. Falls back to the page width / a nominal
    /// height before the control has been measured.
    /// </summary>
    private Starling.Layout.Rect CurrentViewportRect()
    {
        var pageW = _currentPage?.Root.Frame.Width ?? 0;
        var pageH = _currentPage?.Root.Frame.Height ?? 0;

        var vpW = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : Math.Max(1, pageW);
        var vpH = _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : Math.Max(1, Math.Min(pageH, 1024));

        var offX = _scroll.Offset.X;
        var offY = _scroll.Offset.Y;

        // Clamp so we never request a region past the page extent.
        vpW = Math.Max(1, Math.Min(vpW, Math.Max(1, pageW - offX)));
        vpH = Math.Max(1, Math.Min(vpH, Math.Max(1, pageH - offY)));

        return new Starling.Layout.Rect(offX, offY, vpW, vpH);
    }

    /// <summary>
    /// Renders the current viewport region and positions the bitmap at the
    /// scroll offset so it stays aligned with the page-coordinate overlays.
    /// </summary>
    private void RenderViewportRegion()
    {
        if (_currentPage is null) return;
        var rect = CurrentViewportRect();

        ArgumentNullException.ThrowIfNull(_pageSurfaceHost);

        PresentSurface(rect);

        _pageSurfaceHost.Width = rect.Width;
        _pageSurfaceHost.Height = rect.Height;
        _pageSurfaceHost.IsVisible = true;
        if (_pageImage.IsVisible) _pageImage.IsVisible = false;
    }

    /// <summary>Device-pixel size of a page-coordinate viewport rect at the current scale.</summary>
    private (int Width, int Height) DeviceSize(Starling.Layout.Rect rect)
        => ((int)Math.Ceiling(rect.Width * _currentScale), (int)Math.Ceiling(rect.Height * _currentScale));

    /// <summary>
    /// Configures the GPU surface once its native handle is ready, then presents the
    /// current viewport. Runs on the UI thread (NativeControlHost attach).
    /// </summary>
    private void OnSurfaceReady()
    {
        if (_pageSurfaceHost is null || !_renderSession.SupportsSurfaceTargets)
        {
            return;
        }

        var layer = _pageSurfaceHost.MetalLayerPtr;
        if (layer == 0)
        {
            return;
        }

        try
        {
            // Build the surface target bound to the host's CAMetalLayer.
            _surfaceTarget = MetalLayerFrameTarget.TryCreate(layer, _diag);
            if (_surfaceTarget is null)
            {
                throw new InvalidOperationException("GPU page surface unavailable.");
            }

            var rect = CurrentViewportRect();
            var (w, h) = DeviceSize(rect);
            _surfaceTarget.Configure(w, h);
            _useSurface = true;
            _diag.Log(DiagLevel.Info, "gui", $"zero-copy GPU page surface active ({w}x{h} device px)");
        }
        catch (Exception ex)
        {
            // The selected GPU session must not silently switch to the bitmap path.
            _diag.LogException("gui", ex, "GPU page surface setup failed");
            _surfaceTarget?.Dispose();
            _surfaceTarget = null;
            _useSurface = false;
            throw;
        }

        if (_currentPage is not null)
        {
            RenderViewportRegion();
        }
    }

    /// <summary>
    /// Presents <paramref name="rect"/> (page-coord viewport) straight to the GPU
    /// surface — no readback, no WriteableBitmap. Returns false only when the
    /// surface is not active or not ready yet.
    /// </summary>
    private void PresentSurface(Starling.Layout.Rect rect)
    {
        if (!_useSurface || _surfaceTarget is null
                         || _pageSurfaceHost is null || !_pageSurfaceHost.HasSurface || _currentPage is null)
        {
            throw new InvalidOperationException("Surface is not active or not ready");
        }

        if (Activity.Current is { IsStopped: true })
        {
            Activity.Current = null;
        }

        var (styleOverride, scrollLookup) = BuildRenderInputs();

        var overlays = CollectSurfaceOverlays();
        bool ok;
        try
        {
            // Present at the current viewport: the surface target configures its
            // swapchain to ceil(rect × scale) device px as needed, so no explicit
            // Resize call is required. The scroll lookup threads per-container
            // (overflow:scroll) offsets into the layer tree so inner-scrolled pages
            // present on the zero-copy surface instead of dropping to readback.
            using (_diag.Span("gui", "render"))
            using (var frame = _renderSession.Render(new PageFrameRequest
                   {
                       Root = _currentPage.Root,
                       Scale = (float)_currentScale,
                       StyleOverride = styleOverride,
                       Images = _currentPage.ImageResolver,
                       Viewport = rect,
                       IsAnimatingLayerRoot = IsElementAnimatingLayerRoot,
                       Overlays = overlays,
                       ScrollOffsets = scrollLookup,
                   }, _surfaceTarget))
            {
                ok = frame.Presented;
            }
        }
        catch (Exception ex)
        {
            _diag.LogException("gui", ex, "GPU surface present failed");
            throw;
        }

        if (ok)
        {
            RecordPresentedFrame();
        }
        else
        {
            throw new InvalidOperationException("GPU surface did not present the frame.");
        }
    }

    /// <summary>
    /// Snapshots the page-coordinate overlay rects (selection highlight, find-match
    /// flash, blinking caret) the readback path draws as Avalonia controls, so the
    /// surface path can paint them on top of the page. The native surface occludes
    /// those Avalonia controls, so on the surface path they must be drawn into the
    /// GPU frame instead. Returns null when there's nothing to overlay.
    /// </summary>
    private IReadOnlyList<SurfaceOverlayRect>? CollectSurfaceOverlays()
    {
        List<SurfaceOverlayRect>? overlays = null;

        void Add(Control c)
        {
            if (!c.IsVisible) return;
            if (c is not Border b || b.Background is not ISolidColorBrush brush) return;
            var w = b.Width;
            var h = b.Height;
            if (double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0) return;
            var x = Canvas.GetLeft(b);
            var y = Canvas.GetTop(b);
            var col = brush.Color;
            (overlays ??= new List<SurfaceOverlayRect>())
                .Add(new SurfaceOverlayRect(x, y, w, h, col.R, col.G, col.B, col.A));
        }

        foreach (var o in _selectionOverlays) Add(o);
        foreach (var o in _findOverlays) Add(o);
        foreach (var o in _highlightOverlays) Add(o);
        if (_caretOverlay is not null) Add(_caretOverlay);

        return overlays;
    }

    /// <summary>
    /// Re-presents the page when an overlay (caret blink, selection, find flash)
    /// changes while the GPU surface is active — the Avalonia overlay controls are
    /// occluded by the surface, so they must be re-drawn into the frame. A no-op on
    /// the readback path, where the controls render themselves over the bitmap, and
    /// while <see cref="_suppressPresent"/> is set (a ShowPage batch coalesces its
    /// many overlay mutations into the single present ShowPage fires at the end).
    /// </summary>
    private void RefreshOverlays()
    {
        if (_useSurface && _currentPage is not null && !_suppressPresent)
            RenderViewportRegion();
    }

    /// <summary>
    /// Builds the per-frame hover/animation style override and the per-container
    /// scroll-offset lookup shared by the readback (<see cref="RenderPageBitmap"/>)
    /// and the zero-copy surface present paths.
    /// </summary>
    private (Func<LayoutBox, ComputedStyle?>? StyleOverride, Func<DomElement, (double X, double Y)>? ScrollLookup)
        BuildRenderInputs()
    {
        var overrides = _hoverOverrides;
        // While animating, overlay each animated element's sampled style; hover
        // overrides still win for the hovered element.
        var animate = _animating && _currentPage is not null;
        Func<LayoutBox, ComputedStyle?>? styleOverride =
            (overrides is null && !animate)
                ? null
                : box => (overrides is null ? null : ResolveOverride(box, overrides))
                         ?? (animate ? AnimatedStyle(_currentPage!, box) : null);

        Func<DomElement, (double X, double Y)>? scrollLookup = _scrollOffsets.Count == 0
            ? null
            : el => _scrollOffsets.TryGetValue(el, out var off) ? off : (0d, 0d);

        return (styleOverride, scrollLookup);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        RenderViewportRegion();
        if (e.ViewportDelta.X != 0 || e.ViewportDelta.Y != 0)
            ScheduleRelayout();
    }

    /// <summary>
    /// The live CSS layout viewport — the ScrollViewer's visible area (which
    /// already excludes any scrollbar gutter). Falls back to the control bounds,
    /// then a nominal size, before the panel has been measured. This is the
    /// width/height pages are laid out against, so it tracks the window size.
    /// </summary>
    public EngineSize CurrentViewportSize()
    {
        var w = _scroll.Viewport.Width;
        var h = _scroll.Viewport.Height;
        if (w < 1 || h < 1)
        {
            w = Bounds.Width > 1 ? Bounds.Width : 1200;
            h = Bounds.Height > 1 ? Bounds.Height : 900;
        }

        return new EngineSize((int)Math.Round(w), (int)Math.Round(h));
    }

    private void ScheduleRelayout()
    {
        if (_relayout is null) return;
        // Restart the timer so a continuous drag-resize only reflows once it
        // pauses, not on every intermediate size.
        _relayoutTimer.Stop();
        _relayoutTimer.Start();
    }

    private void RelayoutToViewport()
    {
        _relayoutTimer.Stop();
        var page = _currentPage;
        if (page is null || _relayout is null) return;

        var size = CurrentViewportSize();
        // No-op if the layout viewport already matches what produced this page —
        // guards against scrollbar-toggle flicker and the post-load delta.
        if (size.Width == (int)Math.Round(page.Viewport.Width) &&
            size.Height == (int)Math.Round(page.Viewport.Height))
            return;

        CaretLog(
            $"RelayoutToViewport: {(int)page.Viewport.Width}x{(int)page.Viewport.Height} -> {size.Width}x{size.Height}");
        var relaid = _relayout(page, size);
        if (relaid is not null)
            ShowPage(relaid, preserveScroll: true);
    }

    private double GetRenderScale()
    {
        var tl = TopLevel.GetTopLevel(this);
        return tl?.RenderScaling ?? 1.0;
    }

    // ---- Pointer event handlers ----------------------------------------

    private (double X, double Y)? PointerToDocSpace(PointerEventArgs e)
    {
        if (_currentPage is null) return null;
        var p = e.GetPosition(_pageCanvas);
        if (p.X < 0 || p.Y < 0) return null;
        if (p.X >= _pageCanvas.Bounds.Width || p.Y >= _pageCanvas.Bounds.Height) return null;
        return (p.X, p.Y);
    }

    /// <summary>
    /// Wrap <see cref="BoxHitTester.HitTest(Starling.Layout.Box.BlockBox,double,double,double,double,Func{DomElement,(double,double)}?)"/>
    /// with the current viewport (page scroll) and per-element scroll-offset
    /// state so hit-testing sees the same coordinate space the painter uses.
    /// </summary>
    private BoxHitTester.HitResult HitTestPage(double x, double y)
    {
        if (_currentPage is null) return default;
        Func<DomElement, (double X, double Y)>? lookup = _scrollOffsets.Count == 0
            ? null
            : el => _scrollOffsets.TryGetValue(el, out var off) ? off : (0d, 0d);
        return BoxHitTester.HitTest(_currentPage.Root, x, y, _scroll.Offset.X, _scroll.Offset.Y, lookup);
    }

    // Wheel handler: walks the ancestor chain at the pointer to find the
    // deepest `overflow: scroll | auto` box with room to scroll in the wheel
    // direction. When found, the box's scroll offset is updated and the page
    // repainted; the event is marked Handled so the outer ScrollViewer does
    // not also scroll. Falls through to the outer ScrollViewer otherwise.
    private void OnPageWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_currentPage is null) return;
        var doc = PointerToDocSpace(e);
        if (doc is null) return;

        var hit = HitTestPage(doc.Value.X, doc.Value.Y);
        if (hit.Box is null) return;

        // Wheel deltas in Avalonia are in "lines" (positive = up/right, the
        // direction the content moves). Multiply to a sensible CSS-px step;
        // 40 px per line is the platform-default ballpark.
        const double LinePx = 40d;
        var dx = -e.Delta.X * LinePx;
        var dy = -e.Delta.Y * LinePx;

        if (!TryScrollContainer(hit.Box, dx, dy)) return;
        e.Handled = true;
        RenderViewportRegion();
    }

    /// <summary>
    /// Walk from <paramref name="box"/> up the parent chain looking for a
    /// scroll container that can absorb a wheel delta of (<paramref name="dx"/>,
    /// <paramref name="dy"/>) — i.e. its content overflows its frame in the
    /// requested direction and its current offset isn't already pinned at the
    /// extreme. Returns true when an offset was updated.
    /// </summary>
    private bool TryScrollContainer(LayoutBox box, double dx, double dy)
    {
        for (var node = (LayoutBox?)box; node is not null; node = node.Parent)
        {
            var style = node.Style;
            if (style is null || node.Element is not { } el) continue;
            var scrollsX = IsScrollAxisKeyword(style.Get(PropertyId.OverflowX));
            var scrollsY = IsScrollAxisKeyword(style.Get(PropertyId.OverflowY));
            if (!scrollsX && !scrollsY) continue;

            var contentSize = ContentExtent(node);
            var visibleW = Math.Max(0, node.Frame.Width - node.Padding.Horizontal - node.Border.Horizontal);
            var visibleH = Math.Max(0, node.Frame.Height - node.Padding.Vertical - node.Border.Vertical);
            var maxX = scrollsX ? Math.Max(0, contentSize.W - visibleW) : 0;
            var maxY = scrollsY ? Math.Max(0, contentSize.H - visibleH) : 0;
            if (maxX <= 0 && maxY <= 0) continue;

            _scrollOffsets.TryGetValue(el, out var current);
            var newX = Math.Clamp(current.X + (scrollsX ? dx : 0), 0, maxX);
            var newY = Math.Clamp(current.Y + (scrollsY ? dy : 0), 0, maxY);
            if (newX == current.X && newY == current.Y) continue; // hit the rail, keep looking
            _scrollOffsets[el] = (newX, newY);
            return true;
        }

        return false;
    }

    private static bool IsScrollAxisKeyword(Starling.Css.Values.CssValue? value)
        => value is Starling.Css.Values.CssKeyword { Name: var n }
           && (n.Equals("scroll", StringComparison.OrdinalIgnoreCase)
               || n.Equals("auto", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bounding extent of <paramref name="box"/>'s in-flow descendants in its
    /// own content-box coordinates — i.e. how far the scrolled content
    /// reaches. Children whose frames poke past the box's visible width/height
    /// give us the scroll-extent maxima.
    /// </summary>
    private static (double W, double H) ContentExtent(LayoutBox box)
    {
        double maxX = 0, maxY = 0;
        foreach (var child in box.Children)
        {
            var right = child.Frame.X + child.Frame.Width;
            var bottom = child.Frame.Y + child.Frame.Height;
            if (right > maxX) maxX = right;
            if (bottom > maxY) maxY = bottom;
        }

        return (maxX, maxY);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_currentPage is null) return;
        var doc = PointerToDocSpace(e);
        if (doc is null)
        {
            ResetCursor();
            return;
        }

        if (_selecting && _selectAnchor is { } anchor)
        {
            UpdateSelection(anchor, doc.Value);
            return;
        }

        UpdateHover(HitTestPage(doc.Value.X, doc.Value.Y));
    }

    /// <summary>Applies the cursor and CSS <c>:hover</c> state for a hit, and
    /// surfaces a hovered link's href in the status bar. Shared by the real
    /// pointer-move handler and the synthetic <see cref="MoveTo"/> entry point.</summary>
    private void UpdateHover(BoxHitTester.HitResult hit)
    {
        ApplyCursor(hit);

        // CSS :hover follows the innermost element under the pointer (any
        // element, not just links), so buttons/tiles/divs re-cascade too.
        var hoverEl = FindClickTarget(hit.Box);
        if (!ReferenceEquals(hoverEl, _hoverElement))
        {
            _hoverElement = hoverEl;
            ApplyHoverState();
        }

        var anchorEl = hit.LinkAnchor;
        if (anchorEl is not null)
        {
            var href = anchorEl.GetAttribute("href");
            if (!string.IsNullOrEmpty(href))
                _onStatus(href!, false);
        }
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        ResetCursor();
        if (_hoverElement is not null)
        {
            _hoverElement = null;
            ApplyHoverState();
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentPage is null) return;
        if (!e.GetCurrentPoint(_pageCanvas).Properties.IsLeftButtonPressed) return;

        var doc = PointerToDocSpace(e);
        if (doc is null) return;

        // Always collapse any prior selection on primary press; the new gesture
        // is either a single click (link / blank) or the start of a fresh drag.
        ClearSelection();

        _selectAnchor = doc.Value;
        _selecting = true;
        e.Pointer.Capture(_pageCanvas);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_currentPage is null)
        {
            EndSelectionDrag(cancel: true);
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left) return;

        var wasSelecting = _selecting && _selectAnchor is not null;
        var dragged = wasSelecting && _selectionOverlays.Count > 0;

        EndSelectionDrag(cancel: false);

        if (dragged) return; // drag-select: don't navigate on release

        var doc = PointerToDocSpace(e);
        if (doc is null) return;

        if (PerformClick(doc.Value).Handled)
            e.Handled = true;
    }

    /// <summary>
    /// Runs the click decision tree at a document-space point, mirroring a real
    /// left-click release: a hit on an editable text field focuses it (caret at
    /// the clicked character); otherwise a DOM <c>click</c> is dispatched into the
    /// page (JS handlers, checkbox toggle, form submit) and, if the page consumed
    /// it, field focus is kept; otherwise the current field is blurred and a hit
    /// link is followed. <paramref name="Handled"/> reports whether anything
    /// consumed the click (so the pointer handler can mark the event handled);
    /// <paramref name="Detail"/> is a short human-readable outcome for tooling.
    /// </summary>
    private (bool Handled, string Detail) PerformClick((double X, double Y) doc)
    {
        var hit = HitTestPage(doc.X, doc.Y);

        if (FindFocusableInput(hit.Box) is { } field)
        {
            FocusInput(field, doc);
            return (true, $"focused <{field.LocalName}>");
        }

        if (DispatchClick(hit, doc))
            return (true, $"clicked <{DescribeHit(hit)}> (page handled)");

        // Not consumed by the page: a click elsewhere blurs the current field and
        // then follows a link if one was hit.
        BlurInput();

        if (hit.LinkAnchor is { } anchorEl)
        {
            var href = anchorEl.GetAttribute("href");
            if (!string.IsNullOrEmpty(href) && ResolveLink(href!) is { Length: > 0 } resolved)
            {
                _onLinkActivated(resolved);
                return (true, $"followed link to {resolved}");
            }
        }

        return (false, hit.Box is null ? "clicked empty area" : $"clicked <{DescribeHit(hit)}> (no action)");
    }

    private static string DescribeHit(BoxHitTester.HitResult hit)
        => FindClickTarget(hit.Box)?.LocalName ?? "unknown";

    private void EndSelectionDrag(bool cancel)
    {
        _selecting = false;
        if (cancel) _selectAnchor = null;
    }

    // ---- Text input editing -------------------------------------------
    //
    // Click-to-focus + type for <input>/<textarea>. The value lives on the DOM
    // element (Element.InputValue); each edit mutates it, re-lays-out the page
    // (so the synthesized label text refreshes), and repositions a blinking
    // caret drawn as a page overlay. document.FocusedElement is kept in sync so
    // :focus CSS and the JS activeElement/.value accessors agree. Firing DOM
    // input/keydown events into page scripts is a deliberate follow-up.

    private const double CaretWidth = 1.0;

    /// <summary>
    /// The editable <c>&lt;input&gt;</c>/<c>&lt;textarea&gt;</c> enclosing
    /// <paramref name="box"/> in the box/DOM tree, or null when the hit isn't a
    /// text-entry control. Non-text inputs (button, checkbox, …) are excluded.
    /// </summary>
    private static DomElement? FindFocusableInput(LayoutBox? box)
    {
        for (var b = box; b is not null; b = b.Parent)
        {
            if (b.Element is not DomElement el) continue;
            if (!el.HasAttribute("disabled") && !el.HasAttribute("readonly") && HtmlFormControls.IsTextControl(el))
                return el;
        }

        return null;
    }

    private string CurrentValue()
        => _focusedInput is null ? string.Empty : HtmlFormControls.Value(_focusedInput);

    /// <summary>
    /// Focuses <paramref name="input"/>: records document focus (so <c>:focus</c>
    /// and <c>activeElement</c> see it), re-lays-out so a focused empty field
    /// drops its placeholder, then places the caret at the clicked character.
    /// </summary>
    private void FocusInput(DomElement input, (double X, double Y) clickDoc)
    {
        if (_currentPage is null) return;

        _focusedInput = input;
        _currentPage.Document.FocusedElement = input;
        _valueAtFocus = CurrentValue();
        _caretIndex = CurrentValue().Length;
        HtmlFormControls.SetSelectionRange(input, _caretIndex, _caretIndex, "none");
        _pageCanvas.Focus();

        // Notify page JS (HTML focus event order: focus, then focusin/bubbles).
        DispatchDom(input, new FocusEvent("focus"));
        DispatchDom(input, new FocusEvent("focusin", new EventInit(Bubbles: true)));

        RefreshFocusedLayout();
        _caretIndex = CaretIndexFromClick(clickDoc);
        RenderCaret();
    }

    /// <summary>Clears the current field focus (caret removed, placeholder
    /// restored), if any. A no-op when nothing is focused.</summary>
    private void BlurInput()
    {
        if (_focusedInput is null) return;
        var was = _focusedInput;
        var finalValue = HtmlFormControls.Value(was);
        var changed = !string.Equals(finalValue, _valueAtFocus, StringComparison.Ordinal);
        _focusedInput = null;
        _caretBlinkTimer.Stop();
        RemoveCaretOverlay();
        if (_currentPage is not null && ReferenceEquals(_currentPage.Document.FocusedElement, was))
            _currentPage.Document.FocusedElement = null;

        // HTML order: a value edited since focus fires `change`, then blur/focusout.
        if (changed) DispatchDom(was, new Event("change", new EventInit(Bubbles: true)));
        DispatchDom(was, new FocusEvent("blur"));
        DispatchDom(was, new FocusEvent("focusout", new EventInit(Bubbles: true)));

        RefreshFocusedLayout(); // placeholder returns on the now-blurred field
    }

    private void OnPageTextInput(object? sender, TextInputEventArgs e)
    {
        if (_focusedInput is null || string.IsNullOrEmpty(e.Text)) return;
        // Drop control characters — Enter/Tab arrive via OnPageKeyDown, and some
        // platforms also surface them here.
        var text = new string(e.Text.Where(c => !char.IsControl(c)).ToArray());
        if (text.Length == 0) return;

        e.Handled = true;
        var val = CurrentValue();
        var idx = Math.Clamp(_caretIndex, 0, val.Length);
        CaretLog($"TextInput: '{text}' at idx={idx} (caretIdx={_caretIndex}) val='{val}'");
        CommitValue(val.Insert(idx, text), idx + text.Length);
    }

    private void OnPageKeyUp(object? sender, KeyEventArgs e)
    {
        if (_focusedInput is null) return;
        DispatchDom(_focusedInput, MakeKeyboardEvent("keyup", e));
    }

    private void OnPageKeyDown(object? sender, KeyEventArgs e)
    {
        if (_focusedInput is null) return;
        DispatchDom(_focusedInput, MakeKeyboardEvent("keydown", e));
        var val = CurrentValue();
        var idx = Math.Clamp(_caretIndex, 0, val.Length);
        switch (e.Key)
        {
            case Key.Back:
                if (idx > 0) CommitValue(val.Remove(idx - 1, 1), idx - 1);
                e.Handled = true;
                break;
            case Key.Delete:
                if (idx < val.Length) CommitValue(val.Remove(idx, 1), idx);
                e.Handled = true;
                break;
            case Key.Left:
                MoveCaret(idx - 1);
                e.Handled = true;
                break;
            case Key.Right:
                MoveCaret(idx + 1);
                e.Handled = true;
                break;
            case Key.Home:
                MoveCaret(0);
                e.Handled = true;
                break;
            case Key.End:
                MoveCaret(val.Length);
                e.Handled = true;
                break;
            case Key.Escape:
                BlurInput();
                e.Handled = true;
                break;
            case Key.Enter:
                // Implicit form submission: Enter in a text field submits its
                // owning form (the keydown was already dispatched above).
                e.Handled = true;
                if (_focusedInput.LocalName == "input")
                {
                    var actioned = false;
                    if (SubmitOwningForm(_focusedInput, ref actioned))
                        RefreshLiveLayout();
                }

                break;
        }
    }

    /// <summary>Writes the field's new value + caret position, then re-lays-out
    /// so the rendered label text matches.</summary>
    private void CommitValue(string next, int newCaret)
    {
        if (_focusedInput is null) return;
        HtmlFormControls.SetValue(_focusedInput, next);
        _caretIndex = Math.Clamp(newCaret, 0, next.Length);
        HtmlFormControls.SetSelectionRange(_focusedInput, _caretIndex, _caretIndex, "none");
        // Fire the DOM `input` event so search-as-you-type / form handlers run;
        // RefreshFocusedLayout then reflects both the new value text and any DOM
        // the handler mutated synchronously.
        DispatchDom(_focusedInput, new InputEvent("input", new EventInit(Bubbles: true)) { InputType = "insertText" });
        RefreshFocusedLayout();
    }

    private void MoveCaret(int index)
    {
        _caretIndex = Math.Clamp(index, 0, CurrentValue().Length);
        if (_focusedInput is not null)
            HtmlFormControls.SetSelectionRange(_focusedInput, _caretIndex, _caretIndex, "none");
        RenderCaret(); // value unchanged — no re-layout needed
    }

    /// <summary>Re-lays-out the current page (reusing its document/resources) so
    /// the focused field's text and placeholder reflect the latest state, then
    /// reshows it preserving scroll. Falls back to repositioning the caret in
    /// place when no relayout hook is wired.</summary>
    private void RefreshFocusedLayout()
    {
        if (_currentPage is null || _relayout is null)
        {
            RenderCaret();
            return;
        }

        var relaid = _relayout(_currentPage, CurrentViewportSize());
        if (relaid is not null)
            ShowPage(relaid, preserveScroll: true); // re-renders caret if still focused
        else
            RenderCaret();
    }

    // TEMP caret diagnostics — gated behind STARLING_CARET_DEBUG=1.
    private static readonly bool _caretDebug =
        Environment.GetEnvironmentVariable("STARLING_CARET_DEBUG") == "1";

    private static void CaretLog(string msg)
    {
        if (!_caretDebug) return;
        try
        {
            System.IO.File.AppendAllText("/tmp/starling-caret.log", $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch
        {
        }
    }

    /// <summary>(Re)draws the caret overlay at the current insertion point and
    /// (re)starts the blink. Removes the caret when nothing is focused.</summary>
    private void RenderCaret()
    {
        RemoveCaretOverlay();
        if (_focusedInput is null || _currentPage is null)
        {
            CaretLog("RenderCaret: no focus/page");
            return;
        }

        if (ComputeCaretRect(_currentPage.Root, _focusedInput, _caretIndex) is not { } c)
        {
            CaretLog($"RenderCaret: ComputeCaretRect NULL idx={_caretIndex} val='{CurrentValue()}'");
            return;
        }

        CaretLog($"RenderCaret: idx={_caretIndex} X={c.X:F1} val='{CurrentValue()}'");
        _caretOverlay = MakeOverlay(new SolidColorBrush(c.Color), c.X, c.Y, CaretWidth, c.H);
        _pageCanvas.Children.Add(_caretOverlay);
        _caretOn = true;
        _caretBlinkTimer.Stop();
        _caretBlinkTimer.Start(); // solid immediately after a change, then blinks
        RefreshOverlays();
    }

    private void RemoveCaretOverlay()
    {
        if (_caretOverlay is null) return;
        _pageCanvas.Children.Remove(_caretOverlay);
        _caretOverlay = null;
        RefreshOverlays();
    }

    /// <summary>
    /// Document-space caret rectangle (and colour) for <paramref name="caretIndex"/>
    /// inside <paramref name="input"/>'s value, or null if the field's box can't
    /// be located. Derives X from the shaped glyph pen positions of the value
    /// text fragment; an empty field gets a caret spanning the content box.
    /// </summary>
    private static (double X, double Y, double H, AvColor Color)? ComputeCaretRect(
        Starling.Layout.Box.BlockBox root, DomElement input, int caretIndex)
    {
        if (FindBoxAbs(root, 0, 0, b => ReferenceEquals(b.Element, input)) is not { } found)
            return null;

        var inputBox = found.Box;
        var color = ResolveCaretColor(inputBox);
        var cx = found.X + inputBox.Border.Left + inputBox.Padding.Left;
        var cy = found.Y + inputBox.Border.Top + inputBox.Padding.Top;

        var tb = FindValueTextBox(inputBox, cx, cy);
        if (tb is { } th)
        {
            // The value is laid out as one fragment per whitespace-delimited token
            // (words and the spaces between them), all in this text box. Walk them
            // in order, mapping the global caret index onto the fragment that holds
            // it — using only Fragments[0] froze the caret at the first space.
            var t = (Starling.Layout.Box.TextBox)th.Box;
            var remaining = Math.Max(0, caretIndex);
            foreach (var frag in t.Fragments)
            {
                if (remaining <= frag.Text.Length)
                    return (th.X + frag.X + XOffsetInFragment(frag, remaining),
                        th.Y + frag.Y, frag.Height, color);
                remaining -= frag.Text.Length;
            }

            // Past the last character → caret at the end of the final fragment.
            var last = t.Fragments[^1];
            return (th.X + last.X + XOffsetInFragment(last, last.Text.Length),
                th.Y + last.Y, last.Height, color);
        }

        // Empty field: caret at the content origin, spanning the content box.
        var contentH = inputBox.Frame.Height
                       - inputBox.Border.Top - inputBox.Border.Bottom
                       - inputBox.Padding.Top - inputBox.Padding.Bottom;
        return (cx, cy, contentH > 2 ? contentH : 14, color);
    }

    private int CaretIndexFromClick((double X, double Y) clickDoc)
    {
        var val = CurrentValue();
        if (_focusedInput is null || _currentPage is null || val.Length == 0) return val.Length;
        if (FindBoxAbs(_currentPage.Root, 0, 0, b => ReferenceEquals(b.Element, _focusedInput)) is not { } found)
            return val.Length;

        var inputBox = found.Box;
        var cx = found.X + inputBox.Border.Left + inputBox.Padding.Left;
        var cy = found.Y + inputBox.Border.Top + inputBox.Padding.Top;
        if (FindValueTextBox(inputBox, cx, cy) is not { } th) return val.Length;

        // Find the value fragment under the click (or the last one when the click
        // is past the text), then add the char offset within it to the count of
        // characters in the fragments before it.
        var t = (Starling.Layout.Box.TextBox)th.Box;
        var prior = 0;
        for (var fi = 0; fi < t.Fragments.Count; fi++)
        {
            var frag = t.Fragments[fi];
            var fragLeft = th.X + frag.X;
            var isLast = fi == t.Fragments.Count - 1;
            if (clickDoc.X < fragLeft + frag.Width || isLast)
                return prior + CharOffsetInFragment(frag, clickDoc.X - fragLeft);
            prior += frag.Text.Length;
        }

        return val.Length;
    }

    /// <summary>Character index (0..len) within <paramref name="frag"/> nearest to
    /// local x-offset <paramref name="localX"/> from the fragment's left edge,
    /// using shaped glyph midpoints when available and a proportional fallback.</summary>
    private static int CharOffsetInFragment(Starling.Layout.Box.TextFragment frag, double localX)
    {
        if (localX <= 0) return 0;
        if (frag.Shaped is { } sh && sh.Glyphs.Length == frag.Text.Length)
        {
            for (var i = 0; i < sh.Glyphs.Length; i++)
            {
                var left = sh.Glyphs[i].X;
                var right = i + 1 < sh.Glyphs.Length ? sh.Glyphs[i + 1].X : frag.Width;
                if (localX < (left + right) / 2) return i;
            }

            return frag.Text.Length;
        }

        var approx = (int)Math.Round(localX / Math.Max(1, frag.Width) * frag.Text.Length);
        return Math.Clamp(approx, 0, frag.Text.Length);
    }

    /// <summary>First non-empty text fragment box under <paramref name="inputBox"/>,
    /// with its absolute origin (the rendered value text).</summary>
    private static (LayoutBox Box, double X, double Y)? FindValueTextBox(
        LayoutBox inputBox, double contentX, double contentY)
    {
        foreach (var child in inputBox.Children)
        {
            if (FindBoxAbs(child, contentX, contentY,
                    b => b is Starling.Layout.Box.TextBox { Fragments.Count: > 0 }) is { } r)
                return r;
        }

        return null;
    }

    /// <summary>Finds the first box matching <paramref name="pred"/>, returning it
    /// with its absolute (document-space) border-box origin. Mirrors the
    /// origin accumulation used by <see cref="BoxHitTester"/>.</summary>
    private static (LayoutBox Box, double X, double Y)? FindBoxAbs(
        LayoutBox box, double originX, double originY, Func<LayoutBox, bool> pred)
    {
        var fx = originX + box.Frame.X;
        var fy = originY + box.Frame.Y;
        if (pred(box)) return (box, fx, fy);
        if (box is Starling.Layout.Box.TextBox) return null;

        var cx = fx + box.Border.Left + box.Padding.Left;
        var cy = fy + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            if (FindBoxAbs(child, cx, cy, pred) is { } r)
                return r;
        return null;
    }

    private static double XOffsetInFragment(Starling.Layout.Box.TextFragment frag, int caretIndex)
    {
        var n = Math.Clamp(caretIndex, 0, frag.Text.Length);
        if (n == 0) return 0;
        if (frag.Shaped is { } sh && sh.Glyphs.Length == frag.Text.Length)
            return n < sh.Glyphs.Length ? sh.Glyphs[n].X : frag.Width;
        return frag.Text.Length == 0 ? 0 : frag.Width * n / frag.Text.Length;
    }

    private static AvColor ResolveCaretColor(LayoutBox box)
    {
        var c = box.Style?.GetColor(PropertyId.Color);
        if (c is null) return AvColor.FromArgb(255, 0, 0, 0);
        return AvColor.FromArgb(c.A == 0 ? (byte)255 : c.A, c.R, c.G, c.B);
    }

    // ---- Live page event loop + DOM event dispatch --------------------
    //
    // While the current page carries a live JS context (PageScripting, attached
    // by the engine's interactive load), a ~60Hz timer pumps its microtasks /
    // setTimeout / rAF / fetch completions against wall-clock time and re-renders
    // whatever the page mutates. DOM events from interaction (focus/input/key…)
    // are dispatched into the same context so page handlers run.

    /// <summary>
    /// (Re)bind the live-page pump to the current page's JS context. Called at the
    /// end of <see cref="ShowPage"/> and by the shell once a navigation settles
    /// (the no-DOM-mutation case returns the already-shown page object, so
    /// ShowPage isn't re-invoked but the context has since been attached). The
    /// wall-clock baseline resets only when the context object changes, so a
    /// relayout of the same page keeps timer due-times monotonic.
    /// </summary>
    /// <summary>Wall-clock ms the live-page pump has been driving the current JS
    /// context — the timestamp to render a screenshot at so any CSS animations
    /// sample the same frame that's on screen. Zero when no live context is bound.</summary>
    public long LiveElapsedMs => _boundScripting is null ? 0 : _liveStopwatch.ElapsedMilliseconds;

    public void BindLiveScripting()
    {
        var scripting = _currentPage?.Scripting;
        // The live timer also drives the animation loop, so it must run for a
        // page that has in-flight animations even when it has no JS context.
        var wantsAnimation = _currentPage is { } p && _hasActiveAnimations?.Invoke(p) == true;
        if (scripting is null && !wantsAnimation)
        {
            _liveTimer.Stop();
            _boundScripting = null;
            return;
        }

        if (scripting is not null && !ReferenceEquals(scripting, _boundScripting))
        {
            _boundScripting = scripting;
            _liveStopwatch.Restart();
        }

        if (scripting is null && !_liveStopwatch.IsRunning) _liveStopwatch.Restart();
        if (!_liveTimer.IsEnabled) _liveTimer.Start();
    }

    private void LiveTick()
    {
        var page = _currentPage;
        if (page is null)
        {
            _liveTimer.Stop();
            _boundScripting = null;
            _animating = false;
            return;
        }

        // Age the recently-mutated promotion window by one frame before this tick
        // records new mutations, so a subtree promoted by a past mutation falls
        // back into the base layer once its hysteresis elapses.
        page.Document.DecayRecentMutations();

        using var detachedActivity = GuiActivityScope.Detached();
        // One span per frame, with sub-spans for each phase (pump / relayout /
        // prepare_anim / render) so a trace shows where a laggy animation frame
        // spends its time. The inner "gui.render" span lives in RenderPageBitmap.
        using var _tick = _diag.Span("gui", "live.tick");
        // End-to-end frame time for the daemon's frame report (overrun rate, p99,
        // fps). Emitted on the normal completion path below.
        var frameStart = System.Diagnostics.Stopwatch.GetTimestamp();

        var scripting = page.Scripting;
        if (scripting is not null)
        {
            try
            {
                using (_diag.Span("gui", "live.pump"))
                    scripting.PumpFrame(_liveStopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _diag.Log(DiagLevel.Warn, "gui", $"live JS pump failed: {ex.Message}");
                return;
            }
        }

        // Relayout only when this frame produced a *layout-relevant* mutation —
        // a structural/text change or a value change to a layout-relevant
        // attribute (Document.IsLayoutRelevantAttribute). Pure data-* / aria-* /
        // js* writes bump MutationVersion but not LayoutInvalidationVersion, so
        // an analytics or animation rAF that only touches those no longer pays a
        // full reflow per frame. Defer its paint: when the page is also animating
        // we advance the animation clock first and paint exactly once below —
        // instead of painting here with last frame's clock and again after
        // PrepareAnimationFrame.
        //
        // Attribute-selector invalidation is handled by Document.IsAttributeLayoutRelevant:
        // the layout pass records selector-referenced attributes, so author CSS
        // keyed on data-* / aria-* still advances LayoutInvalidationVersion.
        var layoutVersion = page.Document.LayoutInvalidationVersion;
        var needsRelayout = layoutVersion != _lastLayoutInvalidationVersion;
        if (needsRelayout)
        {
            _lastLayoutInvalidationVersion = layoutVersion;
            CaretLog("LiveTick: layout-relevant mutation -> RefreshLiveLayout");
            RefreshLiveLayout(deferRender: true);
            page = _currentPage;
            if (page is null)
            {
                _liveTimer.Stop();
                _boundScripting = null;
                _animating = false;
                return;
            }
        }

        // Animation loop: advance the page's animation/transition clock and
        // repaint while anything is in flight (no DOM mutation required). When
        // it settles we simply stop repainting, leaving the final frame in
        // place (no static revert — fill:forwards persists).
        var animating = _prepareAnimationFrame is not null && _hasActiveAnimations?.Invoke(page) == true;
        if (animating)
        {
            _animClockMs = _liveStopwatch.ElapsedMilliseconds;
            using (_diag.Span("gui", "live.prepare_anim"))
                _prepareAnimationFrame!(page, _animClockMs);
            _animating = true;
            // Re-sample the hovered scope at the advanced clock so a hover
            // transition (e.g. a tile easing on :hover) progresses. Elements that
            // left the hover scope are not here — they animate back via the
            // AnimatedStyle path below, which carries their reverse transition.
            if (_hoverElement is not null)
                _hoverOverrides = BuildHoverOverrides(_hoverElement, _animClockMs);
        }
        else
        {
            _animating = false;
        }

        // Exactly one paint per frame — the animation clock and _animating flag
        // are already set, so the sampled styles match this frame. An animating
        // frame takes the compositor path whether or not it relayouted. The
        // per-layer caches now survive relayout and each layer's content hash
        // decides what re-rasters, so a relayout-every-frame animation no longer
        // pays a full-viewport raster.
        if (needsRelayout || animating)
            RenderViewportRegion();

        // Stop the timer once neither scripting nor animation needs it.
        if (scripting is null && !animating)
        {
            _liveTimer.Stop();
            _boundScripting = null;
        }

        EmitFrameTime(frameStart);
    }

    // Records this frame's end-to-end time as gui.frame.time_ms and bumps the
    // budget-overrun counter when it blew the 60fps budget. The telemetry daemon
    // turns these into the frame-time distribution + overrun rate and correlates
    // the spikes with the sampled CPU/RAM.
    private const double FrameBudgetMs = 1000.0 / 60; // 16.667ms — the actual 60fps budget

    private void EmitFrameTime(long startTimestamp)
    {
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        _diag.Gauge(RenderMetrics.FrameTimeMs, ms);
        if (ms > FrameBudgetMs) _diag.Counter(RenderMetrics.FrameBudgetOverrun, 1);
    }

    /// <summary>Re-lay-out + repaint after the live loop mutated the DOM,
    /// preserving scroll and the text caret. No-op without a relayout hook.</summary>
    private void RefreshLiveLayout(bool deferRender = false)
    {
        if (_currentPage is null || _relayout is null) return;
        LaidOutPage? relaid;
        using (_diag.Span("gui", "live.relayout"))
            relaid = _relayout(_currentPage, CurrentViewportSize());
        if (relaid is not null) ShowPage(relaid, preserveScroll: true, deferRender: deferRender);
    }

    /// <summary>Dispatch a DOM event into the page's JS listeners (a no-op for
    /// pages without a live JS context), then ensure the live pump is running so
    /// any async work the listener scheduled (timers, fetch) gets serviced.
    /// Returns true when the dispatch synchronously mutated the DOM.</summary>
    private bool DispatchDom(DomElement target, Event evt)
    {
        var scripting = _currentPage?.Scripting;
        if (scripting is null) return false;
        bool mutated = false;
        try
        {
            mutated = scripting.DispatchEvent(target, evt);
        }
        catch (Exception ex)
        {
            _diag.Log(DiagLevel.Warn, "gui", $"DOM event dispatch failed: {ex.Message}");
        }

        if (!_liveTimer.IsEnabled) BindLiveScripting();
        return mutated;
    }

    /// <summary>
    /// Dispatches a DOM <c>click</c> at the hit element into the page's JS, then
    /// runs the click's default action for form controls — toggling a
    /// checkbox/radio (firing <c>input</c>+<c>change</c>) and submitting the
    /// owning form for a submit button (firing a cancelable <c>submit</c>). The
    /// page is re-laid-out if any of this mutated the DOM. Returns true when a
    /// live page consumed the click — the JS cancelled it, we activated a control,
    /// or the DOM changed — so the caller skips chrome handling (blur, link nav).
    /// </summary>
    private bool DispatchClick(BoxHitTester.HitResult hit, (double X, double Y) doc)
    {
        if (_currentPage?.Scripting is null) return false;
        if (FindClickTarget(hit.Box) is not { } target) return false;

        var click = new MouseEvent("click", new EventInit(Bubbles: true, Cancelable: true))
        {
            Button = 0,
            ClientX = doc.X - _scroll.Offset.X,
            ClientY = doc.Y - _scroll.Offset.Y,
        };
        var mutated = DispatchDom(target, click);

        var actioned = false;
        if (!click.DefaultPrevented)
            mutated |= RunClickDefault(target, out actioned);

        if (mutated) RefreshLiveLayout();
        return click.DefaultPrevented || actioned || mutated;
    }

    /// <summary>Runs the activation behaviour of a click on <paramref name="clicked"/>
    /// (or its nearest enclosing control): checkbox/radio toggle, or implicit form
    /// submission for a submit button. <paramref name="actioned"/> reports whether
    /// a control was activated; the return value reports whether the DOM mutated.</summary>
    private bool RunClickDefault(DomElement clicked, out bool actioned)
    {
        actioned = false;
        if (NearestControl(clicked) is not { } control) return false;

        switch (control.LocalName)
        {
            case "input":
                var type = (control.GetAttribute("type") ?? "text").Trim().ToLowerInvariant();
                if (type is "checkbox" or "radio")
                {
                    actioned = true;
                    HtmlFormControls.SetChecked(control, type == "radio" || !HtmlFormControls.Checked(control));
                    var m = DispatchDom(control, new InputEvent("input", new EventInit(Bubbles: true)));
                    m |= DispatchDom(control, new Event("change", new EventInit(Bubbles: true)));
                    return m;
                }

                if (type is "submit" or "image")
                    return SubmitOwningForm(control, ref actioned);
                return false;

            case "button":
                var btnType = (control.GetAttribute("type") ?? "submit").Trim().ToLowerInvariant();
                if (btnType == "submit")
                    return SubmitOwningForm(control, ref actioned);
                return false;

            default:
                return false;
        }
    }

    /// <summary>Fires a cancelable <c>submit</c> at <paramref name="control"/>'s
    /// owning &lt;form&gt; (implicit form submission). Sets <paramref name="actioned"/>
    /// when a form was found; returns whether the submit handler mutated the DOM.
    /// Actual form navigation when the event isn't cancelled is not yet wired —
    /// the SPA/todo pattern preventDefaults and handles submission in JS.</summary>
    private bool SubmitOwningForm(DomElement control, ref bool actioned)
    {
        if (HtmlFormControls.FormOwner(control) is not { } form) return false;
        actioned = true;
        if (!DispatchInvalidEvents(form)) return true;
        var submit = new Event("submit", new EventInit(Bubbles: true, Cancelable: true));
        var mutated = DispatchDom(form, submit);
        if (!submit.DefaultPrevented)
            HtmlFormControls.RecordAutocompleteSubmission(form);
        return mutated;
    }

    /// <summary>Nearest DOM element enclosing <paramref name="box"/> (the click's
    /// event target), or null for a hit with no element ancestor.</summary>
    private static DomElement? FindClickTarget(LayoutBox? box)
    {
        for (var b = box; b is not null; b = b.Parent)
            if (b.Element is DomElement el)
                return el;
        return null;
    }

    /// <summary>Nearest &lt;button&gt;/&lt;input&gt; control at or above
    /// <paramref name="el"/> (a click on a button's inner text activates the
    /// button), or null.</summary>
    private static DomElement? NearestControl(DomElement el)
    {
        for (DomNode? n = el; n is not null; n = n.ParentNode)
            if (n is DomElement { LocalName: "button" or "input" } c)
                return c;
        return null;
    }

    private static DomElement? NearestForm(DomElement el)
    {
        return HtmlFormControls.FormOwner(el);
    }

    private bool DispatchInvalidEvents(DomElement form)
    {
        var valid = true;
        foreach (var control in HtmlFormControls.FormControls(form))
        {
            if (HtmlFormControls.Validity(control).Valid) continue;
            valid = false;
            DispatchDom(control, new Event("invalid", new EventInit(Cancelable: true)));
        }

        return valid;
    }

    // ---- Programmatic input (MCP automation) --------
    //
    // ClickAt / MoveTo / TypeText let an out-of-process driver synthesize input
    // without a real device. Coordinates are document-space CSS px from the page's
    // top-left, the same space browser_screenshot captures across the full scroll
    // extent. These reuse the same paths the pointer and keyboard handlers drive.

    /// <summary>True once a page is laid out and ready to receive synthetic input.</summary>
    public bool HasPage => _currentPage is not null;

    /// <summary>Synthesizes a left click at a document-space point. See
    /// <see cref="PerformClick"/> for the decision tree. Fails only when no page
    /// is loaded; the returned detail describes what the click did.</summary>
    public InputResult ClickAt(double x, double y)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        return new InputResult(true, PerformClick((x, y)).Detail);
    }

    /// <summary>Moves the synthetic mouse to a document-space point: updates the
    /// cursor + CSS <c>:hover</c> exactly as a real move would, and dispatches DOM
    /// <c>mouseover</c>/<c>mousemove</c> (plus <c>mouseout</c> on the previously
    /// hovered element) so JS hover handlers run. Fails only when no page is
    /// loaded.</summary>
    public InputResult MoveTo(double x, double y)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");

        var hit = HitTestPage(x, y);
        UpdateHover(hit);
        DispatchMouseMove((x, y), FindClickTarget(hit.Box));

        if (hit.Box is null) return new InputResult(true, "moved over empty area");
        var link = hit.LinkAnchor?.GetAttribute("href") is { Length: > 0 } href ? $" → {href}" : string.Empty;
        return new InputResult(true, $"moved over <{DescribeHit(hit)}>{link}");
    }

    /// <summary>Clicks the center of the first rendered element matching
    /// <paramref name="selector"/>.</summary>
    public InputResult ClickBySelector(string selector)
    {
        var (els, error) = QueryAll(selector);
        if (error is not null) return new InputResult(false, error);
        if (els.Count == 0) return new InputResult(true, $"no elements matched '{selector}'");

        foreach (var el in els)
        {
            if (BoxForElement(el) is not { } loc) continue;
            var doc = (loc.X + loc.Box.Frame.Width / 2, loc.Y + loc.Box.Frame.Height / 2);
            return new InputResult(true, PerformClick(doc).Detail);
        }

        return new InputResult(false, $"matched elements for '{selector}' have no rendered box to click");
    }

    /// <summary>Scrolls the outer page viewport by document-space deltas. Positive
    /// <paramref name="deltaY"/> moves down the page. Positive
    /// <paramref name="deltaX"/> moves right. The offset is clamped to the page
    /// extent.</summary>
    public InputResult ScrollBy(double deltaX, double deltaY)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
            return new InputResult(false, $"scroll deltas must be finite, got {deltaX}, {deltaY}");

        var pageW = _pageCanvas.Width > 0 ? _pageCanvas.Width : _currentPage.Root.Frame.Width;
        var pageH = _pageCanvas.Height > 0 ? _pageCanvas.Height : _currentPage.Root.Frame.Height;
        var viewport = CurrentViewportSize();
        var viewportW = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : viewport.Width;
        var viewportH = _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : viewport.Height;
        var maxX = Math.Max(0, pageW - viewportW);
        var maxY = Math.Max(0, pageH - viewportH);

        var current = _scroll.Offset;
        var nextX = Math.Clamp(current.X + deltaX, 0, maxX);
        var nextY = Math.Clamp(current.Y + deltaY, 0, maxY);
        var changed = nextX != current.X || nextY != current.Y;
        if (changed)
        {
            _scroll.Offset = new Vector(nextX, nextY);
            RenderViewportRegion();
        }

        var prefix = changed ? "scrolled" : "scroll unchanged";
        return new InputResult(true, $"{prefix} to x={nextX:0.#}, y={nextY:0.#} (max x={maxX:0.#}, y={maxY:0.#})");
    }

    /// <summary>Scrolls to an absolute page offset or to the first rendered match
    /// for <paramref name="selector"/>.</summary>
    public InputResult ScrollTo(double? x, double? y, string? selector, string? position)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        if (selector is { Length: > 0 })
        {
            var (els, error) = QueryAll(selector);
            if (error is not null) return new InputResult(false, error);
            foreach (var el in els)
            {
                if (BoxForElement(el) is not { } loc) continue;
                var targetY = position?.Trim().ToLowerInvariant() switch
                {
                    "center" => loc.Y - (ViewportHeight() - loc.Box.Frame.Height) / 2,
                    "bottom" => loc.Y - ViewportHeight() + loc.Box.Frame.Height,
                    null or "" or "top" => loc.Y,
                    var p => double.NaN,
                };
                if (double.IsNaN(targetY))
                    return new InputResult(false, "position must be top, center, or bottom");
                return ScrollToOffset(x ?? _scroll.Offset.X, targetY,
                    $"scrolled to <{el.LocalName}> matching '{selector}'");
            }

            return new InputResult(true, $"no rendered element matched '{selector}'");
        }

        if (x is null && y is null)
            return new InputResult(false, "scroll_to requires x/y or selector");
        return ScrollToOffset(x ?? _scroll.Offset.X, y ?? _scroll.Offset.Y, "scrolled");
    }

    /// <summary>Presses a key for page automation. Text controls get editing
    /// behavior. Page keys scroll the outer viewport. Tab moves DOM focus.</summary>
    public InputResult PressKey(string key, bool shift, bool ctrl, bool alt, bool meta)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        var normalized = NormalizeKey(key);
        if (normalized.Length == 0) return new InputResult(false, "key is empty");

        if (normalized == "Tab")
            return FocusAdjacent(shift ? -1 : 1);

        if (_focusedInput is not null)
            return PressFocusedInputKey(normalized, shift, ctrl, alt, meta);

        if (_currentPage.Document.FocusedElement is { } focused)
        {
            var down = MakeKeyboardEvent("keydown", normalized, shift, ctrl, alt, meta);
            var mutated = DispatchDom(focused, down);
            var detail = $"pressed {normalized} on <{focused.LocalName}>";
            if (normalized == "Enter" && !down.DefaultPrevented && BoxForElement(focused) is { } loc)
            {
                var doc = (loc.X + loc.Box.Frame.Width / 2, loc.Y + loc.Box.Frame.Height / 2);
                detail = PerformClick(doc).Detail;
            }

            mutated |= DispatchDom(focused, MakeKeyboardEvent("keyup", normalized, shift, ctrl, alt, meta));
            if (mutated) RefreshLiveLayout();
            return new InputResult(true, detail);
        }

        return normalized switch
        {
            "PageDown" => ScrollBy(0, ViewportHeight() * 0.9),
            "PageUp" => ScrollBy(0, -ViewportHeight() * 0.9),
            "Home" => ScrollToOffset(_scroll.Offset.X, 0, "scrolled to top"),
            "End" => ScrollToOffset(_scroll.Offset.X, double.PositiveInfinity, "scrolled to bottom"),
            "ArrowDown" => ScrollBy(0, 40),
            "ArrowUp" => ScrollBy(0, -40),
            "ArrowRight" => ScrollBy(40, 0),
            "ArrowLeft" => ScrollBy(-40, 0),
            "Escape" => new InputResult(true, "pressed Escape"),
            _ => new InputResult(true, $"pressed {normalized}"),
        };
    }

    /// <summary>Types literal text into the currently focused text field (focus one
    /// first via <see cref="ClickAt"/>). Control characters are dropped; the text
    /// is inserted at the caret and a DOM <c>input</c> event fires (so
    /// search-as-you-type / form handlers run). When <paramref name="submit"/> is
    /// set, Enter is pressed afterward to submit the owning form. Fails when no
    /// page is loaded or no text field is focused.</summary>
    public InputResult TypeText(string text, bool submit = false)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        if (_focusedInput is null)
            return new InputResult(false, "no focused text field — click an input first");

        var clean = new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray());
        if (clean.Length > 0)
        {
            var val = CurrentValue();
            var idx = Math.Clamp(_caretIndex, 0, val.Length);
            CommitValue(val.Insert(idx, clean), idx + clean.Length);
        }

        if (submit)
        {
            var fired = PressEnterOnFocused();
            return new InputResult(true,
                $"typed \"{clean}\"; submit {(fired ? "fired" : "no-op")}; value=\"{CurrentValue()}\"");
        }

        return new InputResult(true, clean.Length == 0
            ? "nothing to type (text was empty or control-only)"
            : $"typed \"{clean}\"; value=\"{CurrentValue()}\"");
    }

    // ---- MCP element-targeting tools ----------------

    private readonly List<Control> _highlightOverlays = new();

    /// <summary>
    /// Draws a translucent highlight box over every element matching
    /// <paramref name="selector"/> (a CSS selector). Non-destructive — clears the
    /// previous highlight first and on navigation. <paramref name="color"/> is an
    /// optional CSS colour; an opaque colour is made translucent so the element
    /// shows through.
    /// </summary>
    public InputResult HighlightElement(string selector, string? color)
    {
        var (els, error) = QueryAll(selector);
        if (error is not null) return new InputResult(false, error);
        ClearHighlightOverlays();
        if (els.Count == 0) return new InputResult(true, $"no elements matched '{selector}'");

        var brush = HighlightBrush(color);
        var count = 0;
        foreach (var el in els)
        {
            if (FindBoxAbs(_currentPage!.Root, 0, 0, b => ReferenceEquals(b.Element, el)) is not { } loc) continue;
            var overlay = MakeOverlay(brush, loc.X, loc.Y, loc.Box.Frame.Width, loc.Box.Frame.Height);
            _pageCanvas.Children.Add(overlay);
            _highlightOverlays.Add(overlay);
            count++;
        }

        RefreshOverlays();
        return new InputResult(true, $"highlighted {count} of {els.Count} element(s) matching '{selector}'");
    }

    /// <summary>
    /// Selects the first element matching <paramref name="selector"/>: draws a
    /// selection box over it and makes its text the copyable selection (⌘C).
    /// </summary>
    public InputResult SelectBySelector(string selector)
    {
        var (els, error) = QueryAll(selector);
        if (error is not null) return new InputResult(false, error);
        if (els.Count == 0) return new InputResult(true, $"no elements matched '{selector}'");

        var el = els[0];
        if (FindBoxAbs(_currentPage!.Root, 0, 0, b => ReferenceEquals(b.Element, el)) is not { } loc)
            return new InputResult(false, $"matched <{el.LocalName}> has no rendered box to select");

        ClearSelection();
        var brush = new SolidColorBrush(AvColor.FromArgb(80, 51, 144, 255));
        var overlay = MakeOverlay(brush, loc.X, loc.Y, loc.Box.Frame.Width, loc.Box.Frame.Height);
        _pageCanvas.Children.Add(overlay);
        _selectionOverlays.Add(overlay);
        _selectionText = el.TextContent ?? string.Empty;
        RefreshOverlays();
        return new InputResult(true, $"selected <{el.LocalName}> ({_selectionText.Length} chars) — ⌘C to copy");
    }

    /// <summary>
    /// Focuses the first element matching <paramref name="selector"/>: a text
    /// field gets keyboard focus + caret (then <c>browser_type</c> works); any
    /// other element gets DOM focus so <c>:focus</c> styling and JS focus handlers
    /// run.
    /// </summary>
    public InputResult FocusBySelector(string selector)
    {
        var (els, error) = QueryAll(selector);
        if (error is not null) return new InputResult(false, error);
        if (els.Count == 0) return new InputResult(true, $"no elements matched '{selector}'");

        var el = els[0];
        if (HtmlFormControls.IsTextControl(el) && !el.HasAttribute("disabled")
                                               && FindBoxAbs(_currentPage!.Root, 0, 0,
                                                   b => ReferenceEquals(b.Element, el)) is { } loc)
        {
            // Focus through the input path so the caret + DOM events are set up;
            // aim the synthetic click at the field centre.
            var cx = loc.X + loc.Box.Frame.Width / 2;
            var cy = loc.Y + loc.Box.Frame.Height / 2;
            FocusInput(el, (cx, cy));
            return new InputResult(true, $"focused text field <{el.LocalName}> matching '{selector}'");
        }

        // Any other element: clear input focus, set document focus, fire events,
        // and relayout so :focus styling applies.
        BlurInput();
        _currentPage!.Document.FocusedElement = el;
        DispatchDom(el, new FocusEvent("focus"));
        DispatchDom(el, new FocusEvent("focusin", new EventInit(Bubbles: true)));
        RefreshFocusedLayout();
        return new InputResult(true, $"focused <{el.LocalName}> matching '{selector}'");
    }

    /// <summary>Reports selector matches with document-space bounds and optional
    /// text / serialized HTML.</summary>
    public InputResult QuerySelector(string selector, bool includeText, bool includeHtml, int limit)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        var (els, error) = QueryAll(selector);
        if (error is not null) return new InputResult(false, error);
        var capped = Math.Clamp(limit <= 0 ? 20 : limit, 1, 100);
        if (els.Count == 0) return new InputResult(true, $"matches: 0 for '{selector}'");

        var sb = new StringBuilder();
        sb.Append("matches: ").Append(els.Count).Append(" for '").Append(selector).AppendLine("'");
        var shown = 0;
        foreach (var el in els)
        {
            if (shown >= capped) break;
            sb.Append('#').Append(shown + 1).Append(' ').Append(DescribeElement(el));
            if (BoxForElement(el) is { } loc)
            {
                sb.Append(" bounds=(")
                    .Append(loc.X.ToString("0.#")).Append(',')
                    .Append(loc.Y.ToString("0.#")).Append(',')
                    .Append(loc.Box.Frame.Width.ToString("0.#")).Append(',')
                    .Append(loc.Box.Frame.Height.ToString("0.#")).Append(')');
            }
            else
            {
                sb.Append(" bounds=(none)");
            }

            if (includeText)
                sb.Append(" text=\"").Append(TrimForTool(el.TextContent, 500)).Append('"');
            if (includeHtml)
                sb.Append(" html=\"").Append(TrimForTool(HtmlSerializer.SerializeNode(el), 1000)).Append('"');
            sb.AppendLine();
            shown++;
        }

        if (els.Count > capped)
            sb.Append("truncated: ").Append(els.Count - capped).AppendLine(" more");
        return new InputResult(true, sb.ToString());
    }

    public int CountSelectorMatches(string selector)
    {
        var (els, error) = QueryAll(selector);
        return error is null ? els.Count : 0;
    }

    public bool PageContainsText(string value)
        => _currentPage?.Document.TextContent.Contains(value, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Finds text, scrolls the next/previous match into view, and flashes it.</summary>
    public InputResult FindText(string query, string direction)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        var q = (query ?? string.Empty).Trim();
        if (q.Length == 0) return new InputResult(false, "query is empty");
        if (_findIndex.Count == 0) return new InputResult(true, $"no text fragments to search for '{q}'");

        var matches = new List<int>();
        for (var i = 0; i < _findIndex.Count; i++)
            if (_findIndex[i].Text.Contains(q, StringComparison.OrdinalIgnoreCase))
                matches.Add(i);
        if (matches.Count == 0) return new InputResult(true, $"no matches for '{q}'");

        var backwards = direction.Equals("previous", StringComparison.OrdinalIgnoreCase)
                        || direction.Equals("prev", StringComparison.OrdinalIgnoreCase);
        int matchIndex;
        if (backwards)
        {
            var cursor = _findCursor <= 0 ? _findIndex.Count : _findCursor - 1;
            matchIndex = matches[^1];
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i] < cursor)
                {
                    matchIndex = matches[i];
                    break;
                }
            }
        }
        else
        {
            matchIndex = matches[0];
            foreach (var candidate in matches)
            {
                if (candidate >= _findCursor)
                {
                    matchIndex = candidate;
                    break;
                }
            }
        }

        _findCursor = matchIndex + 1;
        ClearFindHighlight();
        FlashFindMatch(matchIndex);
        var ordinal = matches.IndexOf(matchIndex) + 1;
        return new InputResult(true, $"found '{q}' match {ordinal}/{matches.Count}");
    }

    /// <summary>Clipboard helper for automation. Supports copy, paste, read, and
    /// readSelection.</summary>
    public async Task<InputResult> ClipboardAsync(string action, string? text)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        var mode = (action ?? string.Empty).Trim().ToLowerInvariant();
        var tl = TopLevel.GetTopLevel(this);
        var clip = tl?.Clipboard;

        switch (mode)
        {
            case "copy":
                if (string.IsNullOrEmpty(_selectionText))
                    return new InputResult(false, "no selected text to copy");
                if (clip is null)
                    return new InputResult(false, "clipboard is not available");
                await clip.SetTextAsync(_selectionText);
                return new InputResult(true, $"copied {_selectionText.Length} chars");

            case "paste":
                var paste = text;
                if (paste is null && clip is not null)
                    paste = await clip.TryGetTextAsync();
                if (paste is null && clip is null)
                    return new InputResult(false, "clipboard is not available and no paste text was provided");
                if (paste is null) paste = string.Empty;
                return TypeText(paste);

            case "read":
                if (clip is null) return new InputResult(false, "clipboard is not available");
                return new InputResult(true, await clip.TryGetTextAsync() ?? string.Empty);

            case "readselection":
            case "read_selection":
                return new InputResult(true, _selectionText);

            default:
                return new InputResult(false, "action must be copy, paste, read, or readSelection");
        }
    }
    //
    // /// <summary>Captures the current visible viewport to a PNG.</summary>
    // public InputResult CaptureViewportToPng(string path)
    // {
    //     if (_currentPage is null) return new InputResult(false, "no page is loaded");
    //     var full = Path.GetFullPath(string.IsNullOrWhiteSpace(path) ? "starling-viewport.png" : path);
    //     var rect = CurrentViewportRect();
    //     var (styleOverride, scrollLookup) = BuildRenderInputs();
    //     var pageVersion = _animating
    //         ? unchecked(_currentPage.DisplayListVersion + (int)_animClockMs)
    //         : _currentPage.DisplayListVersion;
    //
    //     using var rendered = _renderSession.Render(new PageFrameRequest
    //     {
    //         Root = _currentPage.Root,
    //         Scale = (float)_currentScale,
    //         StyleOverride = styleOverride,
    //         Images = _currentPage.ImageResolver,
    //         Viewport = rect,
    //         PageVersion = pageVersion,
    //         ScrollOffsets = scrollLookup,
    //     }, CpuBitmapFrameTarget.Instance);
    //     var cpuFrame = rendered.Bitmap
    //         ?? throw new InvalidOperationException("Bitmap render target did not produce a CPU frame.");
    //     var dir = Path.GetDirectoryName(full);
    //     if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    //     using var image = SixLabors.ImageSharp.Image.LoadPixelData<SixLabors.ImageSharp.PixelFormats.Rgba32>(
    //         cpuFrame.Rgba.Span, cpuFrame.Width, cpuFrame.Height);
    //     SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, full);
    //     return new InputResult(true, $"wrote {full} ({cpuFrame.Width}x{cpuFrame.Height})");
    // }

    /// <summary>
    /// Debug report for the <c>browser_computed_style</c> MCP
    /// tool. For every element matching <paramref name="selector"/>, reports the
    /// EFFECTIVE painted style using the same precedence the painter does: a live
    /// hover override first, then an animation/transition sample, otherwise the
    /// laid-out style. Also reports whether hover or animation is driving it.
    /// Built to diagnose "renders wrong / goes invisible" bugs: it surfaces the
    /// opacity / transform / colour actually on screen and the flag that explains
    /// them (e.g. an unexpected hover override shadowing an animation).
    /// </summary>
    public InputResult InspectComputedStyle(string selector)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        var (els, error) = QueryAll(selector);
        if (error is not null) return new InputResult(false, error);
        if (els.Count == 0) return new InputResult(true, $"no elements matched '{selector}'");

        var page = _currentPage;
        var sb = new System.Text.StringBuilder();
        const int Max = 10;
        var shown = 0;
        foreach (var el in els)
        {
            if (shown >= Max)
            {
                sb.Append($"; … (+{els.Count - Max} more)");
                break;
            }

            var hovered = _hoverOverrides is not null && _hoverOverrides.ContainsKey(el);
            var animating = IsAnimating(el);
            var s = hovered ? _hoverOverrides![el]
                : animating ? page.Style.ComputeWithAnimations(el, _animClockMs)
                : page.Style.Compute(el);

            var color = s.GetColor(PropertyId.Color);
            var bg = s.GetColor(PropertyId.BackgroundColor);
            if (shown > 0) sb.Append("; ");
            sb.Append($"<{el.LocalName}> opacity={s.Get(PropertyId.Opacity)} " +
                      $"transform={s.Get(PropertyId.Transform)} " +
                      $"color=rgba({color.R},{color.G},{color.B},{color.A}) " +
                      $"bg=rgba({bg.R},{bg.G},{bg.B},{bg.A}) " +
                      $"hoverOverride={(hovered ? "yes" : "no")} animating={(animating ? "yes" : "no")}");
            shown++;
        }

        return new InputResult(true, sb.ToString());

        bool IsAnimating(DomElement el)
        {
            foreach (var _ in page.Style.AnimationEngine.ActiveProperties(el)) return true;
            foreach (var _ in page.Style.TransitionEngine.ActiveProperties(el)) return true;
            return false;
        }
    }

    private InputResult ScrollToOffset(double x, double y, string label)
    {
        var pageW = _pageCanvas.Width > 0 ? _pageCanvas.Width : _currentPage?.Root.Frame.Width ?? 0;
        var pageH = _pageCanvas.Height > 0 ? _pageCanvas.Height : _currentPage?.Root.Frame.Height ?? 0;
        var viewport = CurrentViewportSize();
        var viewportW = _scroll.Viewport.Width > 0 ? _scroll.Viewport.Width : viewport.Width;
        var viewportH = _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : viewport.Height;
        var maxX = Math.Max(0, pageW - viewportW);
        var maxY = Math.Max(0, pageH - viewportH);
        if (double.IsPositiveInfinity(y)) y = maxY;
        if (double.IsPositiveInfinity(x)) x = maxX;
        if (!double.IsFinite(x) || !double.IsFinite(y))
            return new InputResult(false, $"scroll target must be finite, got {x}, {y}");

        var nextX = Math.Clamp(x, 0, maxX);
        var nextY = Math.Clamp(y, 0, maxY);
        _scroll.Offset = new Vector(nextX, nextY);
        RenderViewportRegion();
        return new InputResult(true, $"{label} x={nextX:0.#}, y={nextY:0.#} (max x={maxX:0.#}, y={maxY:0.#})");
    }

    private double ViewportHeight()
        => _scroll.Viewport.Height > 0 ? _scroll.Viewport.Height : CurrentViewportSize().Height;

    private (LayoutBox Box, double X, double Y)? BoxForElement(DomElement el)
        => _currentPage is null
            ? null
            : FindBoxAbs(_currentPage.Root, 0, 0, b => ReferenceEquals(b.Element, el));

    private InputResult FocusAdjacent(int step)
    {
        if (_currentPage is null) return new InputResult(false, "no page is loaded");
        var focusable = FocusableElements().ToArray();
        if (focusable.Length == 0) return new InputResult(true, "no focusable elements");

        var current = _focusedInput ?? _currentPage.Document.FocusedElement;
        var idx = current is null ? -1 : Array.IndexOf(focusable, current);
        var next = idx < 0
            ? (step > 0 ? 0 : focusable.Length - 1)
            : (idx + step + focusable.Length) % focusable.Length;
        FocusElement(focusable[next]);
        return new InputResult(true, $"focused {DescribeElement(focusable[next])}");
    }

    private IEnumerable<DomElement> FocusableElements()
    {
        if (_currentPage is null) yield break;
        foreach (var el in _currentPage.Document.DescendantElements())
        {
            if (el.HasAttribute("disabled")) continue;
            if (HtmlFormControls.IsTextControl(el)
                || el.LocalName is "button" or "select" or "textarea"
                || (el.LocalName == "input" && (el.GetAttribute("type") ?? "text") != "hidden")
                || (el.LocalName == "a" && !string.IsNullOrEmpty(el.GetAttribute("href")))
                || el.HasAttribute("tabindex"))
            {
                yield return el;
            }
        }
    }

    private void FocusElement(DomElement el)
    {
        if (_currentPage is null) return;
        if (HtmlFormControls.IsTextControl(el) && !el.HasAttribute("disabled") && BoxForElement(el) is { } loc)
        {
            FocusInput(el, (loc.X + loc.Box.Frame.Width / 2, loc.Y + loc.Box.Frame.Height / 2));
            return;
        }

        BlurInput();
        _currentPage.Document.FocusedElement = el;
        DispatchDom(el, new FocusEvent("focus"));
        DispatchDom(el, new FocusEvent("focusin", new EventInit(Bubbles: true)));
        RefreshFocusedLayout();
    }

    private InputResult PressFocusedInputKey(string key, bool shift, bool ctrl, bool alt, bool meta)
    {
        var target = _focusedInput!;
        if (key == "Enter")
        {
            var fired = PressEnterOnFocused();
            return new InputResult(true, $"pressed Enter; submit {(fired ? "fired" : "no-op")}");
        }

        var down = MakeKeyboardEvent("keydown", key, shift, ctrl, alt, meta);
        var mutated = DispatchDom(target, down);
        if (!down.DefaultPrevented && !ctrl && !alt && !meta)
        {
            var val = CurrentValue();
            var idx = Math.Clamp(_caretIndex, 0, val.Length);
            switch (key)
            {
                case "Backspace":
                    if (idx > 0) CommitValue(val.Remove(idx - 1, 1), idx - 1);
                    break;
                case "Delete":
                    if (idx < val.Length) CommitValue(val.Remove(idx, 1), idx);
                    break;
                case "ArrowLeft":
                    MoveCaret(idx - 1);
                    break;
                case "ArrowRight":
                    MoveCaret(idx + 1);
                    break;
                case "Home":
                    MoveCaret(0);
                    break;
                case "End":
                    MoveCaret(val.Length);
                    break;
                case "Escape":
                    BlurInput();
                    break;
                case " ":
                    return TypeText(" ");
            }
        }

        mutated |= DispatchDom(target, MakeKeyboardEvent("keyup", key, shift, ctrl, alt, meta));
        if (mutated) RefreshLiveLayout();
        return new InputResult(true, $"pressed {key}");
    }

    private static KeyboardEvent MakeKeyboardEvent(string type, string key, bool shift, bool ctrl, bool alt, bool meta)
        => new(type, new EventInit(Bubbles: true, Cancelable: true))
        {
            Key = key,
            Code = key == " " ? "Space" : key,
            CtrlKey = ctrl,
            ShiftKey = shift,
            AltKey = alt,
            MetaKey = meta,
        };

    private static string NormalizeKey(string key)
    {
        var trimmed = (key ?? string.Empty).Trim();
        if (trimmed.Length == 0) return string.Empty;
        return trimmed.ToLowerInvariant() switch
        {
            "esc" or "escape" => "Escape",
            "enter" or "return" => "Enter",
            "tab" => "Tab",
            "backspace" or "back" => "Backspace",
            "delete" or "del" => "Delete",
            "left" or "arrowleft" => "ArrowLeft",
            "right" or "arrowright" => "ArrowRight",
            "up" or "arrowup" => "ArrowUp",
            "down" or "arrowdown" => "ArrowDown",
            "home" => "Home",
            "end" => "End",
            "pagedown" or "page_down" => "PageDown",
            "pageup" or "page_up" => "PageUp",
            "space" or "spacebar" => " ",
            _ when trimmed.Length == 1 => trimmed,
            _ => trimmed,
        };
    }

    private static string DescribeElement(DomElement el)
    {
        var sb = new StringBuilder();
        sb.Append('<').Append(el.LocalName);
        if (!string.IsNullOrEmpty(el.Id))
            sb.Append('#').Append(el.Id);
        if (el.GetAttribute("class") is { Length: > 0 } cls)
            sb.Append('.').Append(cls.Replace(' ', '.'));
        sb.Append('>');
        return sb.ToString();
    }

    private static string TrimForTool(string text, int max)
    {
        var compact = text.ReplaceLineEndings(" ").Trim();
        while (compact.Contains("  ", StringComparison.Ordinal))
            compact = compact.Replace("  ", " ", StringComparison.Ordinal);
        return compact.Length <= max ? compact : compact[..max] + "...";
    }

    /// <summary>Resolves a CSS selector to the matching elements, in document order.</summary>
    private (List<DomElement> Elements, string? Error) QueryAll(string selector)
    {
        if (_currentPage is null) return (new List<DomElement>(), "no page is loaded");
        if (string.IsNullOrWhiteSpace(selector)) return (new List<DomElement>(), "selector is empty");

        SelectorList list;
        try
        {
            list = SelectorParser.ParseSelectorList(selector);
        }
        catch (Exception ex)
        {
            return (new List<DomElement>(), $"invalid selector '{selector}': {ex.Message}");
        }

        if (list.Selectors.Count == 0) return (new List<DomElement>(), $"invalid selector '{selector}'");

        var matched = new List<DomElement>();
        foreach (var el in _currentPage.Document.DescendantElements())
            if (SelectorMatcher.Matches(list, el))
                matched.Add(el);
        return (matched, null);
    }

    private void ClearHighlightOverlays()
    {
        foreach (var o in _highlightOverlays)
            _pageCanvas.Children.Remove(o);
        _highlightOverlays.Clear();
        RefreshOverlays();
    }

    private static IBrush HighlightBrush(string? color)
    {
        if (!string.IsNullOrWhiteSpace(color) && AvColor.TryParse(color, out var c))
        {
            // An opaque colour is dimmed to a translucent highlight so the element
            // shows through; a colour given with alpha is honoured as-is.
            if (c.A == 255) c = AvColor.FromArgb(110, c.R, c.G, c.B);
            return new SolidColorBrush(c);
        }

        return new SolidColorBrush(AvColor.FromArgb(120, 255, 224, 0)); // translucent yellow
    }

    /// <summary>Dispatches DOM mouse-move events for a synthetic move to
    /// <paramref name="target"/>: <c>mouseout</c>/<c>mouseover</c> across a target
    /// change (with the other element as relatedTarget), then <c>mousemove</c>.
    /// Client coordinates are viewport-relative (document minus scroll), matching
    /// <see cref="DispatchClick"/>. Tracks <see cref="_mouseTarget"/> and re-lays
    /// out if a handler mutated the DOM. A no-op without a live JS context.</summary>
    private void DispatchMouseMove((double X, double Y) doc, DomElement? target)
    {
        if (_currentPage?.Scripting is null)
        {
            _mouseTarget = target;
            return;
        }

        var clientX = doc.X - _scroll.Offset.X;
        var clientY = doc.Y - _scroll.Offset.Y;

        MouseEvent Make(string type, DomElement? related) =>
            new(type, new EventInit(Bubbles: true, Cancelable: true))
            {
                Button = 0,
                ClientX = clientX,
                ClientY = clientY,
                RelatedTarget = related,
            };

        var mutated = false;
        if (!ReferenceEquals(target, _mouseTarget))
        {
            if (_mouseTarget is { } prev) mutated |= DispatchDom(prev, Make("mouseout", target));
            if (target is { } cur) mutated |= DispatchDom(cur, Make("mouseover", _mouseTarget));
            _mouseTarget = target;
        }

        if (target is not null) mutated |= DispatchDom(target, Make("mousemove", null));
        if (mutated) RefreshLiveLayout();
    }

    /// <summary>Synthesizes an Enter keypress on the focused field: dispatches
    /// <c>keydown</c>, submits the owning form (for a text <c>&lt;input&gt;</c>,
    /// implicit submission), then <c>keyup</c> — mirroring the Enter branch of
    /// <see cref="OnPageKeyDown"/>. Returns whether any of it mutated the DOM.</summary>
    private bool PressEnterOnFocused()
    {
        if (_focusedInput is null) return false;
        var target = _focusedInput;
        var down = new KeyboardEvent("keydown", new EventInit(Bubbles: true, Cancelable: true))
            { Key = "Enter", Code = "Enter" };
        var mutated = DispatchDom(target, down);
        if (target.LocalName == "input")
        {
            var actioned = false;
            mutated |= SubmitOwningForm(target, ref actioned);
        }

        var up = new KeyboardEvent("keyup", new EventInit(Bubbles: true, Cancelable: true))
            { Key = "Enter", Code = "Enter" };
        mutated |= DispatchDom(target, up);
        if (mutated) RefreshLiveLayout();
        return mutated;
    }

    private static KeyboardEvent MakeKeyboardEvent(string type, KeyEventArgs e) =>
        new(type, new EventInit(Bubbles: true, Cancelable: true))
        {
            Key = DomKeyName(e),
            Code = e.PhysicalKey.ToString(),
            CtrlKey = e.KeyModifiers.HasFlag(KeyModifiers.Control),
            ShiftKey = e.KeyModifiers.HasFlag(KeyModifiers.Shift),
            AltKey = e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            MetaKey = e.KeyModifiers.HasFlag(KeyModifiers.Meta),
        };

    private static string DomKeyName(KeyEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.KeySymbol)) return e.KeySymbol!;
        return e.Key switch
        {
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Escape => "Escape",
            Key.Left => "ArrowLeft",
            Key.Right => "ArrowRight",
            Key.Up => "ArrowUp",
            Key.Down => "ArrowDown",
            Key.Home => "Home",
            Key.End => "End",
            Key.Space => " ",
            _ => e.Key.ToString(),
        };
    }

    // ---- Cursor --------------------------------------------------------

    private void ApplyCursor(BoxHitTester.HitResult hit)
    {
        var keyword = BoxHitTester.ResolveCursor(hit);
        Cursor = MapCursor(keyword);
    }

    private void ResetCursor() => Cursor = new Cursor(StandardCursorType.Arrow);

    private static Cursor MapCursor(string keyword) => keyword switch
    {
        "pointer" => new Cursor(StandardCursorType.Hand),
        "text" => new Cursor(StandardCursorType.Ibeam),
        "wait" or "progress" => new Cursor(StandardCursorType.Wait),
        "help" => new Cursor(StandardCursorType.Help),
        "crosshair" => new Cursor(StandardCursorType.Cross),
        "move" => new Cursor(StandardCursorType.SizeAll),
        "not-allowed" or "no-drop" => new Cursor(StandardCursorType.No),
        "ew-resize" or "col-resize" => new Cursor(StandardCursorType.SizeWestEast),
        "ns-resize" or "row-resize" => new Cursor(StandardCursorType.SizeNorthSouth),
        "grab" => new Cursor(StandardCursorType.Hand),
        "grabbing" => new Cursor(StandardCursorType.DragMove),
        _ => new Cursor(StandardCursorType.Arrow),
    };

    // ---- Hover re-render ----------------------------------------------

    /// <summary>
    /// Re-cascades the hovered anchor + its DOM descendants with a
    /// <c>:hover</c>-active context and re-rasterizes the page through the
    /// paint pipeline with those styles substituted in via the
    /// DisplayListBuilder's per-box override hook. Layout is reused —
    /// hover changes that affect geometry (rare in real CSS) won't reflow.
    /// </summary>
    private void ApplyHoverState()
    {
        if (_currentPage is null) return;

        var style = _currentPage.Style;
        var animationsWired = _prepareAnimationFrame is not null && _hasActiveAnimations is not null;

        // Advance the animation/transition clock to "now" BEFORE re-cascading, so
        // a transition the hover change triggers starts at the current frame time.
        // The TransitionEngine stamps StartMs from its own clock (set by Tick), so
        // without this a freshly-registered transition would date from the last
        // frame and jump ahead on the next tick.
        long nowMs = 0;
        if (animationsWired)
        {
            EnsureAnimationClock();
            nowMs = _liveStopwatch.ElapsedMilliseconds;
            _prepareAnimationFrame!(_currentPage, nowMs);
        }

        // Build the new hover scope (hovered subtree + ancestor chain). When the
        // animation hooks are wired this runs through ComputeWithAnimations, which
        // registers + samples any forward transition as a side effect.
        var newOverrides = BuildHoverOverrides(_hoverElement, nowMs);
        var newScope = newOverrides is null ? null : new HashSet<DomElement>(newOverrides.Keys);

        // Elements leaving the scope revert: re-cascade each once with the new
        // hover context so the compositor sees the hover->base change and starts
        // the reverse transition. They then animate out via the live loop's
        // AnimatedStyle path (they carry an active transition, not a hover override).
        if (animationsWired && _hoverScope.Count > 0)
        {
            var newCtx = _hoverElement is null
                ? null
                : new SelectorMatchContext { HoveredElement = _hoverElement };
            foreach (var el in _hoverScope)
            {
                if (newScope is not null && newScope.Contains(el)) continue;
                style.ComputeWithAnimations(el, nowMs, newCtx);
            }
        }

        _hoverScope = newScope ?? new HashSet<DomElement>();

        var transitionsActive = animationsWired && _hasActiveAnimations!(_currentPage);

        // Fast path for non-:hover pages (and settled hovers that change nothing
        // paint-relevant): drop overrides without repainting — unless a transition
        // is mid-flight, in which case the live loop must keep driving frames.
        if (!transitionsActive && !OverridesDifferFromBaseline(newOverrides))
        {
            // If we were previously showing hover pixels, the display list just
            // reverted to the baseline: drop the hover-painted cache and repaint.
            var hadOverrides = _hoverOverrides is not null;
            _hoverOverrides = null;
            if (hadOverrides)
            {
                RenderViewportRegion();
            }

            return;
        }

        // The override set changes the display list without changing the page
        // version, so the picture cache must be dropped or it would blit stale
        // (non-hover) pixels. Wholesale invalidation keeps the cache simple:
        // any display-list change is a full miss.
        _hoverOverrides = newOverrides;
        // While a hover transition runs, hand off to the live loop: it ticks the
        // clock, re-samples the overrides, and repaints each frame until the
        // transition settles. _animating lets this immediate paint already overlay
        // the reverse-transitioning (leaving) elements.
        if (transitionsActive)
        {
            _animClockMs = nowMs;
            _animating = true;
            BindLiveScripting();
        }

        RenderViewportRegion();
    }

    /// <summary>Start the live-loop stopwatch if it isn't already running, so a
    /// hover-triggered transition has a monotonic clock even on a page with no JS
    /// context. A bound scripting context already keeps it running.</summary>
    private void EnsureAnimationClock()
    {
        if (!_liveStopwatch.IsRunning) _liveStopwatch.Restart();
    }

    /// <summary>Test hook: true when <paramref name="el"/> is in the current hover
    /// override set. Used to assert hover only overrides the genuinely
    /// :hover-affected elements, not the whole hovered subtree.</summary>
    internal bool HoverScopeContainsForTest(DomElement el)
        => _hoverOverrides is not null && _hoverOverrides.ContainsKey(el);

    /// <summary>Test hook: number of elements in the current hover override set.</summary>
    internal int HoverOverrideCountForTest => _hoverOverrides?.Count ?? 0;

    /// <summary>Test hook: drive the CSS <c>:hover</c> re-cascade for
    /// <paramref name="el"/> directly (no hit-test), exactly as a real pointer move
    /// onto it would — so a test can assert which elements the hover overrides.</summary>
    internal void HoverElementForTest(DomElement el)
    {
        _hoverElement = el;
        ApplyHoverState();
    }

    /// <summary>
    /// Re-cascades the hovered element's subtree plus its ancestor chain under a
    /// <c>:hover</c> context, returning the per-element computed styles to overlay
    /// at paint time. When the animation hooks are wired the cascade runs through
    /// <see cref="StyleEngine.ComputeWithAnimations"/> at <paramref name="nowMs"/>,
    /// so a state change registers and samples a CSS transition; otherwise it is a
    /// plain static cascade. Returns null when nothing is hovered.
    /// </summary>
    private Dictionary<DomElement, ComputedStyle>? BuildHoverOverrides(DomElement? hovered, long nowMs)
    {
        if (hovered is null || _currentPage is null) return null;
        var style = _currentPage.Style;
        var ctx = new SelectorMatchContext { HoveredElement = hovered };
        var animate = _prepareAnimationFrame is not null;
        var result = new Dictionary<DomElement, ComputedStyle>();

        // The hovered element's subtree covers `.x:hover` (self) and
        // `.x:hover .descendant` rules.
        Recurse(hovered);
        // Hovering an element also hovers its ancestors, so re-cascade the chain
        // for `.ancestor:hover` self-rules. (Rules anchored on an ancestor that
        // target a non-hovered sibling subtree are not recomputed — a bounded
        // approximation that covers the common cases.)
        for (var n = hovered.ParentNode; n is not null; n = n.ParentNode)
            if (n is DomElement p && Compose(p) is { } ps)
                result[p] = ps;

        // Only elements whose :hover cascade actually changes a paint-relevant
        // property are overridden. Without this, hovering a big container (or the
        // body while the pointer sweeps) re-cascaded and overrode its WHOLE
        // subtree on every move — shadowing each element's AnimatedStyle with the
        // hover sample, so animated/just-styled content flashed to its base state
        // (invisible) until the pointer stopped. Pruning to the genuinely
        // :hover-affected elements lets everyone else keep their laid-out /
        // animated style.
        return result.Count == 0 ? null : result;

        // The override style for `el` when :hover changes its paint, else null.
        // Relevance is judged from the STATIC cascade (with vs. without the :hover
        // context) so a running animation's per-frame sample doesn't make every
        // animating element look "changed"; the stored style still carries the
        // animation sample via ComputeWithAnimations.
        ComputedStyle? Compose(DomElement el)
        {
            var hoverStatic = style.Compute(el, ctx);
            // Compare the element's cascade with vs. without :hover; prune it when
            // nothing the painter emits changed (SamePaintProperties value-compares,
            // so identical cascades from two runs match).
            if (SamePaintProperties(style.Compute(el), hoverStatic)) return null;
            return animate ? style.ComputeWithAnimations(el, nowMs, ctx) : hoverStatic;
        }

        void Recurse(DomElement el)
        {
            if (Compose(el) is { } s) result[el] = s;
            for (var child = el.FirstChild; child is not null; child = child.NextSibling)
                if (child is DomElement c)
                    Recurse(c);
        }
    }

    /// <summary>
    /// Returns true if any computed style in <paramref name="overrides"/>
    /// differs from the element's layout-time style for the properties the
    /// paint pipeline actually emits. Comparing the full ComputedStyle would
    /// trigger re-renders on identity changes (CssValue boxing), so we sample
    /// the handful of paint-affecting properties.
    /// </summary>
    private bool OverridesDifferFromBaseline(Dictionary<DomElement, ComputedStyle>? overrides)
    {
        if (_currentPage is null || overrides is null || overrides.Count == 0) return false;
        var baseline = _currentPage.Style;
        foreach (var (element, hovered) in overrides)
        {
            var original = baseline.Compute(element);
            if (!SamePaintProperties(original, hovered)) return true;
        }

        return false;
    }

    private static bool SamePaintProperties(ComputedStyle a, ComputedStyle b)
    {
        if (a.GetColor(PropertyId.Color) != b.GetColor(PropertyId.Color)) return false;
        if (a.GetColor(PropertyId.BackgroundColor) != b.GetColor(PropertyId.BackgroundColor)) return false;
        if (a.GetColor(PropertyId.BorderTopColor) != b.GetColor(PropertyId.BorderTopColor)) return false;
        if (a.GetColor(PropertyId.BorderRightColor) != b.GetColor(PropertyId.BorderRightColor)) return false;
        if (a.GetColor(PropertyId.BorderBottomColor) != b.GetColor(PropertyId.BorderBottomColor)) return false;
        if (a.GetColor(PropertyId.BorderLeftColor) != b.GetColor(PropertyId.BorderLeftColor)) return false;
        // Value-compare the decoration: two independent cascade runs (with vs.
        // without :hover) produce non-identical CssKeyword instances for the same
        // keyword, so ReferenceEquals would always report a spurious change — which
        // pulled every element into the hover override set (the invisibility bug).
        if (!Equals(a.Get(PropertyId.TextDecoration), b.Get(PropertyId.TextDecoration))) return false;
        // Transform / opacity are paint-on-self too (the painter reads them off
        // the override style), so a hover that only moves or fades an element —
        // the common case for the demo's tiles — must still repaint.
        if (!Equals(a.Get(PropertyId.Transform), b.Get(PropertyId.Transform))) return false;
        if (!Equals(a.Get(PropertyId.Opacity), b.Get(PropertyId.Opacity))) return false;
        return true;
    }

    /// <summary>
    /// Renders the <paramref name="viewport"/> region of <c>_currentPage.Root</c>
    /// through the paint pipeline, threading the current hover overrides into
    /// <see cref="DisplayListBuilder"/> so <c>a:hover</c> rules repaint without
    /// re-layout. Called on page load, on scroll, and on every hover transition
    /// that changes paint-affecting styles.
    /// </summary>
    // private void RenderPageBitmap(Starling.Layout.Rect viewport)
    // {
    //     if (_currentPage is null) return;
    //
    //     // A finished navigation can leave its stopped Activity as the UI thread's
    //     // ambient Activity.Current (async span leak — see RunNavigation). A
    //     // standalone render (hover / resize / scroll) must not pile into that prior
    //     // navigation's trace, so detach from a leaked, already-stopped span. Renders
    //     // that run inside a live navigation keep their (non-stopped) parent and nest.
    //     if (Activity.Current is { IsStopped: true })
    //         Activity.Current = null;
    //
    //     var animate = _animating && _currentPage is not null;
    //     var (styleOverride, scrollLookup) = BuildRenderInputs();
    //
    //     // While animating, the painted pixels change every frame even though the
    //     // box tree (DisplayListVersion) is unchanged, so vary the picture-cache
    //     // key by the animation clock to force a fresh raster each frame.
    //     // On live animation frames with no per-container scroll offsets, use the
    //     // compositor layer tree. Promoted layers keep content-keyed caches, while
    //     // transform and opacity are applied at composite time. Pages with element
    //     // scroll offsets fall back to the flat path because RenderViaLayerTree does
    //     // not thread those offsets yet.
    //     var useLayerTree = scrollLookup is null && _animating;
    //     RenderFrame rendered;
    //     using (_diag.Span("gui", "render"))
    //     {
    //         var pageVersion = animate
    //             ? unchecked(_currentPage!.DisplayListVersion + (int)_animClockMs)
    //             : _currentPage!.DisplayListVersion;
    //         rendered = _renderSession.Render(new PageFrameRequest
    //         {
    //             Root = _currentPage!.Root,
    //             Scale = (float)_currentScale,
    //             StyleOverride = styleOverride,
    //             Images = _currentPage.ImageResolver,
    //             Viewport = viewport,
    //             PageVersion = pageVersion,
    //             IsAnimatingLayerRoot = IsElementAnimatingLayerRoot,
    //             ScrollOffsets = scrollLookup,
    //             UseLayerTree = useLayerTree,
    //         }, CpuBitmapFrameTarget.Instance);
    //     }
    //     var cpuFrame = rendered.Bitmap
    //         ?? throw new InvalidOperationException("Bitmap render target did not produce a CPU frame.");
    //     WriteableBitmap bmp;
    //     using (rendered)
    //         bmp = BitmapBridge.ToWriteableBitmap(cpuFrame, _currentScale);
    //
    //     (_pageImage.Source as IDisposable)?.Dispose();
    //     _pageImage.Source = bmp;
    //     RecordPresentedFrame();
    // }

    /// <summary>Counts a frame that just reached the screen and, once per second,
    /// emits the live frame rate as a metric and a structured log line. The log
    /// line is what surfaces in the Aspire structured-logs query; the
    /// "gui.frames.presented" counter graphs as a rate in the dashboard.</summary>
    private void RecordPresentedFrame()
    {
        var now = _frameClock.ElapsedMilliseconds;
        _diag.Counter("gui.frames.presented", 1);

        // A gap larger than a couple of frames means the loop went idle (no
        // animation / no script). Restart the averaging window so the resumed
        // frame is not divided by the idle seconds.
        if (now - _fpsLastFrameMs > FpsIdleResetMs)
        {
            _fpsWindowStartMs = now;
            _fpsFrames = 0;
        }

        _fpsLastFrameMs = now;
        _fpsFrames++;

        var elapsed = now - _fpsWindowStartMs;
        if (elapsed >= FpsWindowMs)
        {
            var fps = _fpsFrames * 1000.0 / elapsed;
            _diag.Gauge("gui.fps", fps);
            _diag.Log(DiagLevel.Info, "gui", $"fps {fps:F1} ({_fpsFrames} frames / {elapsed} ms)");
            _fpsWindowStartMs = now;
            _fpsFrames = 0;
        }
    }

    /// <summary>
    /// Maps a paint-time box back to its hover override. Boxes with their own
    /// <see cref="LayoutBox.Element"/> match directly; anonymous text boxes
    /// inherit by walking up the box-parent chain to find the nearest box
    /// whose element has an override (text inherits color from its element
    /// ancestor's cascade output).
    /// </summary>
    private static ComputedStyle? ResolveOverride(LayoutBox box, Dictionary<DomElement, ComputedStyle> overrides)
    {
        if (box.Element is { } el && overrides.TryGetValue(el, out var direct))
            return direct;
        // TextBox / anonymous block: walk up box parents looking for a
        // box whose element appears in the override map.
        for (var p = box.Parent; p is not null; p = p.Parent)
        {
            if (p.Element is { } pel && overrides.TryGetValue(pel, out var inherited))
                return inherited;
        }

        return null;
    }

    /// <summary>
    /// Per-box animated style overlay for the live animation loop. Returns the
    /// element's composed (static + animation/transition sampled) style when it
    /// has any animated property in flight; otherwise null so the box keeps its
    /// laid-out style. Boxes without their own element (anonymous/text) are left
    /// to their static style — animations on paint-on-self properties (transform,
    /// opacity, background, border, color of the element itself) render; color
    /// inherited into descendant text boxes is a follow-up.
    /// </summary>
    /// <summary>
    /// Per-frame layer-promotion predicate. A box whose element has any active
    /// animation or transition, or whose subtree a script mutated in the last few
    /// frames, becomes its own compositor layer even with no static
    /// <see cref="LayerHint"/>. Composite-time transform and opacity keep the
    /// slice cacheable. Any other changed paint property re-rasters just this
    /// box's small slice while the base layer stays cached. Boxes with no element
    /// (anonymous/text) are never promoted here.
    /// </summary>
    private bool IsElementAnimatingLayerRoot(LayoutBox box)
    {
        if (_currentPage is not { } page || box.Element is not { } el) return false;
        // A subtree a script mutated in the last few frames is promoted to its own
        // isolated layer, so its repaint does not re-raster the base layer
        // (the base slice excludes it, so the base content hash stays stable and
        // serves from cache). Light hysteresis (Document.DecayRecentMutations)
        // keeps it promoted briefly so promotion does not churn frame to frame.
        if (page.Document.WasRecentlyMutated(el)) return true;
        foreach (var _ in page.Style.AnimationEngine.ActiveProperties(el)) return true;
        foreach (var _ in page.Style.TransitionEngine.ActiveProperties(el)) return true;
        return false;
    }

    private ComputedStyle? AnimatedStyle(LaidOutPage page, LayoutBox box)
    {
        if (box.Element is not { } el) return null;
        var hasAnim = false;
        foreach (var _ in page.Style.AnimationEngine.ActiveProperties(el))
        {
            hasAnim = true;
            break;
        }

        if (!hasAnim)
            foreach (var _ in page.Style.TransitionEngine.ActiveProperties(el))
            {
                hasAnim = true;
                break;
            }

        return hasAnim ? page.Style.ComputeWithAnimations(el, _animClockMs) : null;
    }

    // ---- Selection -----------------------------------------------------

    private void UpdateSelection((double X, double Y) anchor, (double X, double Y) cursor)
    {
        ClearSelectionOverlays();
        if (_fragments.Count == 0) return;

        var anchorCaret = SelectionModel.CaretFromPoint(_fragments, anchor.X, anchor.Y);
        var cursorCaret = SelectionModel.CaretFromPoint(_fragments, cursor.X, cursor.Y);
        var range = SelectionModel.Order(anchorCaret, cursorCaret);
        if (range.IsEmpty)
        {
            _selectionText = string.Empty;
            return;
        }

        var brush = new SolidColorBrush(AvColor.FromArgb(96, 80, 140, 255));
        foreach (var r in SelectionModel.RectsFor(_fragments, range))
        {
            var overlay = MakeOverlay(brush, r.X, r.Y, r.Width, r.Height);
            _pageCanvas.Children.Add(overlay);
            _selectionOverlays.Add(overlay);
        }

        _selectionText = SelectionModel.TextFor(_fragments, range);
        _onStatus($"Selected {_selectionText.Length} chars — ⌘C to copy", false);
        RefreshOverlays();
    }

    private void ClearSelection()
    {
        ClearSelectionOverlays();
        _selectionText = string.Empty;
        _selectAnchor = null;
    }

    private void ClearSelectionOverlays()
    {
        foreach (var o in _selectionOverlays)
            _pageCanvas.Children.Remove(o);
        _selectionOverlays.Clear();
        RefreshOverlays();
    }

    /// <summary>Copies the current text selection to the clipboard.</summary>
    public async Task CopySelectionAsync()
    {
        if (string.IsNullOrEmpty(_selectionText)) return;
        var tl = TopLevel.GetTopLevel(this);
        var clip = tl?.Clipboard;
        if (clip is null) return;
        await clip.SetTextAsync(_selectionText);
        _onStatus($"Copied {_selectionText.Length} chars to clipboard", false);
    }

    // ---- Find in page --------------------------------------------------

    /// <summary>Reveals the find bar and focuses its entry.</summary>
    public void FocusFind()
    {
        _findBar.IsVisible = true;
        _findEntry.Focus();
        _findEntry.SelectAll();
    }

    /// <summary>Hides the find bar and clears any match-highlight overlay.</summary>
    public void HideFindBar()
    {
        _findBar.IsVisible = false;
        ClearFindHighlight();
    }

    private void OnFindEntryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                FindNext();
                break;
            case Key.Escape:
                e.Handled = true;
                HideFindBar();
                break;
        }
    }

    private void RebuildFindIndex()
    {
        _findIndex.Clear();
        _findCursor = 0;
        foreach (var f in _fragments)
            _findIndex.Add((f.Y, f.Text));
    }

    private void FindNext()
    {
        ClearFindHighlight();
        var query = (_findEntry.Text ?? string.Empty).Trim();
        if (query.Length == 0 || _findIndex.Count == 0) return;

        for (var i = 0; i < _findIndex.Count; i++)
        {
            var idx = (_findCursor + i) % _findIndex.Count;
            var (_, text) = _findIndex[idx];
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _findCursor = idx + 1;
                FlashFindMatch(idx);
                _onStatus($"Find: '{query}' — match {idx + 1}/{_findIndex.Count}", false);
                return;
            }
        }

        _onStatus($"Find: '{query}' — no matches", true);
    }

    private void FlashFindMatch(int idx)
    {
        var f = _fragments[idx];
        var brush = new SolidColorBrush(AvColor.FromArgb(140, 255, 220, 70));
        var overlay = MakeOverlay(brush, f.X, f.Y, f.Width, f.Height);
        _pageCanvas.Children.Add(overlay);
        _findOverlays.Add(overlay);

        var viewport = _scroll.Viewport.Height;
        var targetY = Math.Max(0, f.Y - (viewport / 3));
        _scroll.Offset = new Vector(_scroll.Offset.X, targetY);
        RefreshOverlays();

        // Auto-fade after a short delay so repeated find-next stays readable.
        DispatcherTimer.RunOnce(() =>
        {
            _pageCanvas.Children.Remove(overlay);
            _findOverlays.Remove(overlay);
            RefreshOverlays();
        }, TimeSpan.FromMilliseconds(1200));
    }

    private void ClearFindHighlight()
    {
        foreach (var o in _findOverlays)
            _pageCanvas.Children.Remove(o);
        _findOverlays.Clear();
        RefreshOverlays();
    }

    // ---- Helpers -------------------------------------------------------

    private static Border MakeOverlay(IBrush brush, double x, double y, double w, double h)
    {
        var b = new Border
        {
            Background = brush,
            Width = w,
            Height = h,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        return b;
    }

    private string? ResolveLink(string href)
        => _currentPage is null ? null : LinkResolver.Resolve(href, _currentPage.Url);

    public void Dispose()
    {
        _relayoutTimer.Stop();
        _caretBlinkTimer.Stop();
        _liveTimer.Stop();
        _surfaceTarget?.Dispose();
        _renderSession.Dispose();
        _currentPage?.Dispose();
        // Detach the bitmap before disposing it: a teardown layout pass can
        // otherwise measure the Image against an already-disposed bitmap.
        var pageSource = _pageImage.Source as IDisposable;
        _pageImage.Source = null;
        pageSource?.Dispose();
    }
}

/// <summary>Outcome of a synthetic-input call (<see cref="WebviewPanel.ClickAt"/>,
/// <see cref="WebviewPanel.MoveTo"/>, <see cref="WebviewPanel.TypeText"/>):
/// <paramref name="Ok"/> is false only for precondition failures (no page / no
/// focused field); <paramref name="Detail"/> is a short human-readable summary.</summary>
internal readonly record struct InputResult(bool Ok, string Detail);
