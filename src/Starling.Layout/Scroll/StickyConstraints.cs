// SPDX-License-Identifier: Apache-2.0
using Starling.Dom;

namespace Starling.Layout.Scroll;

/// <summary>
/// Layout-time record for one <c>position: sticky</c> element — everything the
/// scroll-time shift arithmetic needs, so no layout runs on a scroll tick
/// (browser-plan/scroll-model.md, "Sticky: constraints at layout, offset at
/// scroll"). All geometry is in the binding scroller's <em>padding-box space,
/// unscrolled</em>: the scrollport's origin is the scroller's padding-box
/// corner, so the element's position in scrollport space at any instant is
/// <c>Natural - scrollOffset</c>. For the root scroller that space is the
/// document space itself.
/// </summary>
/// <param name="NaturalX">Natural (in-flow, unshifted) border-box X.</param>
/// <param name="NaturalY">Natural border-box Y.</param>
/// <param name="Width">Border-box width (the shift never resizes).</param>
/// <param name="Height">Border-box height.</param>
/// <param name="CbX">Containing block's content-box left edge.</param>
/// <param name="CbY">Containing block's content-box top edge.</param>
/// <param name="CbRight">Containing block's content-box right edge.</param>
/// <param name="CbBottom">Containing block's content-box bottom edge.</param>
/// <param name="Top">Resolved <c>top</c> inset in px, or null when not
/// specified — only specified insets constrain.</param>
/// <param name="Right">Resolved <c>right</c> inset, or null.</param>
/// <param name="Bottom">Resolved <c>bottom</c> inset, or null.</param>
/// <param name="Left">Resolved <c>left</c> inset, or null.</param>
/// <param name="Scroller">The nearest scrolling ancestor the element is bound
/// to — the first scroll container on the containing-block chain — or null for
/// the root (document) scroller. Each sticky binds to exactly ONE scroller;
/// outer scrollers move the whole inner scroller, constraints included.</param>
public readonly record struct StickyConstraints(
    double NaturalX,
    double NaturalY,
    double Width,
    double Height,
    double CbX,
    double CbY,
    double CbRight,
    double CbBottom,
    double? Top,
    double? Right,
    double? Bottom,
    double? Left,
    Element? Scroller);
