# Scroll model and real `position: sticky` — design

Tier 3 item 8 in `tasks/SITE_STYLING_PLAN.md` calls this the deepest single
gap and asks for this doc before any code. Both GitHub's and x.com's sticky
headers depend on it.

## What exists today

More is in place than the plan item suggests. The gap is that the pieces do
not share one model.

- **Inner scroll works in one shell only.** `WebviewPanel` keeps a
  per-element offset dictionary (`Starling.Gui/Controls/WebviewPanel.cs:145-151`)
  and a wheel handler that walks the ancestor chain for an
  `overflow: scroll | auto` box with room to move (`:908-962`). The scrollable
  extent is guessed per wheel tick from direct children only (`ContentExtent`,
  `:975-987`), so deep or positioned descendants under-report.
- **Paint already translates.** The display-list builder emits the scroll
  offset as a `PushTransform` inside the overflow `PushClip` bracket
  (`Starling.Paint/DisplayList/DisplayListBuilder.cs:444-516`). The layer-tree
  builder threads the same lookup into every layer slice
  (`Starling.Paint/Compositor/LayerTreeBuilder.cs:40-57`).
- **Hit testing follows.** `BoxHitTester.HitTest` takes the same offset
  lookup (`Starling.Gui.Core/Interaction/BoxHitTester.cs:64,154-156`).
- **The native shell has only root scroll.** One `scrollY` double, no
  per-element offsets (`Starling.Shell.Native/NativeBrowserWindow.cs:740-745`).
- **Sticky is clamped-relative.** No scroll input at all
  (`Starling.Layout/Position/Sticky.cs`, applied at
  `Starling.Layout/Position/PositionLayout.cs:116`).
- **The JavaScript surface is stubbed.** `scrollTop`/`scrollLeft` return 0,
  and `scrollWidth`/`scrollHeight` alias the offset size
  (`Starling.Bindings/NodeBindings.cs:1319-1332`). `window.scrollX/Y` return 0
  (`Starling.Bindings/WindowBinding.cs:155-161`). No `scrollTo`, `scrollBy`,
  or `scrollIntoView`.

So the work is: one shared scroll store, real overflow measurement in layout,
sticky wired to it, the bindings wired to it, and both shells reading it.

## Goals (v1)

- Element scroll containers for `overflow: auto | scroll`, both axes.
- Wheel and trackpad scrolling in the Avalonia host and the native shell.
- Programmatic `scrollTop`/`scrollLeft` writes, `scrollTo`, `scrollBy`,
  `scrollIntoView` (instant only).
- `position: sticky` pinned against the nearest scrollport.
- `scroll` events, coalesced to one per frame per target.
- Correct `scrollWidth`/`scrollHeight`/`clientWidth`/`clientHeight`.

## Non-goals (v1)

- Scroll snap, smooth-scroll animation (`behavior: smooth` falls back to
  instant), overscroll effects.
- Scrollbar painting. Scrollbars are overlay style with zero thickness, like
  macOS. So `clientWidth` never shrinks for a scrollbar. A painted thumb can
  come later without changing any geometry.
- `scrollend`. It needs a quiet-period heuristic. Later.

## Data model: the scroll store

A new `ScrollStateStore` in `Starling.Layout`, one instance per document,
keyed by `Element`. Boxes are rebuilt every relayout, so state on the box
dies. Elements are stable, which is why `WebviewPanel._scrollOffsets` and the
script-animation store (`Starling.Css/Animations/AnimationEngine.cs:119`)
both key by element already. Same pattern here.

Each entry holds:

- **Offset** — current `(x, y)`, always clamped to the legal range.
- **Scrollport** — the padding-box size from the last layout. This is the
  `clientWidth`/`clientHeight` source.
- **Scrollable overflow** — the content extent from the last layout. This is
  the `scrollWidth`/`scrollHeight` source and the clamp bound.
- **Pending-event flag** — set on any offset change, drained by the frame
  pump (see Events).

The store also holds one root entry for the document scroller. The engine
session owns the store and exposes it through `ILayoutHost`
(`Starling.Js.Hosting/ILayoutHost.cs`), so the bindings, both shells, paint,
and hit testing all read the same numbers. `WebviewPanel` drops its private
dictionary in the same change — no second copy, ever.

## Layout: measure, never dirty

Scrolling must not dirty layout. The offset is a paint-time and hit-test-time
concern only. Two rules keep that true:

1. **Measure during layout.** When the block pass finishes a box whose style
   scrolls on either axis, it records the scrollport and the scrollable
   overflow into the store. Scrollable overflow is the union of in-flow and
   positioned descendant border boxes in the container's content space, per
   CSS Overflow 3, not the direct-children guess the panel uses today.
2. **Clamp after layout.** When content shrinks or the scrollport grows, the
   stored offset can exceed the new range. After each layout pass the store
   re-clamps every entry. A clamp that moves the offset sets the
   pending-event flag like any other scroll.

