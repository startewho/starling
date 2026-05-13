---
id: "wp:M2-07a-img-fetch-decode-paint"
parent: "wp:M2-07-network-end-to-end"
milestone: "M2"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-13T14:12:18Z"
branch: "main"
depends_on:
  - "wp:M2-05-http1"
  - "wp:M1-09-paint-display-list"
  - "wp:M1-08-layout-block-inline"
blocks:
  - "wp:M2-07b-live-https-fixture"
subsystem: "Tessera.Paint"
plan_refs:
  - "browser-plan/08_FONTS_PAINT.md#display-list"
  - "browser-plan/07_LAYOUT.md#replaced-elements"
  - "browser-plan/13_MILESTONES.md#m2--networking-and-live-html"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-07-network-end-to-end"
completed_at: "2026-05-13T14:32:16Z"
---

# wp:M2-07a — `<img>` fetch, decode, and paint

## Goal

Render `<img src="…">` end-to-end: fetch the bytes through
`TesseraHttpClient` (or read from `file://`), decode via `ImageSharp` (pure
managed, Rule-0 clean), size the replaced-element box during layout, and emit
a new `DrawImage` display item that the ImageSharp painter blits to the
output PNG. Without this, every real public page is missing the dominant
visual element even when DOM + CSS are perfect.

## Inputs

- `TesseraHttpClient` end-to-end fetch path (wp:M2-05 ✓).
- Display-list pipeline and ImageSharp painter (wp:M1-09 ✓).
- Block + inline layout with replaced-element seam (wp:M1-08 ✓).
- `Document.BaseUri` for relative `src` resolution.

## Outputs

- `src/Tessera.Paint/DisplayList/DisplayItem.cs` — add a `DrawImage` variant
  carrying a destination rect + a managed image handle (`ImageSharp.Image`
  or our own `DecodedImage` wrapper).
- `src/Tessera.Paint/DisplayList/DisplayListBuilder.cs` — emit `DrawImage`
  for `<img>` replaced boxes.
- `src/Tessera.Paint/Painter.cs` — blit the decoded image (alpha-aware) into
  the target buffer at the destination rect, scaled if needed.
- `src/Tessera.Layout/Inline/InlineLayout.cs` (and the block layout path for
  `display: block` images) — sizing rules: `width`/`height` attrs, then CSS
  `width`/`height`, then intrinsic image dimensions; preserve aspect ratio
  when only one axis is given.
- `src/Tessera.Engine/Engine.cs` — during render, walk the parsed DOM,
  resolve each `<img src="…">` to an absolute URL via `BaseUri`, fetch via
  `TesseraHttpClient` (or read `file://`), decode once, and stash the
  `DecodedImage` on the element (or a parallel map) so layout and paint can
  read it without a second fetch.
- `tests/Tessera.Paint.Tests/M1StaticRenderingGoldenTests.cs` (or a new
  sibling) — 3 golden tests:
  1. Inline PNG image from a local file (`testdata/images/dot.png`).
  2. Inline JPEG image (`testdata/images/swatch.jpg`).
  3. Broken `src`: paint the alt text inside the replaced-element box, not
     crash.

## Acceptance

- A page with `<p>before<img src="..."/>after</p>` lays out the image as an
  inline replaced box at its intrinsic (or attribute-overridden) size, with
  text flowing around it as if it were a glyph of that height.
- Decoded image bytes appear in the output PNG (verified by sampling pixels
  inside the expected rect against the source image).
- Broken `src` (404 / missing file / decode error) does not throw; it
  renders the `alt` attribute text using the existing text path, inside a
  zero-or-attribute-sized box.
- All three golden tests pass via byte-exact PNG comparison.
- Full repo `dotnet test` stays at the current count + at least 3 new tests.
- Rule-0 grep stays clean (ImageSharp is the only image dependency and is
  pure managed).

## Notes

- ImageSharp's `Image.Load(stream)` auto-detects PNG/JPEG/GIF/BMP/WebP;
  start with PNG + JPEG.
- Cache decoded images per absolute URL inside `TesseraEngine` so the same
  image referenced N times decodes once.
- Defer: `srcset`, `<picture>`, `loading="lazy"`, `object-fit`. Note in
  follow-up.
- This package does NOT add live HTTPS coverage — wp:M2-07b owns that.
- The painter blit op should respect the destination rect's alpha so a PNG
  with transparency composites correctly over background colors painted in
  earlier items.

## Handoff log

- 2026-05-13T00:00Z — agent-claude-cody, filed during MVP-path planning
  split-out of the catch-all wp:M2-07-network-end-to-end. Available to
  claim.
- 2026-05-13T14:12:18Z — claimed by agent-claude-cody, working on main
- 2026-05-13T14:32:16Z — merged; complete
