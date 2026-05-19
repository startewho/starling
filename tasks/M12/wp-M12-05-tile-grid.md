---
id: wp:M12-05-tile-grid
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on:
  - wp:M12-04-layer-tree
blocks:
  - wp:M12-07-compositor-thread
  - wp:M12-08-prefetch-ring
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-05-tile-grid — Per-layer tile grid + tile cache

## Goal

Replace each layer's single `PictureCache` bitmap with a 2D grid of fixed-
size tiles (default 256×256 device px). Rasterization happens per tile:
only tiles intersecting the viewport (plus a 1-tile overdraw ring) are
painted. Tiles are cached keyed by
`(layerId, tileX, tileY, scale, contentVersion)` and held in an LRU with a
configurable byte budget. Composite walks visible tiles and blits them.

After this WP, no single GPU texture allocated by the paint pipeline
exceeds the tile size — the wgpu max-texture-dimension class of bug is
structurally impossible regardless of page or layer dimensions, and the
fallback guard in `ImageSharpBackend` can finally be retired entirely.

## Inputs

- `wp:M12-04-layer-tree` shipped: `CompositorLayer` carries a
  `PictureCache`.
- A device-pixel scale at composite time (already threaded through
  `Render(viewport, scale)`).

## Outputs

- `Starling.Paint.Compositor.TileGrid`:
  - `TileSize` (constant, 256).
  - `TryGetTile(layerId, tileCoord, scale, version) → CachedTile?`
  - `PutTile(layerId, tileCoord, scale, version, bitmap)`
  - LRU eviction with a `MaxBytes` budget (default 256 MB; configurable
    via `StarlingPaintOptions.TileCacheBudgetBytes`).
- `LayerCache` (replacing or wrapping `PictureCache`):
  - Decides which tiles to paint for a given viewport intersection.
  - Calls the backend once per uncached tile with a translated transform
    so the tile's (0,0) maps to the tile's page-coord origin.
- `CompositorLayer.Render(viewport, scale)` becomes "for each visible
  tile, ensure painted, blit to the composite".
- Metrics: `paint.tile.cache_hit`, `paint.tile.cache_miss`,
  `paint.tile.evict`, `paint.tile.bytes`, histogram of tiles painted per
  frame.

## Acceptance

- A 50000-px-tall promoted layer renders correctly across a viewport-sized
  output. Painted tile count for a single frame is bounded by
  `(viewport.Width / 256 + 2) × (viewport.Height / 256 + 2)`, not by the
  layer's full height.
- Scrolling the layer by 1 tile-width re-paints exactly one row/column of
  newly-exposed tiles; the rest come from cache.
- The `MaxWebGpuTextureDimension` guard added by the netclaw.dev fix is
  removed (no longer needed — tiles are 256² and well under any device
  limit).
- LRU evicts oldest tiles first when the budget is exceeded; eviction
  count appears in metrics.
- `dotnet build && dotnet test` green.

## Notes

- Tile boundaries cause visible seams for shapes that straddle tiles when
  anti-aliasing is non-trivial. Two acceptable strategies, pick one and
  document:
  1. Paint each tile with a 1-px overdraw into its neighbors' regions and
     blit ignoring overdraw on composite (simple, costs ~3% area).
  2. Snap shapes to tile-edge integer coordinates (cheaper, ugly).
  Recommendation: (1).
- Text + glyph rasterization can be cached at a higher level (glyph atlas)
  — out of scope here but called out as a follow-up.
- Tile painting is *still* on the main thread in this WP. The
  compositor-thread split lives in `wp:M12-07-compositor-thread`.
- **Tile geometry — consider WebRender-style slices over Chrome-style tiles.**
  The "uniform 256² grid per layer" model is Chrome's cc; WebRender uses
  a *very* different geometry: 1–3 "slices" per scene (content / UI /
  hidden background), each split into a small number of large rectangular
  tiles (2048×512 for content, 128×128 for UI). See
  `gfx/wr/webrender/src/picture.rs` module doc-comment. The advantages:
  fewer tiles overall, fewer seams (so less overdraw cost), and the
  natural scroll-root boundary maps cleanly onto Starling's eventual
  "browser chrome vs. webview" split. The disadvantage: a single dirty
  pixel in a 2048×512 tile invalidates more work than in a 256² tile.
  WebRender mitigates with per-tile quadtree dependency tracking that
  feeds a partial-present scissor rect. Recommendation: implement the
  small-slice + large-tile geometry first; only fall back to a uniform
  256² grid if profiling shows the per-tile invalidation cost dominates.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
- 2026-05-19T17:56Z — added WebRender-vs-Chrome tile-geometry note after
  verifying with primary sources (Mozilla Hacks 2017, searchfox
  `picture.rs`, mozillagfx 2019 Core Animation retrospective) that
  WebRender's picture caching uses 2048×512 content tiles + 128×128 UI
  tiles in 1–3 slices, not a uniform per-layer 256² grid. Document the
  trade-off so the implementing agent considers slice-first.
  (agent-copilot-claude-opus-4.7)
