---
id: "wp:M1-02d-adoption-agency"
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
  - "browser-plan/04_HTML_PARSING.md#adoption-agency-algorithm"
---

# wp:M1-02d — adoption agency algorithm (§13.2.6.4.7)

## Goal

Implement the WHATWG HTML adoption agency algorithm literally. `browser-plan`
already calls it out as "gnarly" and the canonical reference for whether the
parser handles misnested formatting tags like `<b><i></b></i>`. The
`wp:M1-02b` baseline shows the deferred-scope cost: `adoption01.dat` 5.3%,
`adoption02.dat` 0%, `tricky01.dat` 0%, big chunks of `tests1.dat` /
`tests7.dat` / `webkit02.dat` red.

## Inputs

- §13.2.6.4.7 (spec, prose).
- `HtmlTreeBuilder` — already has `ActiveFormattingElements` (search for the
  type) and the open-elements stack, both needed by the algorithm.
- `wp:M1-02b`'s runner — gates progress.

## Outputs

- A new private method on `HtmlTreeBuilder` implementing the algorithm step by
  step, with comments citing the spec step numbers.
- The "Noah's Ark" clause + marker handling for `ActiveFormattingElements`
  (verify what we have; extend if not literal).
- The `HandleInBody` cases for `</a>`, `</b>`, `</big>`, `</code>`, `</em>`,
  `</font>`, `</i>`, `</nobr>`, `</s>`, `</small>`, `</strike>`, `</strong>`,
  `</tt>`, `</u>` all funneled through it.

## Acceptance

- `adoption01.dat` and `adoption02.dat` both ≥ 95% in `wp:M1-02b`.
- `tricky01.dat` ≥ 90%.
- Overall runner pass rate raised; floor in `wp:M1-02b` ratcheted to the new
  baseline.

## Notes

- Implement it **literally** — spec line-by-line, no clever rewrites. The
  algorithm has subtle interactions with the stack-of-open-elements + the list
  of active formatting elements that resist refactoring. `browser-plan/04`
  spells this out in the §"Adoption agency algorithm" subsection.
- The outer loop is bounded to 8 iterations; the inner loop to 3. These bounds
  are spec-mandated, not optimizations.
- Many `tests*.dat` cases are nominally about other modes but fail because of
  adoption-agency interactions — expect to bump multiple files at once.
