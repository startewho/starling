---
id: "wp:spec-css-v1-systemic-fixes"
milestone: "ongoing"
status: "available"
claimed_by: ""
claimed_at: ""
subsystem: "Starling.Css / value parsing + property registry"
depends_on: []
plan_refs:
  - "tasks/SPEC_COVERAGE.md"
  - "browser-plan/06_CSS.md"
---

# wp:spec-css-v1-systemic-fixes

Two cross-cutting engine gaps block many already-🟢 specs from reaching ✅.
Both surfaced repeatedly while writing parse/cascade conformance tests (the
PendingFacts in `CssOverscroll1` and `CssCompositing1` document them). Fixing
each unblocks rejection / per-layer tests across a dozen specs at once, so they
are the highest-leverage CSS-V1 work remaining.

## Tier 1a — per-property value validation

**Problem.** `PropertyRegistry.Parse`'s `default:` branch accepts *any* ident as
a `CssKeyword` with no per-property whitelist. So `overscroll-behavior-x: scroll`
(invalid) survives instead of being dropped. CSS Syntax 3 §8 says an invalid
declaration is dropped — many specs' "invalid value is ignored" requirements
cannot pass without this.

**Approach.** Add a per-property accepted-value table for enumerated
(keyword-only) properties; in the `default:` parse branch, drop a single-keyword
value that is not in the property's set (and not a CSS-wide keyword
`inherit|initial|unset|revert|revert-layer`). Strictly subtractive — every
*valid* value still parses, so the ~370 existing parse SpecFacts stay green; only
invalid values change from "kept" to "dropped".

**Risk / contract.** Large table (~250 properties — do enumerated ones first).
MUST keep the full `Starling.Css.Tests` (700) + `Starling.Css.Spec.Tests` suites
green. Promote `CssOverscroll1.OverscrollBehaviorX_drops_invalid_keyword` and add
rejection SpecFacts to the other enumerated-property specs.

## Tier 1b — top-level comma splitting in value lists

**Problem.** `CssValueParser.ParseList` does not split on top-level commas, so a
comma surfaces as an empty / `,` `CssKeyword` inside the `CssValueList`. This
breaks per-layer semantics for comma-layered properties
(`background-blend-mode`, `mask-image`, `transition`, `animation`,
`background-image`).

**Approach.** Decide a representation for comma-separated layer lists and split
on top-level commas at the value-parsing or longhand-expansion boundary. Audit
every consumer of multi-value `CssValueList` (background/mask expanders already
use `SplitTopLevelCommas` — unify on that). Promote
`CssCompositing1.Background_blend_mode_list_splits_cleanly_on_commas`.

**Risk.** Parser-level; touches many multi-value properties. Run the FULL suite
(Css, Css.Spec, Layout, Paint) after.

## Done when

Both PendingFacts above are `[SpecFact]`; the enumerated specs gain
invalid-value-rejection SpecFacts; all suites green. Update `tasks/SPEC_COVERAGE.md`.
