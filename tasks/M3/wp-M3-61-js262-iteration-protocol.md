---
id: "wp:M3-61-js262-iteration-protocol"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "2"
integrated: "main af06c97 (also js262-phase0-2)"
impact: "iteration fix + VM-fix added +1166 passing language tests (81.43%->84.15%); 'value is not iterable' 154->0. Typed-array iterability was the big lever."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-61 — JS: iteration protocol gaps (`value is not iterable`) (Phase 2)

## Why (evidence)
**128** failures `threw TypeError: value is not iterable` (48 sync) plus
**80** `async incomplete: …TypeError: value is not iterable` — spread,
destructuring, `for-of`, and the async iteration (`for await`) paths reject
values that ARE iterable per spec.

## Scope
- Investigate the GetIterator / IteratorStep / async-iterator path in
  `src/Starling.Js/Runtime/` + the for-of / spread / destructuring compilation.
  Determine which iterables are wrongly rejected (custom `Symbol.iterator`,
  `Symbol.asyncIterator`, iterator returned by a getter, strings, arguments,
  array-likes that are actually iterable, etc.).
- Fix GetIterator to follow spec: `? GetMethod(obj, @@iterator)` (or
  @@asyncIterator for async), call it, require an object result, then
  IteratorStep/IteratorValue. Ensure the async path falls back to sync-iterator
  wrapping (CreateAsyncFromSyncIterator) where the spec requires.

## Acceptance
- `STARLING_TEST262_FILTER` over the for-of / spread / destructuring subsets
  shows the `not iterable` failures drop; report before/after (maintainer-run).
- Focused unit tests for the specific shapes you fixed.
- Existing `Starling.Js.Tests` green (only the known pre-existing failure).
