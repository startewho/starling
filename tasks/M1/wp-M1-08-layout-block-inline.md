---
id: "wp:M1-08-layout-block-inline"
milestone: "M1"
status: "blocked"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M1-07-css-cascade"
  - "wp:M0-03-paint-stub"
blocks:
  - "wp:M1-09-paint-display-list"
subsystem: "Tessera.Layout"
plan_refs:
  - "browser-plan/07_LAYOUT.md#block-formatting-context-bfc"
  - "browser-plan/14_AGENT_TASKS.md#wpm1-08-layout-block-inline"
---

# wp:M1-08 — Block + inline layout

## Goal
Block + inline formatting; margin collapse; basic text wrap; sizing.

## Acceptance
20 golden tests pass (paragraphs, headings, lists, nested divs, margin
collapse, text wrap).

## Handoff log
- 2026-05-11T15:20Z — created.
