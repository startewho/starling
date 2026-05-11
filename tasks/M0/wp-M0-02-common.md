---
id: "wp:M0-02-common"
milestone: "M0"
status: "complete"
claimed_by: ""
claimed_at: ""
branch: ""
completed_at: "2026-05-11T14:50:00Z"
depends_on:
  - "wp:M0-01-scaffold"
blocks:
  - "wp:M0-03-paint-stub"
  - "wp:M1-03-dom-core"
  - "wp:M1-05-css-tokenizer-parser"
  - "wp:M2-01-url-parser"
  - "wp:M3-01-js-lexer"
  - "wp:M4-01-event-loop"
subsystem: "Tessera.Common"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md"
  - "browser-plan/14_AGENT_TASKS.md#wpm0-02-common"
---

# wp:M0-02 — Common library primitives

## Goal
Establish `Result<T,E>`, `Maybe<T>`, lightweight diagnostics, and the
foundational primitives consumed by every other module.

## Inputs
- wp:M0-01-scaffold complete.

## Outputs
- `src/Tessera.Common/Result.cs`
- `src/Tessera.Common/Maybe.cs`
- `src/Tessera.Common/Diagnostics/*.cs`

## Acceptance
- 7 unit tests pass in `tests/Tessera.Common.Tests`.

## Notes
- `Rope` and `CodePoint` are still pending — they're needed by the HTML
  tokenizer (wp:M1-01) and the JS lexer (wp:M3-01), so they may be added
  there or in a follow-up `wp:M0-02b-rope-codepoint` if/when needed.

## Handoff log
- 2026-05-11T14:50Z — complete; Result/Maybe/Diagnostics landed.
