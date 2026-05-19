---
id: "wp:M1-01b-tokenizer-tag-states"
parent: "wp:M1-01-html-tokenizer"
milestone: "M1"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T15:32:56Z"
branch: "wp-M1-01b-tokenizer-tag-states"
completed_at: "2026-05-11T15:38:00Z"
depends_on:
  - "wp:M1-01a-tokenizer-scaffold"
blocks:
  - "wp:M1-01h-tokenizer-html5lib"
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tokenizer"
---

# wp:M1-01b — Tokenizer tag + attribute states

## Goal
Implement: TagOpen, EndTagOpen, TagName, BeforeAttributeName, AttributeName,
AfterAttributeName, BeforeAttributeValue, AttributeValueDoubleQuoted,
AttributeValueSingleQuoted, AttributeValueUnquoted, AfterAttributeValueQuoted,
SelfClosingStartTag.

## Outputs
- `src/Starling.Html/Tokenizer/States/{TagOpen,EndTagOpen,TagName,...}.cs`
- Tests under `tests/Starling.Html.Tests/Tokenizer/TagStateTests.cs`.

## Acceptance
Hand-written tests for each state's transitions; html5lib subset for plain
tags (no entities, no scripts) at 100%.

## Notes
Pick this up after a:
1. `git pull && cat tasks/M1/wp-M1-01a-tokenizer-scaffold.md` for the contract.
2. Follow the file layout doc-decision: one file per state under
   `Tokenizer/States/`.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T15:32:56Z — claimed by agent-claude-cody, branch `wp-M1-01b-tokenizer-tag-states`
- 2026-05-11T15:38Z — landed:
  - `src/Starling.Html/Tokenizer/HtmlTokenizer.TagStates.cs` (new partial,
    all 12 states from §13.2.5.6–40 plus tag-builder helpers).
  - Updated `HtmlTokenizer.cs` to be a partial class with a central
    `Dispatch` switch each cluster registers against. EOF handling
    delegated to per-cluster `Step*Eof` methods.
  - Added structural-equality overrides on `StartTagToken` / `EndTagToken`
    (the synthesized record equality compared the attribute list by
    reference, which broke test asserts).
  - Fixed an M1-01a spec deviation: PreprocessedStream no longer remaps
    NULL — per spec §13.2.5.1, Data emits NULL verbatim, name-buffer
    states map to U+FFFD with a parse error.
  - 21 new tests in `tests/Starling.Html.Tests/Tokenizer/TagStateTests.cs`.
- 2026-05-11T15:38Z — `dotnet test` 70/70 green (was 49; +21 new).
  Marking complete.
