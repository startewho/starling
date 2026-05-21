---
id: wp:M5-img-svg-decode
milestone: M5
status: "complete"
claimed_by: "agent-claude-cody-svg"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: "2026-05-20T00:00:00Z"
branch: "worktree-agent-a451afd09bfde571a"
depends_on:
  - wp:M2-07a-img-fetch-decode-paint
  - wp:M5-skia-removal
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md#raster-backend
---

# wp:M5-img-svg-decode — managed SVG image decoding (`<img src=*.svg>`)

## Goal

Make `<img src=*.svg>` and SVG image references rasterize instead of failing.
Starling's image pipeline decodes via OS-native bitmap codecs
(`Starling.Codecs` → ImageIO/WIC/libjpeg/libpng/libwebp), which cannot decode
SVG — it is XML/vector, not a raster container. Rendering real sites logged
8× `engine: Image decode failed … .svg: Unrecognised image format: no
PNG/JPEG/WebP/GIF/BMP signature in the leading bytes` (e.g. McMaster's logo and
search icons; nginx.org's logos). This WP adds a **pure-managed SVG → raster
path** that rasterizes via the existing ImageSharp.Drawing 3 paint stack.

This is a pragmatic **first cut** — the primitives real-world icons use, not
full SVG 1.1/2. Scope and deferrals are listed below.

## Layering decision (why it lives where it lives)

- **Sniff in `Starling.Codecs`** (`ImageFormatSniffer.Detect` gains an
  `ImageFormat.Svg` arm + a public `ImageFormatSniffer.LooksLikeSvg` /
  `NativeImageDecoder.IsSvg`). SVG has no magic number; it is detected from a
  text prefix (`<?xml`, `<svg`, `<!DOCTYPE svg`, optionally after a UTF-8 BOM /
  leading whitespace / a leading comment). The native `Decode` now throws a
  guidance message if SVG bytes reach it, so the seam stays honest. Codecs
  takes **no** new dependency — it still references only `Starling.Common`.
- **Rasterize in `Starling.Paint`** (`Starling.Paint/Svg/`). This is the only
  engine project that references ImageSharp.Drawing, which is required to
  rasterize vectors. Putting the rasterizer here keeps the managed-first
  interop policy intact (no native code) and avoids a backwards
  `Codecs → Paint` dependency.
- **Wire at the engine call site** (`Starling.Engine/ImageFetcher.cs`):
  `NativeImageDecoder.IsSvg(bytes) ? SvgImageDecoder.Decode(bytes) :
  NativeImageDecoder.Decode(bytes)`. Both return the same backend-neutral
  `DecodedImage` (straight RGBA8888, top-down, tightly packed), so nothing
  downstream changes. `SvgDecodeException` joins the fail-soft catch.

## Outputs

- `src/Starling.Codecs/ImageFormat.cs` — `ImageFormat.Svg`; text-prefix sniff;
  `LooksLikeSvg`.
- `src/Starling.Codecs/NativeImageDecoder.cs` — public `IsSvg`; SVG rejected
  from the native path with a pointer to the managed rasterizer.
- `src/Starling.Paint/Svg/SvgImageDecoder.cs` — public entry; parses (via
  `System.Xml.Linq`, no DTD fetch), computes intrinsic size + viewBox →
  user-space transform, walks the element tree, rasterizes on the
  ImageSharp.Drawing `DrawingCanvas`, returns a `DecodedImage`.
- `src/Starling.Paint/Svg/SvgPathParser.cs` — `d` parser: `M m L l H h V v
  C c S s Q q T t A a Z z`; elliptical arcs via W3C F.6.5
  endpoint→center → cubic Béziers.
- `src/Starling.Paint/Svg/{SvgColor,SvgStyle,SvgStyleSheet,SvgTransform,
  SvgDecodeException}.cs` — colors, presentation/style cascade, flat
  `<style>` class rules, transform list, exception.
- `src/Starling.Engine/ImageFetcher.cs` — SVG routing.
- Tests: `tests/Starling.Codecs.Tests/{ImageFormatSnifferTests,
  NativeImageDecoderTests}.cs` (sniff + routing); `tests/Starling.Paint.Tests/
  Svg/{SvgImageDecoderTests,SvgPathParserTests}.cs` (`[Spec("svg11", …)]`).
- Fixtures: `testdata/images/svg/*.svg` (5 real McMaster icons).
- Regenerated golden: `testdata/golden/snapshots/nginx.org.png` — nginx's two
  SVG logos now rasterize (SSIM moved; the new golden was visually verified).

## Supported (first cut)

- Elements: `<svg>` (width/height/viewBox), `<g>`, `<a>`, `<path>`, `<rect>`
  (incl. rx/ry), `<circle>`, `<ellipse>`, `<line>`, `<polyline>`, `<polygon>`.
  Unknown elements ignored gracefully (still descended for renderable
  children).
- `<path d>`: all of `M m L l H h V v C c S s Q q T t A a Z z`, absolute +
  relative, run-together numbers, smooth-curve control-point reflection,
  elliptical arcs (incl. zero-radius → line, out-of-range radius correction).
- Presentation: `fill`, `stroke`, `stroke-width`, `fill-rule`
  (nonzero/evenodd), `opacity` / `fill-opacity` / `stroke-opacity`, `color`;
  via attributes, `style="…"`, and flat `<style>` class/element rules
  (`.cls{…}` / `tag{…}`). `fill="none"`, `currentColor` (resolved against a
  passed-in color, default black). Colors: `#rgb`/`#rgba`/`#rrggbb`/`#rrggbbaa`,
  `rgb()`/`rgba()` (incl. `%`), named (via ImageSharp `Color.TryParse`),
  `transparent`.
- `transform`: `translate`, `scale`, `rotate` (incl. about a center),
  `matrix`, `skewX`, `skewY`, composed in document order.
- Intrinsic size: width/height attrs → else viewBox → else 150×150 (CSS
  replaced-element default). `viewBox` mapped with `xMidYMid meet`. Output
  clamped to 4096 px per side.

## Deferred / unsupported (documented gaps)

- Gradients (`linearGradient`/`radialGradient`), patterns, `<image>`, `<use>`/
  `<symbol>` references, `<text>`/`<tspan>`, masks, clip paths, filters — all
  ignored (the container elements are skipped). Real icons in scope don't need
  them; sites that do will degrade to a partial/blank render, not a crash.
- `preserveAspectRatio` values other than the default `xMidYMid meet` (e.g.
  `none`, `slice`, alignment variants) are not honored.
- Percentage lengths (e.g. `width="50%"`, `stroke-width="2%"`) are ignored
  (treated as absent / unparsed).
- Units other than px/user-units are stripped, not converted (pt/mm/em/etc.
  are treated as user units).
- CSS in `<style>` is a flat single-simple-selector resolver only: no
  combinators/attribute selectors/`!important`/specificity beyond
  "class beats element"; a compound selector is reduced to its last token.
- `stroke-linecap`/`linejoin`/`dasharray`/`miterlimit`, `paint-order`, and
  non-solid paints are not applied (solid stroke only).
- Distinct `rx != ry` on `<rect>` corners is averaged to a single radius.

## Acceptance

- `dotnet build && dotnet test` green (sandbox uses `-p:UseAppHost=false` and
  the repo-root `sixlabors.lic`).
- SVG bytes sniff as the new format; PNG/JPEG/WebP/GIF/BMP sniffs unchanged.
- Each McMaster fixture decodes without throwing, to the expected intrinsic
  dimensions, with non-empty / non-fully-transparent pixels.
- Golden colour spot-checks pass (red rect, blue circle, even-odd hole, group
  translate, fill-opacity, class-rule fill).
- Path-data unit tests cover relative+absolute commands and arcs.

## Handoff log

- 2026-05-20 — created + completed in one session (agent-claude-cody-svg) on an
  isolated worktree branched from `origin/main` (9d3ec43). NOTE: the local
  `main` pointer was 21 commits ahead (M3 JS work) at branch time; this WP
  touches `Starling.Codecs`/`Starling.Paint`/`Starling.Engine` image-pipeline
  files only, disjoint from that JS work, so the orchestrator can merge/rebase
  cleanly. The INDEX row was added single; reconcile on merge.
  - Sniffer + managed rasterizer + engine wiring as above. 34 SVG decoder/path
    tests (`Starling.Paint.Tests`, `[Spec("svg11", …)]`/`[SpecFact]`) +
    8 sniffer/routing tests (`Starling.Codecs.Tests`). All green:
    Codecs.Tests 27, Paint.Tests 138, Engine.Tests 112.
  - nginx.org golden re-vendored (its two SVG logos now render) and visually
    verified before committing the new PNG.
  - `sixlabors.lic` is gitignored; copied into the worktree root for the build
    (not committed).
