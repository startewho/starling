---
id: "wp:M3-04-js-vm"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T18:40:00Z"
branch: "wp-M3-04-js-vm"
completed_at: "2026-05-11T19:00:00Z"
depends_on:
  - "wp:M3-03-js-compiler"
blocks:
  - "wp:M3-05-js-intrinsics"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#vm"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-04-js-vm"
---

# wp:M3-04 — JS VM

## Goal
Execute the bytecode emitted by `wp:M3-03-js-compiler`. After this lands,
the lexer → parser → compiler → VM pipeline is end-to-end demoable:
`starling js script.js` can compute results.

## Scope (this slice)

- `JsValue` discriminated value type (Undefined / Null / Boolean / Number /
  String / Object / BigInt placeholder).
- `JsObject` basic property bag (string-keyed map).
- `JsRuntime` with a global object; host code can register native
  functions via `RegisterGlobal(name, NativeFunction)`.
- `JsVm` stack-machine dispatch loop covering every opcode emitted by
  M3-03.
- Abstract operations: ToNumber, ToString, ToBoolean, abstract equality,
  strict equality per ES2024 §7.2 / §7.1.
- Top-level script execution returning the last-evaluated value.

## Out of scope (queued)

- FunctionDeclaration sub-chunks + closures (a follow-up; M3-03 still
  emits Nop for these).
- `new` constructor semantics (depends on closures).
- TryStatement / SwitchStatement runtime (M3-05).
- Real BigInt arithmetic (M3-05 intrinsics).
- Iterator protocol for for-in/for-of (M3-05).

## Acceptance
- 25+ unit tests covering literal evaluation, arithmetic, comparison,
  logical (with short-circuit), member access, host function calls
  via registered globals, variable declarations + assignments,
  control flow (if / while), and abstract operations.
- Smoke test: a 10-line JS snippet computing fibonacci(10) by iteration
  returns 55.

## Handoff log
- 2026-05-11T18:40Z — created and claimed atomically by agent-claude-cody.
