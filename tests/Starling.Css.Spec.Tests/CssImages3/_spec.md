---
id: css-images-3
url: https://www.w3.org/TR/css-images-3/
title: CSS Images Module Level 3
status: CR
fetched: 2026-05-20
sections_covered:
  - "3 — Gradients (linear, radial)"
  - "3.1 — linear-gradient()"
  - "3.2 — radial-gradient()"
  - "3.4 — Color stops & gradient line"
implementation:
  module: src/Starling.Css (parsing) + src/Starling.Paint (rendering)
  status: minimal
  notes: |
    `linear-gradient()` / `radial-gradient()` and their `repeating-` variants
    parse to a typed `CssGradient` (`src/Starling.Css/Values/CssGradient.cs`)
    and paint via a `FillGradient` display item mapped to ImageSharp.Drawing's
    `LinearGradientBrush` / `RadialGradientBrush`. `conic-gradient()` parses
    but is not paintable: ImageSharp.Drawing 3 has no conic/sweep brush, so
    conic gradients are filtered out before reaching the backend (fail-soft,
    box left unpainted). Explicit radial radius lengths are recognised but not
    yet honored for sizing.
---

# CSS Images and Replaced Content 3 — Gradients

Spec: <https://www.w3.org/TR/css-images-3/>

## Section map

| Section | Covered by |
|---|---|
| 3.1 — `linear-gradient()` | `GradientParseTests` |
| 3.2 — `radial-gradient()` | `GradientParseTests` |
| 3.4 — Color stops & positions | `GradientParseTests` |
| 3.x — `conic-gradient()` (deferred) | `GradientParseTests` |
