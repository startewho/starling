---
id: "wp:M3-06g-skia-shim"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-shim"
claimed_at: "2026-05-14T16:28:41Z"
completed_at: "2026-05-14T16:39:04Z"
branch: "main"
depends_on:
  - "wp:M3-06b-native-build"
blocks:
  - "wp:M3-06h-skia-interop"
subsystem: "native"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06g-skia-shim — custom C ABI shim (long pole #2)

## Goal

Phase 2: write `native/shim/tessera_skia.{h,cpp}` — a small custom `extern "C"`
ABI (~600–1000 lines) exposing exactly what the Tessera display list needs, and
nothing more. Statically link `libskia` + Dawn into a single
`libtessera_skia.{dylib,dll,so}` per RID so .NET loads one native file with no
transitive native-dep hell. WebGPU types **never** cross into .NET — they stay
behind opaque `void*` handles. This is long pole #2.

## Inputs

- `wp:M3-06b-native-build` complete: per-RID Skia + Dawn static libs exist under
  `runtimes/<rid>/native/` (built out-of-band).
- C++ / CMake fluency; SkiaSharp's `libSkiaSharp` available **only as a
  reference** for the non-Graphite calls (it has zero Graphite coverage — do not
  extend it).

## Outputs

- `native/shim/tessera_skia.h` + `tessera_skia.cpp` — the custom `extern "C"`
  ABI. Minimal C surface:
  - context/device lifecycle (`ts_context_create`, destroy);
  - surface create + canvas;
  - the 4 `DisplayItem` ops: `fill_rect`, `stroke_rect`, `draw_text` (shaped
    glyph runs), `draw_image` (from RGBA pixels);
  - font/typeface + `shape_text` + `font_metrics`;
  - `flush_and_submit` + `read_pixels` for golden/headless readback.
- `native/shim/CMakeLists.txt` — statically links `libskia` + Dawn into a single
  `libtessera_skia.{dylib,dll,so}` per RID.
- A tiny C++ smoke harness that fills a rect and reads back a PNG.

## Acceptance

- `tessera_skia.h` exposes only the minimal surface above; WebGPU/`wgpu::` types
  appear nowhere in the header — they are opaque `void*` handles.
- The CMake build produces a single statically-linked
  `libtessera_skia.{dylib,dll,so}` per RID (no transitive native deps to ship).
- The C++ smoke harness creates a context + surface, fills a rect via
  `fill_rect`, calls `flush_and_submit` + `read_pixels`, and writes a correct
  PNG.
- The shim does **not** extend SkiaSharp's `libSkiaSharp`.
- PNG **encode** stays in C# for now (`read_pixels` → raw RGBA → existing
  encoder) — `Ssim.cs` / `PngComparison.cs` are untouched.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 2).
- The opaque-`void*` insulation is the key defense against WebGPU C-API churn —
  treat it as a hard rule, not a style preference.
- `06h-skia-interop` consumes this: the `[LibraryImport("tessera_skia")]`
  bindings mirror exactly this header.
- Dawn `Instance`/`Adapter`/`Device` creation inside `ts_context_create` is
  fleshed out in `06h` (Phase 4 wiring) — this package can stub the device path
  enough for the rect smoke test.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
- 2026-05-14T16:39:04Z — complete (agent-claude-cody-shim). **osx-arm64 only**;
  win/linux shims wait on those native builds. Summary:
  - `tessera_skia.cpp` implemented for real against Skia Graphite + Dawn
    (chrome/m140 headers). Context = `dawn::native::Instance` →
    `EnumerateAdapters` (Metal) → `Adapter::CreateDevice` → `wgpu::Device` +
    `Queue` → `skgpu::graphite::ContextFactory::MakeDawn` → `Context` +
    `Recorder`. Surfaces via `SkSurfaces::RenderTarget`. The 4 DisplayItem ops
    via `SkCanvas` drawRect/drawGlyphs/drawImageRect. Flush =
    `Recorder::snap` → `Context::insertRecording` → `submit(SyncToCpu::kYes)`.
  - `libtessera_skia.dylib` (28 MB) builds + links; the C smoke harness
    (`smoke_test.c`) passes — context+surface, clear→fill_rect→flush→readback,
    asserts background + rect pixels exact.
  - **C-ABI surface changes vs the scaffold (2):**
    1. `ts_read_pixels` now takes `TsContext*` as well as `TsSurface*` —
       Graphite has no synchronous `SkSurface::readPixels`; readback goes
       through `Context::asyncRescaleAndReadPixels` + a `SyncToCpu` submit,
       both of which need the `Context`. 06h's `[LibraryImport]` binding must
       mirror this.
    2. No signature change, but `ts_shape_text` is **not** HarfBuzz complex
       shaping: the native build (06b) did not stage the `skshaper` module
       (no headers, no `SkShaper*` symbols in any staged `.a`). The shim uses
       `SkFont::textToGlyphs` + advance-based pen positioning — correct glyph
       ids + LTR positions for simple scripts, sufficient for golden tests,
       but no ligatures/marks/bidi/kerning-features. Re-staging `skshaper`
       (+`skunicode`) and swapping in `SkShapers::HB::*` is a follow-up.
  - **Build/link surprises:**
    - Skia's `is_official_build=true` `libskia.a` already folds in ALL of
      Dawn (`dawn_native`/`dawn_proc`/`dawn_platform`, Dawn's common/utils,
      SPIRV-Tools) plus jpeg/expat/the HarfBuzz C++ layer. Linking the
      separate staged `libdawn_*.a`/`libcommon.a`/`libutils.a`/`libspvtools*.a`
      produced thousands of duplicate symbols. Final link line: `libskia.a`
      (`-force_load`) + only the third-party C archives it leaves undefined —
      `libharfbuzz`, `libpng`, `libwebp`(+sse41), `libzlib`, `libskcms`,
      `libwuffs`, `libdng_sdk`, `libpiex` — plus the macOS frameworks.
    - Header sourcing: 06b stages the *contents* of Skia's `include/` into
      `runtimes/<rid>/native/include/skia/`, but Skia headers self-reference
      `#include "include/core/..."`, and Dawn's *generated* headers
      (`dawn/webgpu_cpp.h`, `dawn/dawn_proc_table.h`) aren't staged at all. So
      the shim CMake builds headers from the `third_party/skia` source
      checkout + the `native/out/<rid>/gen` Dawn gen dir; libs still come from
      `runtimes/<rid>/native/`. 06b's staging step should be revisited to
      package the source-tree header layout (incl. Dawn gen headers) if the
      checkout is meant to be disposable.
    - Graphite render targets reject `kUnpremul` alpha — surfaces are created
      `kPremul`; the C boundary stays unpremul RGBA8888 for upload/readback
      (Skia converts on the fly).
    - The shim TU is compiled as Objective-C++ (CoreText `SkFontMgr`).