The offset never enters `ConstraintSpace`
(`Starling.Layout/Incremental/ConstraintSpace.cs`). If it did, every wheel
tick would miss the reuse key and force a full subtree relayout. Nothing in
layout may read the offset — sticky reads constraints out, not offsets in.

## Paint and compositor: where the offset applies

Two candidate homes for the offset:

- **In the display list** — the `PushTransform` inside the `PushClip` bracket
  that exists today. Correct everywhere, but the translation changes the
  layer slice's content hash, so every visible 2048×512 tile of that layer
  re-rasters on every tick (`Starling.Paint/Compositor/TileGrid.cs` keys
  tiles by position and validates by content hash).
- **On the compositor layer** — paint the scroller's contents once at offset
  zero in scroll-content space, and apply the offset plus clip when the layer
  is composited.

**Recommendation: promote scroll containers to their own compositor layer and
apply the offset at composite time.** That is the whole point of the tile
grid: a scroll re-blits cached tiles and rasters only the newly exposed row.
Add a `LayerHint` for scroll containers
(`Starling.Layout/Compositor/LayerHint.cs`) and give `CompositorLayer` a
scroll offset and a scrollport clip. The in-list translation stays as the
fallback for the CPU readback path and for any scroller that is not promoted.
It is already built and correct, just slower.

Ship correctness first on the existing translation path, then land promotion
as its own package (sizes below).

## Sticky: constraints at layout, offset at scroll

Keep the split the comment in `Sticky.cs` predicts:

1. **At layout**, for each sticky element, record a `StickyConstraints`
   entry: the natural frame, the containing-block rect, the resolved insets,
   and the nearest scrolling ancestor (walk the containing-block chain for
   the first box whose style scrolls, else the root scroller).
2. **At scroll time**, the shift is pure arithmetic. For `top: T`: the
   element's natural position in scrollport space is
   `natural.y - scrollOffset.y`. If that falls below `T`, shift down by the
   difference. Clamp the shift so the box never leaves its containing block
   — the slack is `containingBlock.bottom - natural.bottom`. Mirror for the
   other three insets.

The shift is applied exactly like the scroll offset: a paint-time transform
on the sticky box's items and the matching adjustment in `BoxHitTester`.
Layout frames never move, so the incremental reuse keys stay clean.

Nested scrollers come free: each sticky element binds to one ancestor
scroller, and the arithmetic only reads that scroller's offset. Outer
scrollers move the whole inner scroller, constraints included.

Transforms: a transformed ancestor becomes the containing block for sticky
descendants per CSS Transforms. V1 records the constraint against that
ancestor and otherwise composes the shift with the existing transform
matrix. No special cases beyond that.

The current clamped-relative code stays as the no-scroller fallback, which
keeps every static render today pixel-identical until a scroller exists.

## Input and events

**Routing.** Move the panel's ancestor-walk (`TryScrollContainer`) into a
shared `ScrollController` in `Starling.Gui.Core`, next to `BoxHitTester`. It
takes a hit result and a wheel delta, walks up for the deepest scroller with
room (latched per axis), writes the store, and reports whether it consumed
the delta. The Avalonia panel calls it from `OnPageWheel` and falls through
to the outer `ScrollViewer` when nothing consumes. The native shell calls it
from its `mouse.Scroll` handler and falls through to its root `scrollY`.
Wheel deltas stay in lines times 40 CSS pixels, the value both shells use.

**Events.** A `scroll` event fires on the scrolled element and does not
bubble. The document scroller fires on `document` instead, and that one
bubbles to `window`. Dispatch is coalesced: offset writes only set the
store's pending flag, and the frame pump drains the flagged set once per
frame, before `requestAnimationFrame` callbacks, matching the HTML event
loop's scroll steps. `"scroll"` is already a known event name
(`Starling.Bindings/EventTargetBinding.cs:949`), so `onscroll` content
attributes work once dispatch exists.

**Passive by default.** The wheel handler updates the offset and schedules a
repaint before any JavaScript runs. Listeners cannot cancel a scroll in v1.
That is the spec's passive default and it keeps input latency off the
JavaScript clock.

## JavaScript surface

All through `ILayoutHost`, implemented in `Starling.Engine/BoxLayoutHost.cs`:

- `scrollTop`/`scrollLeft` **read** the store entry. **Write** flushes layout
  if dirty (the same up-to-date rule the offset metrics use), clamps, stores,
  flags the event, schedules a repaint. No relayout.
- `scrollTo(x, y)` / `scrollTo(options)` and `scrollBy` on elements and on
  `window`. `behavior: "smooth"` is accepted and treated as instant.
- `scrollIntoView` resolves the target rect, then walks each scrolling
  ancestor outward, adjusting each offset just enough to bring the rect into
  that scrollport (`block`/`inline` alignment options honored, instant only).
