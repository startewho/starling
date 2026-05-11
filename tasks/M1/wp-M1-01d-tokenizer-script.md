---
id: "wp:M1-01d-tokenizer-script"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "available"
claimed_by: ""
claimed_at: ""
branch: ""
depends_on:
  - "wp:M1-01c-tokenizer-rcdata-rawtext"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Tessera.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
---

# wp:M1-01d — ScriptData state family

## Goal
ScriptData + 14 sub-states including the double-escape gymnastics (the
inner-HTML-of-`<script>` corner case).

## Acceptance
html5lib cases targeting `<script>` content pass; `</script>` only
terminates the element when not inside an escape.

## Handoff log
- 2026-05-11T15:20Z — created.
