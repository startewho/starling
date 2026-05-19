---
id: "wp:M3-04c-js-closures-snapshot"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T20:10:00Z"
completed_at: "2026-05-11T20:55:00Z"
branch: "wp-M3-04c-js-closures-snapshot"
depends_on:
  - "wp:M3-04e-js-method-binding"
blocks:
  - "wp:M3-05-js-intrinsics"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#vm"
---

# wp:M3-04c — Closures (snapshot semantics)

## Goal
Nested function expressions / declarations can read enclosing-scope
locals. Captured values are **snapshotted** at function-definition time
— this slice does not yet track mutation through the upvalue.

```js
function makeAdder(n) {
  return function(x) { return x + n; };
}
var add5 = makeAdder(5);
add5(3);  // → 8     ✓ works with snapshot
```

But this one does NOT work yet (deferred to wp:M3-04c2 with Cell-based
upvalues):

```js
function makeCounter() {
  var n = 0;
  return function() { return ++n; };  // mutation through upvalue
}
```

## Outputs
- New opcodes: `LoadUpvalue [u8 idx]`, `MakeClosure [u16 fnTemplateIdx, u8 nUpvalues]`.
- Compiler: parent-pointer + lazy upvalue resolution. When a name doesn't
  resolve as local, walk parents to find it; record an upvalue in this
  function's table; emit `LoadUpvalue`. After compiling, the parent
  pushes the captured values + emits `MakeClosure`.
- VM: `LoadUpvalue` reads from the current `JsFunction.Upvalues` array;
  `MakeClosure` pops N values and constructs a new `JsFunction` with
  them bound.

## Out of scope (queued)

- Mutation through upvalues (wp:M3-04c2). Inner writes to enclosing
  locals don't propagate yet; for those we need Cell-based slots.
- Chained captures: a deeply-nested function capturing a grandparent's
  local. Lazy resolution handles single-level capture; chained capture
  works if each intermediate function explicitly references the var,
  but a "skip-level" capture (intermediate doesn't reference it) needs
  the parent-of-parent path through upvalue slots — implemented.

## Acceptance
- 6+ tests: makeAdder, curry, double-nested capture, capture of param,
  capture of var, sibling closures over same parent local don't share
  state (snapshot semantics).

## Handoff log
- 2026-05-11T20:10Z — created and claimed atomically by agent-claude-cody.
- 2026-05-11T20:55Z — landed. New opcodes `LoadUpvalue [u8]` and
  `MakeClosure [u16 fnIdx, u8 n]`; `JsFunction.Upvalues` snapshot table;
  compiler parent-linkage + lazy upvalue resolution with chained capture
  through intermediate upvalue tables; VM threads the active closure's
  upvalues through `Run`. 9 closure tests (makeAdder snapshot, sibling
  independence, capture of param + var, chained skip-level capture,
  three-level curry, "later reassignment doesn't leak" snapshot proof,
  per-call activation independence). Full solution 7430/7430 tests green.
