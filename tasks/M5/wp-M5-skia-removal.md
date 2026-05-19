---
id: wp:M5-skia-removal
milestone: M5
status: complete
claimed_by: agent-copilot-claude-opus-4.7
claimed_at: 2026-05-19T02:30Z
branch: main
completed_at: 2026-05-19T02:50Z
depends_on: []
blocks: []
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md
  - AGENTS.md#interop-policy
---

# wp:M5-skia-removal — Remove Skia/Graphite native shim, ImageSharp.Drawing 3 is sole paint backend

## Goal

Rip the native Skia/Graphite rasterizer (`src/Starling.Skia` + `native/`
shim) out of the tree and promote ImageSharp.Drawing 3 — which was already
present behind the `EnableImageSharpDrawing3` / `#if STARLING_IMAGESHARP_DRAWING`
toggles — to the sole, unconditional paint backend. Restores the engine to
fully pure-managed: a fresh checkout's `dotnet build` succeeds without any
non-.NET prerequisites.

## What was removed

- `src/Starling.Skia/` (entire project: Interop, handles, exception types).
- `tests/Starling.Skia.Tests/` (entire test project).
- `src/Starling.Paint/Backend/SkiaGraphiteBackend.cs`,
  `src/Starling.Paint/SkiaTextMeasurer.cs`.
- `native/` (`build-skia.sh`/`.ps1`, CMake shim, smoke test, README).
- `.github/workflows/native.yml` and the "Restore native Skia shim" step in
  `ci.yml`.
- The `Starling.Skia` solution entries (`dotnet sln remove`).
- `EnableImageSharpDrawing3` auto-toggle in `Directory.Build.props`.
- `Starling.Skia` `InternalsVisibleTo` block.
- The conditional `SixLabors.ImageSharp` 3.1.12 / 4.0 split in
  `Directory.Packages.props` (now unconditionally 4.0.*).

## What was rewritten

- `src/Starling.Paint/Backend/PaintBackendSelector.cs` — enum collapsed to
  `{ ImageSharp, ImageSharpWebGpu }`; default is `ImageSharp`; unknown values
  throw loudly.
- `src/Starling.Paint/Backend/ImageSharpBackend.cs` and
  `src/Starling.Paint/ImageSharpTextMeasurer.cs` —
  `#if STARLING_IMAGESHARP_DRAWING` gates removed; doc-comments rewritten to
  drop SkiaGraphiteBackend / SkiaTextMeasurer crefs.
- `src/Starling.Paint/ImageSharpFontLookup.cs` — un-gated.
- `src/Starling.Paint/Interop/WgpuNativeLoader.cs` — un-gated;
  pragma-disable comment no longer cites `Starling.Skia.Interop.NativeLoader`.
- `src/Starling.Paint/FontResolver.cs` — stripped to a no-op shim with the
  internal `ExpandFamilies` helper for CSS generic-keyword fallback chains.
  Public surface `Default`/ctor/`Dispose` preserved so existing callers
  (`ImageSharpBackend`, `ImageSharpTextMeasurer`, tests) keep building.
- `src/Starling.Paint/FontFaceRegistry.cs` — replaced `SkTypeface` storage
  with raw SFNT `byte[]` storage; public `TryAdd` surface preserved. The
  WOFF/WOFF2 unwrap path still runs at insert time. Internal `TryGet`
  exists as the seam for future ImageSharp web-font integration.
- `src/Starling.Paint/Painter.cs` — four doc-comment blocks updated.
- `tests/Starling.Paint.Tests/PaintBackendSelectorTests.cs` — rewritten to
  cover the new ImageSharp-only enum.
- `tests/Starling.Paint.Tests/EndToEndRenderTests.cs` — Skia-named test
  renamed to `Backend_reuses_context_for_two_sequential_renders` and
  retargeted at `ImageSharpBackend`.
- `tests/Starling.Bindings.Tests/WindowDocumentTests.cs` — drive-by IDE0005
  fix (unused `using Starling.Bindings;` removed; redundant under
  `namespace Starling.Bindings.Tests`).
- `testdata/golden/snapshots/nginx.org.png` — re-vendored with
  `STARLING_UPDATE_GOLDENS=1` because the prior golden was rendered by Skia.

## Docs / policy

- `AGENTS.md` — "Interop policy" rewritten: Codecs is now the lone native
  seam. The "native Skia shim is a hard requirement" paragraph was replaced
  with a "Paint backend: ImageSharp.Drawing 3 (managed)" note.
- `README.md` — Paint row in the status table, native-shim Quickstart
  callout, `native/` directory in the repo-layout block, and Interop-policy
  section all updated.
- `.github/workflows/ci.yml` — `Restore native Skia shim` step deleted;
  interop-seam grep comment updated to name only `Starling.Codecs`.

## Acceptance

- `dotnet build Starling.sln -c Debug` — succeeds.
- `dotnet test Starling.sln -c Debug --no-build` — green except for two
  pre-existing failures that are unrelated to this change:
  - `Starling.Paint.Tests.DisplayListBuilderTests.Underlined_link_emits_text_and_underline_fill`
    (renderer-neutral DisplayList assertion; failed under both backends pre-removal).
  - `Starling.Codecs.Tests.NativeImageDecoderTests.DecodesPngCornerPixels`
    (alpha 0xFF vs 0x80; reproduces against `git stash` baseline, so
    pre-existing in `Starling.Codecs`).

## Follow-ups (not blockers for this WP)

All four follow-ups are now resolved (commit-pair `7b7ebd0` + the followup
commit recorded in the handoff log below). See each linked file for detail:

- ✅ `tasks/M5/wp-M5-css-02-transform-paint.md` rewritten to target
  ImageSharp.Drawing 3's per-primitive `DrawingOptions.Transform`
  (Matrix4x4) with the backend owning its own transform stack — the Skia
  Save/Restore/Concat plan is gone.
- ✅ `FontFaceRegistry` wired into `ImageSharpFontLookup.LoadCollection`
  via the new public `EnumerateRegisteredSfnt()`; `ImageSharpBackend` and
  `ImageSharpTextMeasurer` thread the per-document registry through.
  Locked down by `ImageSharpFontLookupTests.Registered_web_font_bytes_appear_in_loaded_collection`.
- ✅ Doc-comment-only Skia mentions cleaned up in
  `src/Starling.Css/FontFace/FontFaceRule.cs`,
  `src/Starling.Paint/WebFonts/WoffDecoder.cs`,
  `src/Starling.Paint/WebFonts/Woff2Decoder.cs`.
- ✅ Each `tasks/M3/wp-M3-06*.md` handoff log got a one-line "superseded
  by wp:M5-skia-removal" addendum.

## Handoff log

- 2026-05-19T02:50Z — agent-copilot-claude-opus-4.7 — Skia removal complete;
  full sln builds; tests green modulo the two documented pre-existing
  failures; AGENTS.md/README.md/ci.yml updated; nginx.org golden re-vendored.
- 2026-05-19T03:15Z — agent-copilot-claude-opus-4.7 — all four follow-ups
  landed in a second commit: FontFaceRegistry → ImageSharpFontLookup wiring
  with a new round-trip test, wp:M5-css-02 WP rewritten for ImageSharp.Drawing 3,
  M3-06 addenda, doc-comment cleanup. Also dropped the now-redundant
  `STARLING_IMAGESHARP_DRAWING` `#if` guard in the three paint tests (the
  define was only set by the removed `EnableImageSharpDrawing3` switch).
  Full `dotnet test` green except the same pre-existing
  `Underlined_link_emits_text_and_underline_fill` baseline failure.
