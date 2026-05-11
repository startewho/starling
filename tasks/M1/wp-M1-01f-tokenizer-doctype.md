---
id: "wp:M1-01f-tokenizer-doctype"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "available"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M1-01a-tokenizer-scaffold"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
---

# wp:M1-01f — DOCTYPE states

## Goal
Doctype + 11 sub-states + BogusDoctype. Quirks classification (no-quirks /
limited-quirks / quirks) is owned by the tree builder; the tokenizer just
emits a faithful `DoctypeToken`.

## Acceptance
html5lib doctype cases pass.

## Handoff log
- 2026-05-11T15:20Z — created.
