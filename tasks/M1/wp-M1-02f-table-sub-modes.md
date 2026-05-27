---
id: "wp:M1-02f-table-sub-modes"
parent: "wp:M1-02-html-tree-builder"
milestone: "M1"
status: "available"
claimed_by: ""
claimed_at: ""
branch: "main"
depends_on:
  - "wp:M1-02b-tree-construction-html5lib"
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tree-builder"
---

# wp:M1-02f — Table insertion sub-modes (§13.2.6.4.9-15)

## Goal

Split out the table-related insertion modes that `wp:M1-02` collapsed into
`InBody`. The plan's `InsertionMode.cs` comment names this exact gap:
"Frameset and complex table sub-modes are folded into InBody in v1."

## Inputs

- §13.2.6.4.9 InTable, .10 InTableText, .11 InCaption, .12 InColumnGroup,
  .13 InTableBody, .14 InRow, .15 InCell, plus InSelectInTable.
- `wp:M1-02b` runner — `tables01.dat`, large chunks of `webkit01.dat` and
  `webkit02.dat` are table-heavy.

## Outputs

- New `InsertionMode` enum values (8 of them).
- One `Handle…` method per new mode, dispatching from `HandleToken`.
- "Foster parenting" logic — when content lands inside a table where it
  shouldn't go, the spec relocates it before the table. This is the headline
  weirdness of these modes; implement it literally.
- `OpenElementStack.HasInTableScope` is already there (per
  `browser-plan/04_HTML_PARSING.md` it's listed); confirm it works.

## Acceptance

- `tables01.dat` ≥ 90% in `wp:M1-02b`.
- `webkit01.dat` and `webkit02.dat` table-cases at parity with their non-table
  cases (spot-check via the failures sidecar).
- Overall floor ratcheted.

## Notes

- Foster parenting is the most-debugged bit. Spec example: a `<p>` start tag
  in InTable lands *before* the `<table>` element, not inside.
- The `InSelectInTable` mode is a footnote that overrides `InSelect` when a
  table-row context exists. Don't forget it.
