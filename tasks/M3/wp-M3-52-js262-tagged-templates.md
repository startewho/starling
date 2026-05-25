---
id: "wp:M3-52-js262-tagged-templates"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "1"
integrated: "main a45b979 (also on branch js262-phase0-2)"
note: "base tree already had most tagged-template support (f6f216f); the genuine gap was invalid-escape cooked=undefined, now fixed. Confirms the May-22 failures.txt is STALE."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-52 — JS compiler: tagged template expressions (Phase 1)

## Why (evidence)
~80 failures: `host:NotSupportedException:compiler: expression kind
'TaggedTemplateExpression' not yet supported.` plus the `tagged-template` dir
(46 files). The compiler has no case for tagged templates.

## Scope
- `src/Starling.Js/Bytecode/JsCompiler*.cs`: compile `TaggedTemplateExpression`.
  Evaluate the tag (with correct `this` for member-expression tags), build the
  template object: a frozen array of cooked strings with a frozen `raw` array,
  then call the tag with `(templateObject, ...substitutions)`.
- Implement the **template-object cache**: the same call site must hand the tag
  the *same* frozen template object on every evaluation (per spec realm-keyed
  cache). Handle invalid-escape cooked = `undefined` (raw still present).

## Acceptance
- `STARLING_TEST262_FILTER=tagged-template` and `template-literal` subsets
  improve; report before/after.
- Existing `Starling.Js.Tests` stay green; add a focused test incl. cache
  identity (`f\`a\` === f\`a\`` for one call site across calls).
