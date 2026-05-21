---
id: wp:M5-css-13-gradients
milestone: M5
status: "claimed"
claimed_by: "agent-claude-cody-gradients"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: ""
branch: "worktree"
depends_on:
  - wp:M5-skia-removal
  - wp:M1-09-paint-display-list
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md#raster-backend
  - browser-plan/06_CSS.md
---

# wp:M5-css-13 — CSS Images 3 gradients (parse + paint)

## Goal

Make CSS gradients render. Today `linear-gradient`/`radial-gradient`/
`conic-gradient` (and `repeating-*`) are recognized only as raw
`CssFunctionValue` (`src/Starling.Css/Properties/PropertyRegistry.cs:595`) and
the paint backend has no gradient primitive — so `background-image: …gradient()`
draws nothing. Add a typed gradient value, emit a gradient display item, and
rasterize it with ImageSharp brushes.

## Inputs

- `src/Starling.Css/Values/CssValue.cs` — value record hierarchy
  (`CssColor`, `CssAngle`, `CssLength`, `CssPercentage`, `CssFunctionValue`).
- `src/Starling.Css/Values/ColorParser.cs`, `CssValueParser.cs` — reuse for
  color-stop colors and `<length-percentage>` positions.
- `src/Starling.Paint/DisplayList/DisplayItem.cs` — add your record here
  (append at end; this file is shared with two sibling paint WPs).
- `src/Starling.Paint/DisplayList/DisplayListBuilder.cs` — `EmitBackgroundImage`
  (~257) currently resolves a URL and blits; add a gradient arm. **This is your
  designated method** — sibling WPs edit `PaintBoxAndChildren`/`EmitBorders`/
  `EmitTextFragments`, not this one.
- `src/Starling.Paint/Backend/ImageSharpBackend.cs` — the `DisplayItem` switch
  (~287, cases `FillRect`/`StrokeRect`/`DrawText`/`DrawImage`). Add your case
  adjacent to `DrawImage`. ImageSharp.Drawing exposes
  `LinearGradientBrush` / `RadialGradientBrush` (`SixLabors.ImageSharp.Drawing.Processing`).

## Outputs

- `src/Starling.Css/Values/CssGradient.cs` — typed `CssGradient` record(s):
  kind (linear/radial/conic + repeating), direction (`<angle>` or
  `to <side-or-corner>`), radial shape/size/position, ordered color stops
  (`CssColor` + optional `<length-percentage>` position), and a parser over
  `CssFunctionValue` (mirror `CssTransformParser`).
- New `DisplayItem` record (e.g. `FillGradient(Rect Bounds, CssGradient Gradient)`),
  ImageSharp backend case mapping stops → gradient brush and filling the box,
  and the `EmitBackgroundImage` gradient arm.
- Tests: `tests/Starling.Css.Tests/` parse cases + `tests/Starling.Css.Spec.Tests/CssImages3/`
  (create) `[Spec("css-images-3", "https://www.w3.org/TR/css-images-3/", §)]`
  `[SpecFact]`; `tests/Starling.Paint.Tests/` golden/pixel-probe (e.g. a
  left-to-right red→blue linear gradient: left pixels red-ish, right blue-ish).

## Acceptance

- `dotnet build && dotnet test` green (sandbox: `-p:UseAppHost=false`, `sixlabors.lic`).
- `linear-gradient(90deg, red, blue)` fills a box left→right; pixel probes at
  10% and 90% width are red-dominant / blue-dominant respectively.
- `to right`, explicit stop positions, ≥3 stops, and `radial-gradient(circle, …)`
  parse to the typed value and paint.
- Unsupported/edge syntax fails soft (no throw; box left unpainted), consistent
  with the existing fail-soft image path.

## Notes

- Scope first cut: linear + radial (circle/ellipse, common sizes) + the
  `repeating-` variants if cheap. `conic-gradient` may be stubbed/deferred if
  ImageSharp lacks a direct brush — document it if so.
- Coordinate the shared files: append-only in `DisplayItem.cs`, one new switch
  case in the backend, only `EmitBackgroundImage` in the builder. Orchestrator
  reconciles the switch/record merges across the three paint WPs.

## Handoff log

- 2026-05-20 — created + claimed by agent-claude-cody-gradients (orchestrated batch).
