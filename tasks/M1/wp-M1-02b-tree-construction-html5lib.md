---
id: "wp:M1-02b-tree-construction-html5lib"
parent: "wp:M1-02-html-tree-builder"
milestone: "M1"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-27T18:36:40Z"
branch: "main"
depends_on:
  - "wp:M1-02-html-tree-builder"
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tree-builder"
  - "browser-plan/12_TESTING.md"
---

# wp:M1-02b — html5lib tree-construction suite

## Goal

Vendor the html5lib-tests `tree-construction/` corpus and wire it as an xUnit
data-driven test, the same way `wp:M1-01h` wired the tokenizer half. Drive the
pass rate to the WHATWG tree-construction conformance gate that `wp:M1-02`
promised but deferred (≥ 95% on `tests1.dat` through the rest of the corpus,
100% on adoption-agency cases in `tests1.dat`). Each fixture failure becomes
either a `[SpecFact]` (passing) or a `[PendingFact]` with a real assertion body
so promotion is a one-line attribute flip.

## Why now

The tokenizer side has a 100% conformance gate; the tree builder does not. As a
result the InHead ↔ AfterHead loop on a real-world Astro/Starlight page
(netclaw.dev/getting-started/installation/, fixed 2026-05-27) shipped without
the suite that would have caught it. Wiring the corpus surfaces every remaining
insertion-mode gap (template / InTemplate, foreign content, adoption agency,
table sub-modes, frameset, InHeadNoscript) at once and turns
`wp:M1-02`'s deferred scope into a concrete ratchet.

## Inputs

- html5lib-tests upstream `tree-construction/*.dat` fixtures (text format, not
  JSON — different parser from the tokenizer suite). Vendor under
  `testdata/spec/html5lib-tests/tree-construction/` (same pattern as the
  already-vendored `tokenizer/` subtree).
- Existing `HtmlParser.Parse` + `HtmlTreeBuilder` (`src/Starling.Html/`).
- Existing `Starling.Spec.Common` traits for `[Spec]` / `[SpecFact]` /
  `[PendingFact]`.

## Outputs

- `testdata/spec/html5lib-tests/tree-construction/*.dat` (vendored).
- `tests/Starling.Html.Tests/TreeBuilder/Html5LibTreeConstructionTests.cs` —
  data-driven runner that parses each `.dat` block (`#data`, `#errors`,
  `#document`, optional `#document-fragment`, optional `#script-on` /
  `#script-off`) and asserts the produced DOM matches the expected tree dump.
- A pure-managed `.dat` parser + a DOM-to-`#document` serializer that matches
  the html5lib indented-tree format exactly (one node per line, two-space
  indent per depth, `| `-prefix, `"text"` for text nodes, `<!-- comment -->`,
  `<!DOCTYPE …>`, attribute lines sorted lexicographically). See
  https://github.com/html5lib/html5lib-tests/blob/master/tree-construction/README.md
  for the format.
- Content glob in `tests/Starling.Html.Tests/Starling.Html.Tests.csproj`
  mirroring the existing tokenizer glob.

## Acceptance

- Every `.dat` block under `tree-construction/` runs as a discoverable test
  case named by file + block index (so failures point to the exact fixture).
- `tests1.dat`: **100%** pass (this is the canonical adoption-agency corpus per
  `browser-plan/04_HTML_PARSING.md` line 244).
- Overall suite: **≥ 95%** `[SpecFact]` pass; remainder filed as
  `[PendingFact("…", trackingWp: "…")]` linking to a successor WP (one per
  insertion-mode bucket — InTemplate, foreign content, table sub-modes,
  InHeadNoscript, frameset).
- `tasks/SPEC_COVERAGE.md` gains an "HTML parsing" section row (or the existing
  hand-synced spec table is extended) pointing at this fixture suite and the
  current pass rate.

## Notes

- The fixture format is **not** JSON — html5lib uses a custom block-based text
  format. A pure-managed parser is a small file (<200 lines); don't pull in a
  package for it.
- `#script-on` / `#script-off` controls the `scriptingEnabled` flag we already
  thread through `HtmlParser.Parse` (see `wp:M1-noscript-scripting-flag`).
- `#document-fragment` blocks test `ResetInsertionModeForContext` (already
  scaffolded at `HtmlTreeBuilder.cs:135-205`); these may be a separate ratchet
  if they distract from the main pass-rate goal — split into a follow-up WP if
  so.
- The template-related fixtures (`template.dat`) will fail until a proper
  `InTemplate` insertion mode is modeled — the 2026-05-27 fix in
  `HandleInHead` is a non-crashing placeholder. Tag those as
  `[PendingFact(trackingWp: "wp:M1-02c-in-template-mode")]` and file that WP as
  a successor.
- Test discovery: MSTest `[DynamicData]` already works for the tokenizer
  runner; mirror that. One assertion per block keeps failure messages
  actionable.

## Handoff log

- 2026-05-27 — filed. Motivation: the InHead ↔ AfterHead `<template>` stack
  overflow on netclaw.dev that shipped without a fixture-level gate. The fix
  (commit on main, same day) is a non-crashing placeholder; the real
  `InTemplate` mode and the rest of the deferred `wp:M1-02` scope sit behind
  this WP's pass-rate ratchet.
- 2026-05-27T18:36:40Z — claimed by agent-claude-cody, working on main
