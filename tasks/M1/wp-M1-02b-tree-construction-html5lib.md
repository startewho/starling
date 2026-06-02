---
id: "wp:M1-02b-tree-construction-html5lib"
parent: "wp:M1-02-html-tree-builder"
milestone: "M1"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-27T18:36:40Z"
branch: "main"
depends_on:
  - "wp:M1-02-html-tree-builder"
subsystem: "Starling.Html"
plan_refs:
  - "browser-plan/04_HTML_PARSING.md#tree-builder"
  - "browser-plan/12_TESTING.md"
completed_at: "2026-05-27T18:46:11Z"
---

# wp:M1-02b — html5lib tree-construction suite

## Goal

Stand up a tree-construction conformance runner against the WHATWG-conformance
`html5lib-tests/tree-construction/` corpus, measure a baseline pass rate, and
ratchet toward the ≥ 95% target that `wp:M1-02` deferred. "Start" deliverable:
the harness + vendored corpus + a measured baseline, with a floor gate that
prevents regression. Reaching 95% is iterative tree-builder work tracked from
the failure report this produces — broken out into the successor WPs listed
below (`wp:M1-02c…g`).

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

## Delivered (this WP — infrastructure + baseline)

- `testdata/spec/html5lib-tests/tree-construction/` — corpus vendored at the
  upstream SHA `e4463205ac3c4500e1379103daadfdcfe5e33af5` (56 top-level `.dat`
  files + `scripted/`, 1699 cases total). `UPSTREAM.md` records the pin +
  re-vendor recipe.
- `tests/Starling.Html.Tests/TreeBuilder/`:
  - `Html5LibDatFile.cs` — pure-managed parser for the `.dat` block format
    (sections `#data`, `#errors`, `#new-errors`, `#document`,
    `#document-fragment`, `#script-on`, `#script-off`).
  - `Html5LibTreeSerializer.cs` — DOM → indented-tree dump matching the
    corpus's `#document` body byte-for-byte (namespace designators
    `svg ` / `math ` / `xlink ` / `xml ` / `xmls `, attribute lexicographic
    sort, etc.).
  - `Html5LibTreeConstructionTests.cs` — Test262-style runner: one
    `[TestMethod]` aggregating across all blocks, sidecar
    `bin/.../results/tree-construction/{summary,failures}.txt`,
    env knobs `STARLING_TREEBUILD_FILTER` / `_FLOOR` / `_VERBOSE`.
- `src/Starling.Dom/DocumentType.cs` — drive-by: `Name` may be empty
  (DOM §4.6 doesn't require non-empty, and WHATWG HTML §13.2.5.74 explicitly
  emits empty-name doctypes for `<!DOCTYPE >` input). Eliminates 3 crashes.
- `tasks/SPEC_COVERAGE.md` — new "HTML parsing" section row pointing at this
  runner and the current rate.

## Acceptance (infrastructure)

- ✅ Every `.dat` block runs through the harness; failures are addressable by
  `file.dat#index` via `STARLING_TREEBUILD_FILTER`.
- ✅ Sidecar report written on every run; full failure dump in
  `results/tree-construction/failures.txt` for offline triage.
- ✅ Floor gate in place. Baseline 2026-05-27: **44.23% (786/1777)**, 0 crashes,
  ~0.1 s. Floor set to 44%; raise as conformance improves.
- ✅ `tasks/SPEC_COVERAGE.md` has a row for this runner.

## Toward 95% — successor WPs

Each successor moves a bucket from "all red" to "all green" in the runner. They
are independently picked up; together they take us from 44% toward 95%.

- `wp:M1-02c-in-template-mode` — model §13.2.6.4.4 / §13.2.6.4.16 fully
  (template-content document fragment, "in template" insertion mode, template
  insertion-mode stack). Fixtures: `template.dat`.
- `wp:M1-02d-adoption-agency` — implement §13.2.6.4.7 literally. Fixtures:
  `adoption01.dat`, `adoption02.dat`, `tricky01.dat`, big chunks of
  `tests1.dat` / `tests7.dat` / `webkit02.dat`.
- `wp:M1-02e-foreign-content` — §13.2.6.5 SVG + MathML insertion modes,
  case-corrected element + attribute names. Fixtures: `svg.dat`, `math.dat`,
  `foreign-fragment.dat`, `namespace-sensitivity.dat`, `tests9.dat`.
- `wp:M1-02f-table-sub-modes` — InTable, InTableText, InCaption, InColumnGroup,
  InTableBody, InRow, InCell, InSelectInTable per §13.2.6.4.9-15. Fixtures:
  `tables01.dat`, large chunks of `webkit01.dat` / `webkit02.dat`.
- `wp:M1-02g-in-head-noscript` — model the "in head noscript" insertion mode
  with the scripting flag disabled (the html5lib conformance default). Fixtures:
  `noscript01.dat`.

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
- 2026-05-27T18:55Z — corpus vendored at upstream SHA
  `e4463205ac3c4500e1379103daadfdcfe5e33af5` (1699 .dat cases, 78 of them in
  `scripted/`). Runner mirrors the Test262 pattern: one `[TestMethod]`
  aggregating across all blocks, on-disk sidecar report at
  `bin/.../results/tree-construction/{summary,failures}.txt`,
  `STARLING_TREEBUILD_FILTER`/`STARLING_TREEBUILD_FLOOR`/`STARLING_TREEBUILD_VERBOSE`
  env knobs. Drive-by: relaxed `DocumentType` ctor to allow empty `Name`
  (DOM §4.6 + WHATWG HTML §13.2.5.74 — `<!DOCTYPE >` emits an empty-name
  doctype, and our ctor threw `ArgumentException`).
  **Baseline: 44.23% (786/1777), 0 crashes, in 0.1 s.** Floor set to 44%.
  Per-fixture highlights (full breakdown in the sidecar):
  - ≥ 95%: `entities01.dat` 98.7%, `entities02.dat` 96.2%
  - ~90%: `blocks.dat`, `comments01.dat`, `doctype01.dat`, `menuitem-element.dat`
  - 0%: `noscript01.dat` (no `InHeadNoscript` mode), `tests9.dat` /
    `math.dat` / `namespace-sensitivity.dat` (no foreign-content mode),
    `tricky01.dat`, `pending-spec-changes*.dat`, `ark.dat`
  - low: `adoption01.dat` 5.3%, `adoption02.dat` 0% (no adoption agency),
    `webkit02.dat` 22.4%, `tests_innerHTML_1.dat` 32.1%
  Remaining buckets toward the 95% target line up with the deferred-scope
  list in `wp:M1-02`: adoption agency (§13.2.6.4.7), foreign content
  (§13.2.6.5), real `InTemplate` mode, `InHeadNoscript`, table sub-modes.
  Each will get its own successor WP when picked up.
- 2026-05-27T18:46:11Z — merged; complete
