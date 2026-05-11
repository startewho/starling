---
id: "wp:M3-01-js-lexer"
milestone: "M3"
status: "available"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M0-02-common"
blocks:
  - "wp:M3-02-js-parser"
subsystem: "Tessera.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#lexer"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-01-js-lexer"
---

# wp:M3-01 — JavaScript lexer

## Goal
ES2024 lexer producing token stream consumed by the parser.

## Acceptance
Test262 lexer category 100%; property tests on random valid sources.

## Notes
Unblocked at start of M0; safe for a parallel agent to take.

## Handoff log
- 2026-05-11T15:20Z — created.
