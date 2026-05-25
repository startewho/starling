---
id: "wp:M3-62b-js262-async-gen-yield-reject"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "2"
integrated: "main 04571d7 (also js262-phase0-2)"
finding: "plain yield in async gen must Await operand; on rejection, reject next(). 13-line fix in Suspend opcode handler + 5 tests. yield* path untouched."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-62b — JS: async generator yielding a rejected promise must reject next()

## Why (evidence)
~36 failures `Test262Error: Promise incorrectly resolved.` in
`expressions/async-generator/yield-promise-reject-next*.js` (+ named/`yield*`/
`for-await-of` variants), plus ~38 async-generator `SameValue` value-mismatches.

When an async generator `yield`s a value, it must AWAIT that value (§27.6.3.8);
if the awaited value is a promise that REJECTS, the pending `next()` request's
promise must REJECT with that reason. Currently it resolves instead.

## Scope
- In the async-generator driver (`src/Starling.Js/Runtime/JsGenerator.cs` +
  the async-gen paths in `JsVm.cs`), make a plain `yield` await its operand and,
  on rejection, reject the consumer's `next()` promise (AsyncGeneratorReject)
  rather than resolving it. Verify `next()`/`return()`/`throw()` request-queue
  ordering and the resulting `{value, done}` for the value-correctness cases.
- NOTE: this worktree's base predates the merged Phase 0-2 work (esp. WP-61's
  `yield*` async-iteration fix in `ExecuteYieldDelegate`). Keep changes isolated
  to the plain-yield-await / resolve-reject logic; the integrator reconciles.

## Acceptance
- Focused unit tests: async gen yielding `Promise.reject(x)` → `next()` rejects
  with `x`; yielding `Promise.resolve(v)` → `next()` resolves `{value:v}`.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
