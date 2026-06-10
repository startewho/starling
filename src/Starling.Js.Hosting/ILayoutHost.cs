using Starling.Dom;

// NOTE: this type lives physically in the engine-neutral seam project
// (Starling.Js.Hosting) but keeps its original namespace (Starling.Bindings) so
// both backends and the engine reach it without churn. It depends only on
// Starling.Dom, which the seam already references, so neither JS backend has to
// reference the other's bindings to consult the layout host. See DESIGN.md.
namespace Starling.Bindings;

/// <summary>
/// Pluggable host for layout-readback APIs (<c>getBoundingClientRect</c>,
/// <c>offsetWidth</c>/<c>offsetHeight</c>, <c>getComputedStyle</c>). The
/// engine builds a pre-script layout and injects an implementation through
/// <see cref="Starling.Js.Hosting.ScriptSessionOptions.LayoutHost"/>; the
/// bindings consult it when JS asks for layout dimensions or computed style.
/// When no host is installed the bindings fall through to spec-permitted zeros
/// and empty strings (matches a never-laid-out document).
/// </summary>
/// <remarks>
/// <para><b>Staleness:</b> the host snapshots a single layout pass — DOM
/// mutations performed by JS after the snapshot are not reflected. Scripts
/// that measure-then-mutate get correct numbers; mutate-then-measure get
/// the pre-mutation layout. Surfacing real "force layout" semantics
/// requires re-running the cascade + layout from inside a binding call,
/// which is a follow-up.</para>
///
/// <para><b>Coordinate space:</b> rects are returned in CSS px, relative
/// to the document viewport. This matches the spec definition of
/// <c>getBoundingClientRect</c> when the page has not been scrolled (no
/// scrolling is wired through this surface yet).</para>
/// </remarks>
public interface ILayoutHost
{
    /// <summary>Border-box rect for <paramref name="element"/>, in viewport
    /// CSS px. Returns <c>false</c> when the element wasn't laid out (e.g.
    /// it's <c>display: none</c>, detached from the document, or appeared
    /// only post-snapshot).</summary>
    bool TryGetBoundingClientRect(Element element, out LayoutRect rect);

    /// <summary>Metrics modelled on the HTMLElement layout
    /// properties: <c>offsetWidth</c> / <c>offsetHeight</c> include the
    /// border box; <c>offsetTop</c> / <c>offsetLeft</c> are relative to
    /// the offset parent. The host approximates the offset parent as the
    /// document origin — good enough for most pages, exact for absolute
    /// positioning chains lands with the offsetParent walk.</summary>
    bool TryGetOffsetMetrics(Element element, out OffsetMetrics metrics);

    /// <summary>Resolved value of <paramref name="propertyName"/> on
    /// <paramref name="element"/>, formatted for CSS-text serialization
    /// (e.g. <c>"16px"</c>, <c>"rgb(0, 0, 0)"</c>). Returns
    /// <c>string.Empty</c> when the property is unknown or the element
    /// has no computed style.</summary>
    string GetComputedProperty(Element element, string propertyName);

    /// <summary>Scroll metrics for <paramref name="element"/>'s scroll
    /// container, read from the engine session's scroll store
    /// (browser-plan/scroll-model.md): offsets clamped to the legal range,
    /// scrollport (padding box) and scrollable overflow from the last layout.
    /// Returns <c>false</c> when the element is not a scroll container — the
    /// bindings then fall back to today's zeros / padding-box aliases. The
    /// default implementation reports no scroll state, so hosts that predate
    /// the store keep compiling unchanged.</summary>
    bool TryGetScrollMetrics(Element element, out ScrollMetrics metrics)
    {
        metrics = default;
        return false;
    }

    /// <summary>The document scroller's metrics — backs
    /// <c>window.scrollX/scrollY</c> and the document element's
    /// <c>scrollWidth</c>/<c>scrollHeight</c>. Default: all zeros (a
    /// never-scrolled, never-measured document).</summary>
    ScrollMetrics GetRootScrollMetrics() => default;

    /// <summary>Write side of the element scroll surface (the
    /// <c>scrollTop</c>/<c>scrollLeft</c> setters, <c>scrollTo</c>,
    /// <c>scrollBy</c>, <c>scrollIntoView</c>). Per
    /// browser-plan/scroll-model.md: flush layout if dirty (the same
    /// up-to-date rule the offset metrics use), clamp to the legal range,
    /// store, flag the pending scroll event — and never relayout. The repaint
    /// rides the store's pending-event flag, which the shells' frame pump
    /// drains. Writes to a non-scroller clamp to (0,0), CSSOM's no-op.
    /// Default: ignored, so hosts that predate the store keep compiling.</summary>
    void SetScrollOffset(Element element, double x, double y)
    {
    }

    /// <summary>Document-scroller variant of <see cref="SetScrollOffset"/> —
    /// backs <c>window.scrollTo</c>/<c>scrollBy</c> and the root element's
    /// <c>scrollTop</c>/<c>scrollLeft</c> setters. Default: ignored.</summary>
    void SetRootScrollOffset(double x, double y)
    {
    }

    /// <summary>Evaluates a CSS media query string (e.g. <c>"(max-width:
    /// 768px)"</c>) against the document's media context — viewport size,
    /// color scheme, etc. — backing <c>window.matchMedia(q).matches</c>.
    /// Returns <c>false</c> for an unparseable query or when no style context
    /// is available.</summary>
    bool MatchMedia(string query);
}

/// <summary>Viewport-relative CSS-px rect returned by
/// <see cref="ILayoutHost.TryGetBoundingClientRect"/>.</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Top => Y;
    public double Left => X;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

/// <summary>CSSOM-View-shaped scroll metrics in CSS px:
/// <c>scrollLeft</c>/<c>scrollTop</c> are the clamped offsets,
/// <c>scrollWidth</c>/<c>scrollHeight</c> the scrollable overflow, and
/// <c>clientWidth</c>/<c>clientHeight</c> the scrollport (padding box; no
/// scrollbar inset — scrollbars are overlay style by decision).
/// <c>clientLeft</c>/<c>clientTop</c> are the border-edge → padding-edge
/// insets (CSSOM <c>clientLeft</c>/<c>clientTop</c>); the
/// <c>scrollIntoView</c> walk uses them to locate the scroll origin inside
/// the border box. Defaulted so pre-existing construction sites and fakes
/// keep compiling.</summary>
public readonly record struct ScrollMetrics(
    double ScrollLeft,
    double ScrollTop,
    double ScrollWidth,
    double ScrollHeight,
    double ClientWidth,
    double ClientHeight,
    double ClientLeft = 0,
    double ClientTop = 0);

/// <summary>HTMLElement-shaped offset / client metrics in CSS px.</summary>
public readonly record struct OffsetMetrics(
    double OffsetWidth,
    double OffsetHeight,
    double OffsetTop,
    double OffsetLeft,
    double ClientWidth,
    double ClientHeight);
