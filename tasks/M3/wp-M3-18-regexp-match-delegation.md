---
id: wp:M3-18-regexp-match-delegation
title: "RegExp/String match: spec-correct @@match exec delegation so core-js leaves native @@match in place"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

mcmaster.com's second bundle (`mcm_93043416…` — jQuery 3.x + Backbone + YUI +
Handlebars, shipped alongside a core-js shim) recursed infinitely through
`String.prototype.match` → `RegExp.prototype[@@match]` and exhausted the stack,
surfacing as a catchable `RangeError: Maximum call stack size exceeded` (the
depth/native-stack guard in `JsVm`). This is the same class of bug
`wp:M3-14` fixed for `@@replace`, now for `@@match`.

Root cause: core-js feature-detects Starling's native `@@match` /
`String.prototype.match` as "broken" and installs its own polyfill, which then
recurses against Starling's native methods (which delegate back to the now-
overridden `Symbol.match`). The fix is **not** to patch core-js or jQuery — it
is to make Starling's native behavior spec-correct so core-js's detects pass
and it no-ops (leaves the native methods in place).

## Diagnosis

Temporary instrumentation in `JsVm.Run` (a thread-local mirror of the JS +
native-intrinsic call stack, dumped at the depth-cap throw; removed before
commit) named the recursing cycle as a 3-chunk loop:

- core-js `RegExp.prototype[@@match]` polyfill (consts `global | unicode | lastIndex`)
- core-js `RegExpExec` abstract op (consts `exec | call`)
- native `String.prototype.match` (`#match`) re-entered each turn

i.e. `String#match → native @@match → … → exec → @@match …` without
termination.

## Which core-js detects were failing

From `core-js/internals/fix-regexp-well-known-symbol-logic.js`. core-js
redefines `String.prototype.match` + `RegExp.prototype[@@match]` if either of
these fail:

1. **`DELEGATES_TO_SYMBOL`** (`'whatever'.match(O)` where `O[Symbol.match]` is
   callable must be honored) — Starling's `String.prototype.match` only
   delegated to `@@match` when the argument was a genuine `RegExp`, not any
   object exposing `[Symbol.match]`.
2. **`DELEGATES_TO_EXEC`** (`re[Symbol.match]('')` must call a monkeypatched
   `re.exec`) — Starling's `RegExp.prototype[@@match]` matched internally via
   `re.Compiled.Exec(...)` and never read/called the receiver's `exec`.

When core-js sees these as broken, it wraps `String.prototype.match` so it
delegates to `regexp[Symbol.match]`, and replaces `RegExp.prototype[@@match]`
with a polyfill that calls native `String.prototype.match` as a fast path — the
two now call each other forever.

## What was changed (Starling native behavior)

### 1. `RegExp.prototype[@@match]` drives RegExpExec (`src/Starling.Js/Intrinsics/RegExpCtor.cs`)
`SymbolMatch` re-implemented per **§22.2.6.8 RegExp.prototype [ @@match ]**:

- `this` need only be an Object (not a genuine RegExp); the `global` and
  `unicode` flags are read generically via `AbstractOperations.Get`.
- Non-global → `return RegExpExec(rx, S)` (§22.2.7.1), which reads and calls the
  receiver's (possibly overridden) `exec`.
- Global → reset `lastIndex` to 0, then loop `RegExpExec`, collecting each
  result's `[0]` (read via `Get`); on an empty match advance `lastIndex` with a
  new `AdvanceStringIndex` helper (§22.2.7.3, unicode-aware) so the loop
  terminates.

The old body called the internal `re.Compiled.Exec(...)` / `Exec(...)`
directly, ignoring a user `exec` — exactly the DELEGATES_TO_EXEC failure.

### 2. `String.prototype.match` / `matchAll` delegate to any `@@match` / `@@matchAll` (`StringCtor.cs`)
Per **§22.1.3.13 / §22.1.3.14**, when the argument is not undefined/null and
exposes a callable `[Symbol.match]` / `[Symbol.matchAll]` (via `GetMethod`),
delegate to it for **any** object — not just genuine RegExps. Falls through to
the existing "coerce string → build RegExp" path when no such method exists.

### 3. `RegExp.prototype[@@matchAll]` returns a RegExpStringIterator (`RegExpCtor.cs`)
`SymbolMatchAll` previously returned a `JsArray` (a pre-iterator stopgap).
Routing `String#matchAll` through it (change #2) would have regressed the
iterator-shape contract (`Array.isArray(...) === false`). Fixed `SymbolMatchAll`
to build the same `JsRegExpStringIterator` that `String.prototype.matchAll`
builds, making it spec-correct (§22.2.6.9) and delegation-consistent.

## Verify

Reproduce-first regression suite
`tests/Starling.Js.Tests/Runtime/RegExpMatchDelegationTests.cs` (7 tests,
mirrors `RegExpReplaceDelegationTests`): 4 delegation cases failed before /
green after; 3 native-semantics cases stayed green. Tagged `[Spec]`+`[SpecFact]`.

- `Symbol_match_calls_the_regexps_own_exec_when_not_global`
- `Symbol_match_uses_overridden_exec_result_when_not_global`
- `Global_symbol_match_collects_via_overridden_exec`
- `String_match_delegates_to_argument_Symbol_match`
- `Native_nonglobal_match_returns_capture_array`
- `Native_global_match_returns_all_matches`
- `Native_global_match_no_hit_is_null`

Full `Starling.Js.Tests`: **1311 passed, 1 skipped** (1304 baseline + 7 new;
the 3 transient `MatchAll_*` regressions from change #2 were resolved by change
#3). Test262 harness green.

## Render check

mcmaster.com's `mcm_93043416…` bundle no longer throws
`RangeError: Maximum call stack size exceeded`; it now advances to a NEW
`engine.js` error: **`not a constructor: undefined`** (still in the same
bundle) — the next blocker for a follow-up WP.
