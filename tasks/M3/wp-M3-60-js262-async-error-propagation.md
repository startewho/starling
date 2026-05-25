---
id: "wp:M3-60-js262-async-error-propagation"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "2"
integrated: "main c9937de (also on js262-phase0-2)"
finding: "NOT async propagation — the bug was foundational ToString/ToPrimitive: String(obj) hard-coded [object Object] and OrdinaryToPrimitive only called native toString/valueOf via .Body (never user JS) with an [object Object] fallback instead of the spec step-5 TypeError. Affects ALL object->string coercion engine-wide."
subsystem: "Starling.Js"
depends_on: []
blocks: ["wp:M3-62-js262-async-generators"]
---

# wp:M3-60 — JS: preserve thrown error identity through async propagation (Phase 2, blocker)

## Why (evidence)
The single largest fresh cluster: **~1,826** `async incomplete:
Test262:AsyncTestFailure:Test262Error: [object Object]`, spanning class (1080),
async-generator (383), dynamic-import (223), object (122), async-function (5).

`[object Object]` is test262 `doneprintHandle.js`'s **else-branch**
(`'Test262:AsyncTestFailure:Test262Error: ' + error`), reached only when
`'name' in error` is false AND `error.toString()` is the default Object one.
A real `Test262Error` has a custom `toString` and **sync** tests report proper
messages — so the value reaching `$DONE` is a *plain object*, not the
`Test262Error` that was thrown. Conclusion: **async error propagation
(await / Promise reject / async-generator throw) is dropping the original
thrown JsValue's identity/prototype.** This one bug masks the real async
failures, so async-generator conformance cannot be scoped until it's fixed.

## Scope
- Reproduce minimally: `async function f(){ throw new TypeError('x'); }` then
  inspect the rejection reason — is it the same object? does it keep its
  prototype / `name` / custom `toString`? Repeat for `await`ed rejection,
  `for await`, and an async generator that throws.
- Find where the engine turns a thrown JsValue into a Promise rejection reason
  (async function/generator machinery, `JsPromise`, microtask/await plumbing in
  `src/Starling.Js/Runtime/`) and ensure the **original** JsValue is preserved,
  not re-wrapped into a fresh object.
- Confirm `'name' in errObj` and `String(errObj)` behave for engine Error
  objects (rule out an `in`-operator / Error.prototype.toString gap as the real
  cause).

## Acceptance
- Focused unit tests: a thrown Error preserved by identity through await /
  Promise rejection / async generator; `String(rejectionReason)` and
  `'name' in rejectionReason` correct.
- Existing `Starling.Js.Tests` green (only the known pre-existing failure).
- (Maintainer) re-measure `language`; the `[object Object]` cluster should
  collapse and reveal the true async-generator failures for wp:M3-62.
