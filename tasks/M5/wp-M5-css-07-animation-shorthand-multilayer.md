---
id: wp:M5-css-07-animation-shorthand-multilayer
milestone: M5
status: "available"
claimed_by: null
claimed_at: null
branch: "main"
depends_on: []
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-07-animation-shorthand-multilayer — Multi-layer animation shorthand

## Goal

Lift `PropertyRegistry.ExpandAnimation` (`PropertyRegistry.cs:482` TODO) from
single-layer to comma-separated multi-layer, and add a helper that converts
the cascaded longhand list values back into
`IReadOnlyList<AnimationDeclaration>` for the compositor (wp:M5-css-09).

## Inputs

- `PropertyRegistry.ExpandAnimation` (single-layer today).
- `AnimationDeclaration` record + enums (wp:M5-css-05).
- CSS Animations 1 §4.1 list expansion rules — shorter parallel lists repeat
  from the start to match the length of `animation-name`.

## Outputs

- `PropertyRegistry.ExpandAnimation` accepts comma-separated layers and emits
  parallel `CssList<…>` values for each Animation* longhand.
- New `AnimationCompositor.BuildDeclarations(ComputedStyle)` (in
  `Starling.Css.Animations`) returns the per-layer declarations.
- Tests:
  - `animation: a 1s, b 2s linear infinite` → 2 declarations with the right
    durations/timing/iteration.
  - `animation-name: a, b, c; animation-duration: 1s, 2s` → c gets duration 1s
    (cycles back).

## Acceptance

- Existing single-layer parsing still works (regression-tested via
  `PropertyRegistryTests`).
- Round-trip from shorthand → longhands → `AnimationDeclaration` list.

## Notes

- Don't wire to the compositor yet — that's wp:M5-css-09. This WP just
  produces the data shape.
- `animation-composition` is parsed if present but otherwise ignored.

## Handoff log

- 2026-05-19T16:25Z — created (agent-copilot-claude-opus-4.7).
