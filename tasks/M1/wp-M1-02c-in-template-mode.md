---
id: "wp:M1-02c-in-template-mode"
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

# wp:M1-02c — InTemplate insertion mode

## Goal

Replace the placeholder `<template>` handling in `HandleInHead` (added 2026-05-27
to break the InHead ↔ AfterHead stack overflow on netclaw.dev) with the real
WHATWG HTML model: a template-content document fragment plus the "in template"
insertion mode (§13.2.6.4.4 + §13.2.6.4.16) and the template insertion-mode
stack (§13.2.4.5 step 7).

## Inputs

- The placeholder in `src/Starling.Html/TreeBuilder/HtmlTreeBuilder.cs`
  (`HandleInHead` template case at line ~483 — inserts then immediately pops).
- `InsertionMode.cs` — currently lacks `InTemplate`.
- `wp:M1-02b`'s runner — gates progress via the per-file pass rate of
  `template.dat`.

## Outputs

- `InsertionMode.InTemplate` (new enum value).
- A template insertion-mode stack on `HtmlTreeBuilder` (separate from the open
  elements stack, per spec).
- Template content document fragment — `<template>`'s children get appended to a
  `DocumentFragment` exposed via the element rather than the element itself.
  (DOM §4.10.5 `HTMLTemplateElement.content`.)
- Real `HandleInTemplate` with the spec's 9 cases plus the EOF/end-tag rules.
- `wp:M1-02d`-style `[PendingFact]` removal — fixtures in `template.dat` move
  into the passing bucket; the floor in `wp:M1-02b` ratchets up.
- Bindings hook: `HTMLTemplateElement.content` reachable from JS (split into a
  follow-up if it bloats the scope; document the seam either way).

## Acceptance

- `template.dat` pass rate goes from its current baseline (record at claim) to
  ≥ 95% in the `wp:M1-02b` runner.
- The 2026-05-27 placeholder cases (`Template_after_head_does_not_stack_overflow`,
  `Template_inside_head_does_not_stack_overflow` in `TreeBuilderTests`) still
  pass — they now stand in for "no regressions when template appears in
  AfterHead / InHead."
- `wp:M1-02b`'s floor raised to reflect the new total.

## Notes

- The current placeholder leaves children of `<template>` parsed in normal
  flow. Real handling routes them into the template-content fragment via the
  template insertion-mode stack — read §13.2.6.4.16 carefully; the "current
  template insertion mode" check at the top of the dispatcher is easy to miss.
- Foreign content (SVG/MathML) inside templates is handled by `wp:M1-02e`, not
  this WP. The two interact at §13.2.6.5 step 1 (`HTML namespace ... or current
  template insertion mode is not InTemplate`).
