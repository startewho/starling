---
id: "wp:M0-03-paint-stub"
milestone: "M0"
status: "complete"
claimed_by: ""
claimed_at: ""
branch: ""
completed_at: "2026-05-11T14:57:00Z"
depends_on:
  - "wp:M0-02-common"
blocks:
  - "wp:M0-04-headless-cli"
  - "wp:M1-08-layout-block-inline"
  - "wp:M1-09-paint-display-list"
subsystem: "Starling.Paint"
plan_refs:
  - "browser-plan/08_FONTS_PAINT.md"
  - "browser-plan/14_AGENT_TASKS.md#wpm0-03-paint-stub"
---

# wp:M0-03 — Paint stub

## Goal
A `Painter` that takes a string + viewport size and emits a PNG with the text
drawn in a bundled sans-serif font.

## Inputs
- wp:M0-02-common complete.

## Outputs
- `src/Starling.Paint/Painter.cs`
- `src/Starling.Paint/FontResolver.cs`
- Bundled Inter font under `src/Starling.Paint/Resources/Fonts/`.

## Acceptance
- 2 unit tests pass in `tests/Starling.Paint.Tests`.
- Headless CLI can produce a PNG with text on a white background.

## Handoff log
- 2026-05-11T14:57Z — complete; Painter + FontResolver landed.
