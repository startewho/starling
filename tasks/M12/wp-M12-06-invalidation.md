---
id: wp:M12-06-invalidation
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on:
  - wp:M12-04-layer-tree
blocks:
  - wp:M12-09-compositor-anim
subsystem: Starling.Paint
plan_refs:
  - browser-plan/07_LAYOUT.md
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-06-invalidation — Per-layer + per-tile dirty tracking

## Goal

Today (and through `wp:M12-05-tile-grid`) a single `Page.DisplayListVersion`
bump invalidates every tile of every layer. That makes a `:hover` that
recolors one button re-paint the entire page on the next frame. This WP
introduces fine-grained invalidation: when a style/layout change affects
only a subtree, only the tiles whose painted region overlaps that subtree
are evicted from cache.

## Inputs

- `wp:M12-04-layer-tree` shipped: layers exist with per-layer caches.
- The style + layout pipeline must signal *which boxes* changed, not just
  *that something changed*. This is a real extension to today's
  cascade/layout — they currently rebuild whole trees from scratch.

## Outputs

- A `LayoutDelta` record produced by the layout pass, listing the
  subtree roots whose painted output changed since the previous frame.
  (The first version can be coarse: "this layer changed" as a whole.)
- `LayerCache.InvalidateRect(pageRect)` that evicts every tile whose
  painted region intersects `pageRect`. The tile's content-version is
  bumped so a stale composite for that tile can't be served.
- A new diagnostics counter `paint.invalidation.tiles_dropped` per frame.
- A documented invariant: a frame's composite output is a function of
  `(layout tree, viewport, scale)`; any cached tile served must have been
  painted from the *same* layout subtree the post-invalidation layout
  produces.

## Acceptance

- A test page with N boxes where one box changes color records
  `paint.invalidation.tiles_dropped` equal to the tile count covering
  *that box's bounds only*, not the page.
- A hover-driven style change on a single button invalidates ≤ 4 tiles in
  a 1080p viewport with default 256² tiles.
- A layout reflow (e.g. text wrap due to width change) correctly
  invalidates the entire affected flow region — the test should observe
  the union of pre- and post-reflow bounds being invalidated.
- `dotnet build && dotnet test` green.

## Notes

- Pre-existing layout code does whole-tree rebuilds. Either upgrade
  layout to track dirty subtrees natively, or compare the new and old
  layout trees in a diff pass at the end of layout to derive the delta.
  Diff pass is simpler to land first and easier to validate.
- `Page.DisplayListVersion` becomes a fallback for the "we don't know
  what changed" case (e.g. UA stylesheet swap, font load); deltas are
  the fast path.
- Hover/focus styles route through here too — the cascade marks the
  matched element dirty, which becomes a per-tile invalidation.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
