---
id: wp:M3-25-js-arrow-rest-params
title: "Rest parameters in synchronous arrow functions — (...a) => … parse + runtime collection (also fixes function rest params)"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

`(...a) => a.length` failed to parse:

```
unexpected token Ellipsis '...' (at 1:8)
```

Rest params in async arrows (`async (...a) => …`) and function declarations
already *parsed*; only the sync parenthesized-arrow path threw. mcmaster.com's
app bundle uses `(t,n) => (...a) => t(n(...a))`, so this blocked the bundle.

While fixing this I found the compiler also never *collected* rest-parameter
values at runtime — function rest params (`function f(...a){}`) parsed but bound
`undefined` (an explicit deferral comment in `BindFunctionParameters`). So this
WP covers both: parse the sync arrow rest param AND implement rest collection
for all functions.

## Root cause

The sync parenthesized-arrow form parses `( … )` as a grouping/sequence
EXPRESSION first (`ParseConditional`), then `LiftArrowParams` converts it after
seeing `=>`. A leading `...` is not a valid parenthesized expression, so it
threw on `...` before the `=>` was ever reached. The async path avoids this by
probing ahead with a lexer lookahead (`LooksLikeAsyncArrow`) and parsing params
directly.

## Design

### 1. Parse — `Lex/JsLexer.cs`
Extracted the async-arrow balanced-paren scan into a reusable
`LookaheadIsArrowFromParen(int afterOpenParenOffset)` (pure, reads `_src` by
offset, no state mutation). `LookaheadIsAsyncArrow()` now delegates to it.

### 2. Parse — `Parse/JsParser.cs` (`ParseAssignment`)
Before the cover-grammar `ParseConditional`, when `_current` is `(` and the
lookahead confirms `=>` follows the matching `)`, parse the parameter list
directly with the same rest-aware loop the async arrow path and function
declarations use (rest → `SpreadElement`, defaults via `ParseParameter`,
destructuring via `ParseBindingTarget`). We only divert when an arrow is
certain, so ordinary groupings/sequences (`(1,2)` → SequenceExpression) are
untouched. Rest is enforced last (loop `break`s after the `...` element).

### 3. Compile + runtime — rest collection
The compiler previously declared the rest binding but left it `undefined`
(`BindFunctionParameters`, with a "later iterator/Array task" comment). Now:
- New opcode `RestParam [u16 start]` (`Bytecode/Opcode.cs`) — gathers the
  frame's received args from `start` onward into a fresh dense `JsArray` and
  pushes it. Reads the VM frame's `args` directly, so it works in arrows (which
  have no `arguments` object).
- VM (`Runtime/JsVm.cs`) implements it next to `MakeArguments`/`RestArray`.
- `BindFunctionParameters` emits `RestParam i` then binds the array to the rest
  target (identifier or destructuring pattern) via `EmitPatternFromStack`.

This single path fixes rest params for arrows, function declarations, function
expressions, and methods uniformly.

## Disambiguation handled
`(...a) =>`, `(a, ...b) =>`, `() =>`, `(a) =>`, `(a, b) =>`, and a bare
sequence `(1, 2)` (NOT an arrow). Defaulted leading param + rest
(`(a = 5, ...b) =>`) works. Rest must be last.

## Tests

`tests/Starling.Js.Tests/Runtime/JsArrowRestParamTests.cs` (14 tests, green):
sole rest, empty-call rest, rest array is a real Array (`.join`), leading fixed
param + rest, separate binding, the nested bundle shape
`(t,n)=>(...a)=>t(n(...a))` returning 13, `()=>1`, `(a,b)=>a+b`, `(a)=>a*2`,
bare sequence `(1,2)===2`, defaulted-leading-param + rest, plus two
function-declaration rest tests proving the compiler fix.

Full `Starling.Js.Tests`: **1360 passing, 1 skipped** (was 1347/1 — +13 net new
beyond the prior baseline after the merge).

## Diagnostic result — next mcmaster blocker

The `Ellipsis` error is gone; the app bundle `mcm_cc73c91b…` now reaches a NEW
parser error:

```
expected '}' to close object literal (got Identifier 'selectors') (at 117:67080)
```

Bundle line 117, col 67080 (Chrome-UA fetch), ~120 chars:

```
…)k[L]=lH(U,D,E,M);return k})}return{reducerPath:x,getSelectors:P,get selectors(){return P(w)},selectSlice:w}}const I=oe(G({name:s,…
```

The next gap is **object-literal getter/setter shorthand** — `get selectors(){…}`
as an accessor property. The parser treats `get` as a plain identifier key, then
expects `}`/`,` and chokes on the following `selectors` identifier. (This is a
Redux Toolkit `createSlice`-style slice object.) That is the next WP (M3-26
candidate).
