---
id: "wp:M3-53-js262-computed-key-eval"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "1"
integrated: "main 67a4fb9 (also on branch js262-phase0-2)"
subsystem: "Starling.Js"
depends_on: []
blocks: []
status_note: "agent-complete (branch worktree-agent-a73181e8155b82254, commit 785d513); diff reviewed OK; pending merge + authoritative test262 verify"
---

> **Root-cause correction (from the agent):** the compiler already evaluated
> computed keys eagerly; the real bug is that reading a bare undeclared GLOBAL
> compiles to `Opcode.LoadGlobal`, which silently returns `undefined` instead of
> throwing ReferenceError (the JsVm.cs:524 throw is the *store* path). Fix added
> `Opcode.LoadGlobalChecked` and routes ONLY computed keys (bare free
> identifiers) through it, deliberately leaving `typeof`/host-global probes on
> the silent load. This is correct but **partial**: the non-computed-key part of
> the 1,712 ReferenceError cluster (bare unresolved reads in function/arrow/
> generator bodies, etc.) is NOT fixed. See [[wp-M3-54-js262-unresolved-read]].

# wp:M3-53 — JS: eager evaluation of computed property keys (Phase 1, biggest)

## Why (evidence)
The single largest `language` cluster: **1,712** failures of `Expected a
ReferenceError to be thrown but no exception was thrown at all`, concentrated in
`class` (848), `object` (134), and destructuring/loops. Representative test
`expressions/class/accessor-name-inst/computed-err-unresolvable.js`:
```js
assert.throws(ReferenceError, function() {
  0, class { get [test262unresolvable]() {} };
});
```
Undeclared reads *do* throw (`JsVm.cs:524`), so this is NOT a foundational bug —
computed keys for class/object members aren't getting their reference-resolution
side-effects run at definition time, so the abrupt completion never fires.

## Scope
- Ensure each computed key is evaluated as `ToPropertyKey(? GetValue(ref))`,
  where `ref` is the result of evaluating the key expression, at the
  spec-correct point during ClassDefinitionEvaluation and object-literal
  PropertyDefinitionEvaluation, for: instance/static **methods, getters,
  setters, and fields**, and object-literal computed methods/accessors.
- Preserve spec evaluation **order** (keys resolved in source order as elements
  are processed); a thrown key aborts the whole definition.
- Check destructuring computed keys (`{ [expr]: x } = …`) hit the same path.

## Acceptance
- `STARLING_TEST262_FILTER=computed-err-unresolvable` and the `cpn-` subsets go
  to ~100%; broad `class` + `object` subsets show net gain, **no regression**.
- Existing `Starling.Js.Tests` stay green; add focused tests.
