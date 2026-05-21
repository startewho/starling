---
id: wp:M12-11-glyph-atlas-cpu
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on: []
blocks: []
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - tasks/M12/wp-M12-05-tile-grid.md
---

# wp:M12-10-glyph-atlas-cpu — Glyph atlas for the CPU paint backend

## Goal

Cache rasterized glyphs (keyed per face/codepoint/device-size/subpixel-phase/
color) as small device-resolution `Image<Rgba32>` instances and blit them with
`DrawingCanvas.DrawImage` instead of re-filling every glyph outline on each
paint. Scope this to the **CPU** (`imagesharp`) backend only — see "Why
CPU-only" below.

## Why this exists (Phase 0 spike findings)

Paint cost on a typical page is ~99% text (measured: 257 `draw_text` + 73
`draw_text_decoration` vs 7 non-text items on nginx.org). Shaping is already
reused via shaped runs (257 reuse / 4 reshape), so the recurring cost is glyph
**rasterization**, which has no cache today.

A Phase 0 spike (throwaway `GlyphAtlasSpike`, since removed) established:

- **Lossless + matches current output.** A glyph rendered at device resolution
  and blitted 1:1 (`NearestNeighbor`) scored `SSIM = 1.0000` against both a
  device-direct render and the current CSS-coords-through-a-device-scale-matrix
  path. So caching at device resolution is byte-identical to today's output.
- **Subpixel phases are required.** Naive integer per-glyph positioning scored
  `SSIM = 0.9709` — below the 0.99 snapshot-test floor. Cache ~3 horizontal
  subpixel phases (0, ⅓, ⅔ px); snap the vertical baseline to an integer device
  pixel.
- **Metrics are available.** `TextMeasurer.MeasureRenderableBounds` +
  `MeasureAdvance` give the per-glyph device bbox/bearings + advance needed to
  size atlas cells and position blits. (SixLabors 3 exposes only codepoints, no
  glyph indices, so key the cache on codepoint.)
- **CPU win is large; WebGPU win is nil.** Fair per-glyph comparison (same glyph
  budget, grouped as 8-glyph words for `DrawText` vs per-glyph cached
  `DrawImage`):
  - **CPU: 29.4µs → 2.6µs per glyph (≈11× faster).**
  - **WebGPU: 25.7µs → 27.6µs per glyph (≈0.93×, no win).**
  The WebGPU package is a Vello-style scene renderer; its bottleneck is
  scene-encoding throughput, not glyph rasterization, and it already encodes
  glyph runs about as cheaply as textured quads. The WebGPU lever is retained
  scene caching, tracked separately — **not** this WP.

## Inputs

- `ImageSharpBackend.DrawText` / `DrawTextShadow` are the call sites.
- Glyphs already arrive with a shaped run (`frag.Shaped` →
  `ImageSharpShapedRun`) carrying per-glyph codepoints + pen positions; the
  display list threads it through `DrawText`.
- `IDiagnostics` for hit/miss/evict counters.

## Design sketch

- A `GlyphAtlas` owned per `ImageSharpBackend` instance (alongside
  `_fontCache`). Key: `(FontSpec, codepoint, devicePixelSize, subpixelPhaseX,
  color)`. Value: a persistent device-res `Image<Rgba32>` of the glyph
  (pre-colored — `DrawImage` has no tint) plus its bitmap-origin offset.
- **Reuse the same `Image` instance** across draws/frames (the persistent
  instance is what made the spike fast; the existing `DrawImage` *display-item*
  path rebuilds the source `Image` every call via `Image.LoadPixelData` at
  `ImageSharpBackend.cs:704`, which must be bypassed for glyphs — see the
  related DrawImage-rebuild cleanup).
- `DrawText`: when on the CPU backend, iterate the shaped run's glyphs, look up/
  rasterize each, and `canvas.DrawImage` it at the device pen position with the
  matching subpixel phase. On the WebGPU backend, keep the current
  `canvas.DrawText` path (atlas gives no benefit there).
- Fall back to direct `DrawText` for color/emoji/bitmap glyphs, combining marks,
  and RTL initially.
- LRU eviction on a byte budget (~32–64 MB); dispose evicted `Image` instances;
  never evict an entry used in the current frame.

## Acceptance

- CPU-backend full-viewport text paint drops materially (target: ≥3× on a
  text-heavy page; spike suggests up to ~11× on the glyph-fill portion).
- `EngineSnapshotRenderTests` / `GoogleSearchTests` stay ≥ 0.99 SSIM (re-bless
  goldens only if needed); `PictureCacheTests` byte-identical guarantee holds
  (atlas is used on both the from-scratch and cached sides).
- New unit tests: atlas hit/miss counters; run-rendered-via-atlas vs direct
  `DrawText` within SSIM ≥ 0.99 across the subpixel phases; eviction.
- A paint micro-bench (extend `PaintBench`) records before/after.

## Out of scope

- WebGPU text acceleration (no atlas benefit; pursue retained scene caching).
- Tile grid (`wp:M12-05`) and partial invalidation (`wp:M12-06`).