- `scrollWidth`/`scrollHeight` switch from the offset-size alias to the
  stored scrollable overflow. `clientWidth`/`clientHeight` stay the padding
  box, with zero scrollbar inset by the overlay-scrollbar decision.
- `window.scrollX/scrollY` read the root entry. The Avalonia panel syncs its
  `ScrollViewer` offset into the root entry on `ScrollChanged`, and applies
  store-driven root writes back to the `ScrollViewer`.

## Phased work packages

Each lands alone and keeps the tree green. Sizes are relative (S < M < L).

| # | Package | Size | Tests |
|---|---|---|---|
| WP1 | `ScrollStateStore` + overflow measurement + post-layout clamp in `Starling.Layout` | M | `Starling.Layout.Tests`: overflow rect per container shape (deep children, positioned, negative margins), clamp after content shrink and scrollport grow |
| WP2 | Shells on the store: panel dictionary deleted, shared `ScrollController`, native shell inner scroll | M | `Starling.Gui.Tests` controller units (ancestor walk, axis latch, rail fall-through), GUI smoke on the nested-scroller fixture |
| WP3 | Bindings: read-write `scrollTop`/`scrollLeft`, `scrollTo`/`scrollBy`/`scrollIntoView`, `scrollWidth`/`scrollHeight` fix, `window` pair | M | extend `tests/Starling.Bindings.Tests/CssomViewBindingTests.cs`: write-clamp round trips, `scrollIntoView` through two nested scrollers, overflow sizes |
| WP4 | `scroll` event dispatch, coalesced in the frame pump | S | `Starling.Bindings.Tests`: one event per frame for N writes, no bubble from elements, document → window bubble |
| WP5 | Sticky constraints + scroll-time shift, hit-test parity | L | `Starling.Layout.Tests` constraint units, `Starling.Paint.Tests` golden of a stuck header mid-scroll, fixture render of GitHub and x.com headers vs Chromium |
| WP6 | Promoted scroll layers: `LayerHint`, composite-time offset, tile reuse | M | `Starling.Paint.Tests`: assert zero tile re-rasters for a pure scroll of a promoted layer, only the exposed row rasters |

Order: WP1 first. WP2, WP3, WP4 are independent after it and can fan out.
WP5 needs WP1 and WP2. WP6 is pure performance and can trail everything.

## Risks

- **Reentrancy.** A `scroll` listener that reads `offsetTop` triggers a
  layout flush inside dispatch. Safe only because dispatch runs from the
  frame pump, never from the wheel handler or inside a paint. Keep that rule
  absolute, and add a test that scrolls from inside a scroll listener.
- **Per-tick cost.** Until WP6, an inner scroll re-rasters every visible
  tile of its layer, because the in-list translation changes the slice
  content hash. Fine for small panels, painful for a full-height feed. WP6
  is the fix, and the WP6 test pins it.
- **Incremental-layout keys.** The offset must never reach `ConstraintSpace`
  or any other reuse key. The post-layout clamp and the sticky shift both
  run after the layout pass for the same reason. A debug assert in the store
  (no writes while a layout pass is running) makes violations loud.
- **Fixed elements during scroll.** The Avalonia root scroll re-renders the
  visible region, which is what re-anchors `position: fixed` boxes today. If
  the root scroller ever moves to a promoted compositor layer, fixed layers
  must be split out and anchored to the viewport, not the page. V1 keeps
  root scrolling in the shells, so this stays a follow-up, but WP6 must not
  promote the root.
- **Migration split-brain.** While WP2 is in flight, the panel dictionary
  and the store would disagree. WP2 deletes the dictionary in the same
  change — no compatibility window.

## Open decisions (need a human call)

1. **Root scroller ownership.** V1 keeps the Avalonia `ScrollViewer` and the
   native shell's `scrollY` as the root scroller, synced into the store.
   Folding root scroll into the engine store outright is cleaner but drags
   in the fixed-element split. Recommend: keep the shells for v1.
2. **Sticky and client rects.** With a paint-time shift,
   `getBoundingClientRect` on a stuck element reports the unstuck position.
   Chromium reports the stuck one. Accept the gap for v1, or have the rect
   path apply the same shift (small, but one more store read in a hot
   metric). Recommend: apply the shift in the rect path during WP5.
3. **Trackpad precision deltas.** Avalonia delivers pixel-precision deltas
   on macOS. Mapping them through the 40-pixels-per-line constant feels
   wrong on trackpads. Decide whether v1 special-cases precision deltas or
   ships the constant everywhere.

## Decisions taken (review, 2026-06-09)

Reviewed against the code — every file reference above checks out. The
plan stands as written. The three open calls close as:

1. Root scroller stays in the shells for v1, synced into the store.
2. The sticky shift applies in the client-rect path, landed with WP5.
3. WP2 uses precision deltas directly when the input event flags them.
   The lines-times-40 constant stays for real wheel ticks.
