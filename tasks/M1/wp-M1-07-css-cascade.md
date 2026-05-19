---
id: "wp:M1-07-css-cascade"
milestone: "M1"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-11T20:17:57Z"
branch: "wp-M1-07-css-cascade"
completed_at: "2026-05-11T20:17:57Z"
depends_on:
  - "wp:M1-06-css-selectors"
blocks:
  - "wp:M1-08-layout-block-inline"
subsystem: "Starling.Css"
plan_refs:
  - "browser-plan/06_CSS.md#cascade"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-07-css-cascade"
---

# wp:M1-07 — CSS cascade + properties + values + UA stylesheet

## Goal
Cascade for the layout-affecting and visual properties needed for M1 layout.
Bundled UA stylesheet.

## Acceptance
WPT `css/css-cascade/**` ≥ 80%.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T20:17Z — picked up after wp:M1-06; implemented style origins, property registry/shorthands, value parsing, computed style, style engine cascade, custom property resolution, and UA stylesheet defaults with focused cascade tests.
