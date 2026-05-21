---
id: wp:M5-css-15-text-decoration
milestone: M5
status: "complete"
claimed_by: "agent-claude-cody-textdecor"
claimed_at: "2026-05-20T00:00:00Z"
completed_at: "2026-05-20T00:00:00Z"
branch: "worktree"
depends_on:
  - wp:M5-skia-removal
  - wp:M1-09-paint-display-list
subsystem: Starling.Paint
plan_refs:
  - browser-plan/08_FONTS_PAINT.md#raster-backend
  - browser-plan/06_CSS.md
---

# wp:M5-css-15 — CSS Text Decoration 3 (lines + text-shadow)

## Goal

Real text decoration. Today the only decoration is a hardcoded underline
emitted as a `FillRect` hack (`DisplayListBuilder.cs:492`, gated by
`IsUnderlined`/`IsUnderlineValue` ~497–507). The longhands
`text-decoration-line/-style/-color/-thickness` and `text-underline-offset`
exist in `PropertyId` but are ignored beyond the boolean underline. Add
overline + line-through, decoration styles (solid/double/dotted/dashed/wavy),
color, thickness, offset — and `text-shadow`.

## Inputs

- `src/Starling.Css/Properties/PropertyId.cs` + `PropertyRegistry.cs` —
  `TextDecoration` shorthand → `TextDecorationLine`/`-Style`/`-Color`/
  `-Thickness`, `TextUnderlineOffset`, `TextUnderlinePosition` already present;
  add `TextShadow` (offset-x, offset-y, blur, color; comma multi-layer) parse.
- `src/Starling.Paint/DisplayList/DisplayItem.cs` — add your record(s) here
  (append at end; shared with two sibling paint WPs). A general
  `DrawLine(p0, p1, color, thickness, style)` or `DrawDecoration(...)` is the
  natural primitive; replace the FillRect underline hack with it.
- `src/Starling.Paint/DisplayList/DisplayListBuilder.cs` — `EmitTextFragments`
  (~449) and the underline block (~473–507). **This is your designated method**
  (gradients owns `EmitBackgroundImage`, radius/shadow owns
  `PaintBoxAndChildren`/`EmitBorders`).
- `src/Starling.Paint/Backend/ImageSharpBackend.cs` — the `DisplayItem` switch
  (~287). Use `Pens.Solid`/dashed pens; wavy ≈ a small sine path; double = two
  lines; text-shadow ≈ blurred offset text draw beneath the glyphs.

## Outputs

- A `CssTextShadow` typed value + parser; decoration line/style/color/thickness
  read from `ComputedStyle` in `EmitTextFragments`.
- New `DisplayItem` decoration primitive(s) + backend rendering for solid,
  double, dotted, dashed, wavy; underline/overline/line-through positioned from
  font metrics (baseline, x-height/cap-height, em); `text-underline-offset` and
  `text-decoration-thickness` honored.
- `text-shadow` painted under the text run (offset + blur + color, multi-layer).
- Tests: `tests/Starling.Css.Tests/` parse + `tests/Starling.Css.Spec.Tests/CssTextDecor3/`
  (create) `[Spec("css-text-decor-3", "https://www.w3.org/TR/css-text-decor-3/", §)]`
  `[SpecFact]`; `tests/Starling.Paint.Tests/` — keep/adapt the existing
  `Underlined_link_emits_text_and_underline_fill`-style test; add line-through +
  overline + colored-decoration probes.

## Acceptance

- `dotnet build && dotnet test` green (sandbox: `-p:UseAppHost=false`, `sixlabors.lic`).
- `text-decoration: underline` still renders (now via the decoration primitive);
  `line-through` and `overline` render at the right vertical positions.
- `text-decoration-color: red` on black text produces a red line; `…-thickness`
  changes the line weight; `…-style: dashed/wavy` is visually distinct.
- `text-shadow: 1px 1px 2px gray` paints a soft offset copy beneath the glyphs.

## Notes

- Decoration color defaults to `currentColor`; position underline below the
  baseline, line-through near mid x-height, overline above the cap line, using
  the font metrics already available via the text measurer.
- Shared-file etiquette: append-only in `DisplayItem.cs`, switch case grouped
  near the existing ones, edit only `EmitTextFragments` in the builder. Replace
  the FillRect underline hack rather than leaving both paths. Orchestrator
  reconciles the cross-WP switch/record merges.

## Handoff log

- 2026-05-20 — created + claimed by agent-claude-cody-textdecor (orchestrated batch).
- 2026-05-20 — implemented + completed by agent-claude-cody-textdecor.
  - Worktree branched from a stale base (pre-scaffold); fast-forward-merged
    `main` (40d1f02) into the worktree before starting so shared paint files
    were current.
  - `text-shadow`: new `PropertyId.TextShadow` (inherited), typed
    `CssTextShadow`/`CssTextShadowLayer` value + `ParseTextShadow` (none |
    `<color>? && <length>{2,3}`#, multi-layer comma split, absolute lengths →px,
    color leads/trails, currentColor = null layer color, malformed layers dropped).
  - Display list: appended `DrawTextDecoration` (lines flags, style enum,
    color, thickness, underline-offset, font identity) and `DrawTextShadow`
    (text + offset + blur + color) to `DisplayItem.cs`. Replaced the FillRect
    underline hack in `EmitTextFragments` with shadow-then-glyph-then-decoration
    emission; added decoration/shadow resolution helpers (color defaults to
    currentColor, thickness `auto`=0 sentinel, offset px/%/auto).
  - Backend: `DrawTextDecoration` resolves underline/overline/line-through
    y-positions from real SixLabors `FontMetrics` (UnderlinePosition/Thickness,
    StrikeoutPosition, Ascender); solid/double/dotted/dashed via `Pens.*`, wavy
    via cubic-Bézier sine path. `DrawTextShadow`: sharp = offset glyph copy;
    blurred = off-screen `Image<Rgba32>` text + `GaussianBlur` composited via
    `canvas.DrawImage`, staged in `pendingImageSources` to survive deferred
    rasterization. Added bounds cases to `LayerTreeBuilder` for both items.
  - Tests: `tests/Starling.Css.Tests/TextDecorationTests.cs` (10), spec
    `tests/Starling.Css.Spec.Tests/CssTextDecor3/` (5 SpecFacts), adapted the
    underline paint test + added line-through/overline/colored/dashed/thickness/
    shadow-order probes and backend raster tests (decoration styles, sharp+blur
    shadow). Regenerated `testdata/golden/snapshots/nginx.org.png` (underline
    now metric-positioned/stroked; SSIM 0.974 vs old golden, visually identical
    apart from the intended underline refinement).
  - Green: Css.Tests 515/1skip, Css.Spec.Tests 58/13pre-existing-skip,
    Paint.Tests 154/0, Engine render+snapshot 29/0.
  - Deferred/notes: wavy is a Bézier sine approximation (not exact spec
    waveform); `text-decoration-skip-ink` and `text-underline-position` (sub/
    over) are not honored; blurred-shadow off-screen layer renders at 1:1 CSS
    px so it softens slightly at high DPI.
