---
id: "wp:M3-06c-decoded-image"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-image"
claimed_at: "2026-05-14T14:42:59Z"
completed_at: "2026-05-14T14:50:00Z"
branch: "main"
depends_on: []
blocks:
  - "wp:M3-06d-codecs"
  - "wp:M3-06i-skia-backend"
subsystem: "Starling.Common"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06c-decoded-image — `DecodedImage` seam in `Starling.Common`

## Goal

Phase 8 (seam half): define a backend-neutral `DecodedImage` type in
`Starling.Common` and thread it through every place the engine currently passes
an ImageSharp `Image<Rgba32>` or untyped `object Source`. This decouples the
image-decode contract from any one decoder so `Starling.Codecs` (native) can be
swapped in later. **ImageSharp stays working** as the decoder + rasterizer in
this package — this is a type seam, not a behavior change — so the package is
independently mergeable to `main` without breaking the running engine.

## Inputs

- No code dependencies; the `DecodedImage` type definition is immediate and the
  threading is mechanical.
- Existing image path: `IImageResolver.ResolvedImage`, `DisplayItem.DrawImage`,
  `DisplayListBuilder`, `ImageFetcher`, `ImageSharpBackend`, `BoxTreeRenderer`.

## Outputs

- `src/Starling.Common/Image/DecodedImage.cs` — `{ int Width, int Height,
  ReadOnlyMemory<byte> Pixels }` (straight RGBA8888), `IDisposable`.
- `src/Starling.Layout/Tree/IImageResolver.cs` — `ResolvedImage` carries
  `DecodedImage` instead of `object Source` / `Image<Rgba32>`.
- `src/Starling.Paint/DisplayList/DisplayItem.cs` — `DrawImage` variant carries a
  `DecodedImage` (this is the contended file — land this package **first** so
  `06i-skia-backend` builds on the final signature).
- `src/Starling.Paint/DisplayList/DisplayListBuilder.cs` — emits `DrawImage` with
  `DecodedImage`.
- `src/Starling.Engine/ImageFetcher.cs` — produces `DecodedImage` (still via
  ImageSharp decode in this package).
- `src/Starling.Paint/Backend/ImageSharpBackend.cs` — blits from `DecodedImage`
  pixels (ImageSharp still does the rasterizing).
- `src/Starling.Gui/BoxTreeRenderer.cs` — reads `DecodedImage`.
- Test updates so the existing 3-case image-paint golden suite still passes.

## Acceptance

- `DecodedImage` exists in `Starling.Common` with the exact shape above and is
  `IDisposable`.
- `object Source` / `Image<Rgba32>` no longer appears in `IImageResolver`,
  `DisplayItem.DrawImage`, `DisplayListBuilder`, `ImageFetcher`, or
  `BoxTreeRenderer` — all go through `DecodedImage`.
- ImageSharp still decodes and rasterizes; the engine renders identically.
- The existing image-paint golden tests pass byte-exact (no pixel change).
- Full repo `dotnet test` stays green at the current count.
- This package is mergeable to `main` standalone without breaking the engine.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 8,
  "DecodedImage seam").
- **File-contention:** `DisplayItem.cs` is also edited by `06i-skia-backend`.
  The coordination rule is explicit in the master plan — land `06c` first.
- `06d-codecs` depends on this: `ImageFetcher`'s decode call later becomes
  `NativeImageDecoder.Decode(bytes)` returning the same `DecodedImage`.
- Image **encode** is out of scope here — that goes to the Skia layer later.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
- 2026-05-14T14:50:00Z — completed (agent-claude-cody-image). Added
  `Starling.Common/Image/DecodedImage.cs` (sealed `IDisposable`, `Width`/`Height`/
  `ReadOnlyMemory<byte> Pixels`, straight top-down RGBA8888, pooled backing
  buffer via `ArrayPool`). Threaded `DecodedImage` through `IImageResolver`/
  `ResolvedImage`, `ImageBox.Source` (Box.cs), `DisplayItem.DrawImage`,
  `ImageFetcher` (ImageSharp still decodes; pixels copied out via
  `CopyPixelDataTo`, cache is `Dictionary<string,DecodedImage>` disposed on
  `Dispose`), `ImageSharpBackend.DrawImage` (`Image.LoadPixelData<Rgba32>` —
  ImageSharp still rasterizes), and `BoxTreeRenderer.EmitImage` (re-wrap +
  `SaveAsPng` for MAUI). `DisplayListBuilder` and `BoxTreeBuilder` needed no
  edits — the type flows through unchanged. Test helper `ManualImageResolver`
  updated to build a `DecodedImage`. `dotnet build` + `dotnet test` green;
  7768 tests pass, no count delta; image-paint goldens byte-exact (no pixel
  change). `06d-codecs` and `06i-skia-backend` now unblocked.
- 2026-05-19T02:55Z — superseded by wp:M5-skia-removal (commit 7b7ebd0): the Skia/Graphite native shim was removed from the engine and ImageSharp.Drawing 3 became the sole paint backend. This WP is left in place as history.
