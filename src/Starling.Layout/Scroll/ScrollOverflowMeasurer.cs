using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Layout.Box;

namespace Starling.Layout.Scroll;

/// <summary>
/// Post-layout measurement pass: records every scroll container's scrollport
/// (padding box) and scrollable overflow into the <see cref="ScrollStateStore"/>,
/// plus the root entry for the document scroller.
/// </summary>
/// <remarks>
/// <para>Scrollable overflow follows CSS Overflow 3 §2.2: the union of the
/// box's own padding box with the border boxes of in-flow and positioned
/// descendants, in the container's padding-box space — deep descendants and
/// positioned boxes included, not the direct-children guess the old panel
/// code used. Two scope cuts, both per the spec's shape:</para>
/// <list type="bullet">
///   <item>The walk does not descend into a descendant that establishes its
///   own scroll container or otherwise clips (<c>hidden</c>/<c>clip</c>) —
///   that box's inner overflow belongs to it, only its border box leaks
///   out.</item>
///   <item>Top/left overhang (negative margins) is dropped: the scroll
///   origin is the padding-box corner, so content above/left of it is
///   unreachable in LTR and must not widen the scrolling area (CSSOM View
///   scrolling-area definition).</item>
/// </list>
/// <para>v1 deviations, called out in browser-plan/scroll-model.md: only
/// <c>auto</c>/<c>scroll</c> boxes get store entries — an
/// <c>overflow: hidden</c> box is programmatically scrollable per spec but
/// is out of v1 scope (it still clips, and still bounds this walk).
/// <c>position: fixed</c> descendants are viewport-anchored and contribute
/// to no scrolling area. An absolutely positioned descendant whose
/// containing block sits outside the scroller is still counted (its frame is
/// stored parent-relative); the spec excludes it, but the case is rare and
/// the error is only ever a too-large scroll range.</para>
/// <para>Runs after the position pass — positioned boxes get their frames
/// there, and the doc's overflow definition includes them, so measuring when
/// the block pass finishes a box would under-report.</para>
/// </remarks>
internal static class ScrollOverflowMeasurer
{
    /// <summary>Measure <paramref name="root"/>'s laid-out tree into
    /// <paramref name="store"/>. One O(n) dispatch walk; each box's extent is
    /// accumulated once, for its nearest enclosing clip root.</summary>
    internal static void Measure(Box.Box root, Size viewport, ScrollStateStore store)
    {
        // Document scroller: scrollport = viewport, scrollable overflow = the
        // page extent (the root border box plus anything poking past it).
        double right = 0, bottom = 0;
        AccumulateExtent(root, 0, 0, ref right, ref bottom);
        store.RecordRootGeometry(
            viewport.Width, viewport.Height,
            Math.Max(viewport.Width, right), Math.Max(viewport.Height, bottom));

        Visit(root, store);
    }

    private static void Visit(Box.Box box, ScrollStateStore store)
    {
        if (box.Element is { } el && IsScrollContainer(box.Style))
        {
            // Scrollport = padding box = border box minus borders. Overlay
            // scrollbars by decision (zero thickness), so no scrollbar inset.
            var portW = Math.Max(0, box.Frame.Width - box.Border.Horizontal);
            var portH = Math.Max(0, box.Frame.Height - box.Border.Vertical);

            // Children frames are in the container's content-box space; the
            // overflow rect is measured from the padding-box origin, which is
            // (Padding.Left, Padding.Top) above/left of the content origin.
            double right = 0, bottom = 0;
            foreach (var child in box.Children)
                AccumulateExtent(child, box.Padding.Left, box.Padding.Top, ref right, ref bottom);

            // CSS Overflow 3 §2.2: the scrollable overflow rectangle always
            // contains the padding box, so it never measures under the
            // scrollport.
            store.RecordGeometry(el, portW, portH,
                Math.Max(portW, right), Math.Max(portH, bottom));
        }

        foreach (var child in box.Children)
            Visit(child, store);
    }

    /// <summary>Union <paramref name="box"/>'s border box (and its non-clipped
    /// descendants') into the running right/bottom extent. (<paramref name="dx"/>,
    /// <paramref name="dy"/>) translate the box's parent-content-relative frame
    /// into the measuring container's padding-box space — the same origin walk
    /// the painter does when it descends.</summary>
    private static void AccumulateExtent(Box.Box box, double dx, double dy, ref double maxRight, ref double maxBottom)
    {
        // Fixed-position boxes anchor to the viewport; they are in no
        // ancestor's scrolling area (CSS Overflow 3 §2.2).
        if (IsFixedPosition(box.Style)) return;

        var x = dx + box.Frame.X;
        var y = dy + box.Frame.Y;

        if (box is TextBox text)
        {
            // Line fragments can run past every box frame (long unbreakable
            // text); the painter draws them at frame + fragment offset, so
            // measure the same way.
            foreach (var f in text.Fragments)
            {
                var fr = x + f.X + f.Width;
                var fb = y + f.Y + f.Height;
                if (fr > maxRight) maxRight = fr;
                if (fb > maxBottom) maxBottom = fb;
            }
            return;
        }

        var r = x + box.Frame.Width;
        var b = y + box.Frame.Height;
        if (r > maxRight) maxRight = r;
        if (b > maxBottom) maxBottom = b;

        // A descendant that clips (its own scroll container, or
        // hidden/clip) owns its inner overflow — only its border box
        // contributes here, so the walk is bounded at scroller boundaries.
        if (ClipsOverflow(box.Style)) return;

        var cdx = x + box.Border.Left + box.Padding.Left;
        var cdy = y + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            AccumulateExtent(child, cdx, cdy, ref maxRight, ref maxBottom);
    }

    /// <summary>True when either overflow axis computes to <c>auto</c> or
    /// <c>scroll</c> — the v1 scroll-container test (hidden is clipped but not
    /// user-scrollable, so it gets no store entry; see class remarks).</summary>
    internal static bool IsScrollContainer(ComputedStyle? style)
    {
        if (style is null) return false;
        return IsScrollKeyword(style.Get(PropertyId.OverflowX))
            || IsScrollKeyword(style.Get(PropertyId.OverflowY));
    }

    private static bool IsScrollKeyword(CssValue? value)
        => value is CssKeyword { Name: var n }
           && (n.Equals("auto", StringComparison.OrdinalIgnoreCase)
               || n.Equals("scroll", StringComparison.OrdinalIgnoreCase));

    private static bool ClipsOverflow(ComputedStyle? style)
    {
        if (style is null) return false;
        return IsClipKeyword(style.Get(PropertyId.OverflowX))
            || IsClipKeyword(style.Get(PropertyId.OverflowY));
    }

    private static bool IsClipKeyword(CssValue? value)
        => value is CssKeyword { Name: var n }
           && (n.Equals("auto", StringComparison.OrdinalIgnoreCase)
               || n.Equals("scroll", StringComparison.OrdinalIgnoreCase)
               || n.Equals("hidden", StringComparison.OrdinalIgnoreCase)
               || n.Equals("clip", StringComparison.OrdinalIgnoreCase));

    private static bool IsFixedPosition(ComputedStyle? style)
        => style is not null
           && style.Get(PropertyId.Position) is CssKeyword { Name: var n }
           && n.Equals("fixed", StringComparison.OrdinalIgnoreCase);
}
