---
id: "wp:M1-02e-foreign-content"
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
  - "browser-plan/04_HTML_PARSING.md#foreign-content"
---

# wp:M1-02e — Foreign content insertion mode (§13.2.6.5)

## Goal

Parse `<svg>` and `<math>` subtrees in the dedicated foreign-content mode with
case-corrected element + attribute names, instead of treating them as plain
HTML elements. Today `HtmlTreeBuilder` has the comment "MathML/SVG foreign-
content modes are deferred" (see remarks block at the top); `wp:M1-02b` shows
the cost — `svg.dat`, `math.dat`, `foreign-fragment.dat`,
`namespace-sensitivity.dat`, `tests9.dat` are all 0% pass.

## Inputs

- §13.2.6.5 (spec, prose).
- `Element.CreateNamespaced(@namespace, qualifiedName)` already exists.
- `AttrNode.CreateNamespaced` for namespaced attributes (XLink, XML, XMLNS).
- `wp:M1-02b` runner.

## Outputs

- A small foreign-content dispatcher in `HtmlTreeBuilder` (the spec doesn't
  call it a distinct InsertionMode — it's a check that runs ahead of the
  current insertion mode when the adjusted current node is in SVG / MathML).
- Generated case-mapping tables for SVG element names and SVG/MathML
  attributes per §13.2.6.5 (`feblend` → `feBlend`, `xlink:href` → namespaced
  `xlink href`, etc.). Keep the tables in a single generated file so they're
  easy to regenerate from spec.
- Element + attribute namespace propagation through `InsertElement`.
- Breakout handling: §13.2.6.5 step 4 (start tag of `b`, `big`, `body`, etc.
  in foreign content pops back to HTML).

## Acceptance

- `svg.dat`, `math.dat`, `foreign-fragment.dat`, `namespace-sensitivity.dat`
  each ≥ 90% in `wp:M1-02b`.
- `tests9.dat` ≥ 80%.
- Overall runner pass rate raised; floor ratcheted.

## Notes

- The case-correction tables are large but mechanical. Other engines vendor
  them from spec; do the same — don't hand-write.
- The `Html5LibTreeSerializer` already prints `svg ` / `math ` / `xlink ` /
  `xml ` / `xmlns ` namespace designators. If a fixture fails *only* because
  of a designator mismatch, the namespace assignment in the tree builder is
  the bug — not the serializer.
