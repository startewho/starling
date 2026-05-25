---
id: "wp:M3-50-js262-module-harness"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "0"
integrated: "main d2c7d1a (also on branch js262-phase0-2)"
subsystem: "Starling.Js.Test262.Tests"
depends_on: []
blocks: []
---

# wp:M3-50 — Test262: wire module execution + complete $262 host (Phase 0.1)

## Why (evidence)
`summary.language.txt` shows `language/export` and `language/import` at **0/0**
and `module-code` at 2/4 — module tests are essentially unmeasured. The full
failure log has **1,453 skipped (module/io)** and **248** module errors
(`Failed to fetch module …`, `Failed to resolve module specifier`). Plus ~84
tests fail on a missing `$262.createRealm` / `evalScript`. This is harness
wiring, not engine work, and unblocks a whole sub-corpus we currently can't see.

## Scope
- In `tests/Starling.Js.Test262.Tests/Test262Runner.cs`: stop skipping `flags:
  [module]` tests. Drive the engine's module pipeline (parse-as-module → link →
  evaluate) via the hosting API in `src/Starling.Js.Hosting` / `JsRuntime`.
  Implement a module loader that resolves relative specifiers against the test
  file's directory (and `harness/` for includes) within the test262 tree.
- Extend the minimal `$262` host with `createRealm`, `evalScript`, and
  `detachArrayBuffer` (enough to clear the createRealm cluster).
- Keep non-module behavior unchanged; module tests that genuinely need
  unimplemented features should fail honestly (not skip).

## Acceptance
- `STARLING_TEST262_DIRS=language STARLING_TEST262_FILTER=module-code` (and
  import/export) now run with a real pass/total (not 0/0).
- Report before/after scenario counts for the module dirs.
- Existing `Starling.Js.Tests` stay green.
