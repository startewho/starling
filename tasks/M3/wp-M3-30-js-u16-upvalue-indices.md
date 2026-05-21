---
id: wp:M3-30-js-u16-upvalue-indices
title: "JS bytecode: widen upvalue-index operands to u16 (fix >255-captures compile cap)"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: [wp:M3-29-js-u16-local-slots]
subsystem: Starling.Js
---

## Goal

Fix an engine bug — the direct analog of wp:M3-29 (which widened **local-slot**
operands u8→u16) — where a function that **captures more than 255 outer
bindings** failed to compile outright:

```
Script compile/run failure: more than 255 upvalues per function not supported
```

Upvalue indices were still encoded in a single **u8** operand, and the compiler
hard-capped captures at 255 with an explicit `throw`. Google Analytics / gtag
(loaded by netclaw.dev) contains inner functions that reference hundreds of
hoisted outer vars, so those functions threw at compile time, leaving the
enclosing declarations (e.g. `Nf`) undefined and surfacing downstream as
`not a function: undefined (callee hint: 'Nf')`.

## Root cause (precisely)

The upvalue path encoded the upvalue *index* in a u8 and the captured *count*
in a u8, and `JsCompiler.AddUpvalue` / `EmitFunctionConstructor` threw past 255:

- `Opcode.LoadUpvalue`, `Opcode.StoreUpvalue`, `Opcode.LoadUpvalueCell` — index
  operand was `[u8 idx]`.
- `Opcode.MakeClosure` — layout was `[u16 fnIdx][u8 nUpvalues]`; the captured
  count was a u8 (emitted via `EmitU8Raw((byte)upvalues.Count)`).
- `JsCompiler.AddUpvalue` threw `more than 255 upvalues per function not
  supported` once `_upvalues.Count >= 255`.
- `EmitFunctionConstructor` threw `more than 255 captured variables not
  supported` once `upvalues.Count > 255`.

Closure construction encodes upvalues as: for each captured binding, the
compiler pushes the **cell** onto the stack (parent local capture →
`LoadLocal slot`; parent upvalue capture → `LoadUpvalueCell idx`), then emits
`MakeClosure [fnIdx][nUpvalues]`; the VM pops `nUpvalues` values off the stack
into the new closure's upvalue array. The per-upvalue descriptor (isLocal flag +
index) lives in the **compiler's** `_upvalues` table, not in bytecode — only the
*pushes* and the *count* are encoded — so widening means: widen each push
opcode's index operand, and widen the `MakeClosure` count. The class path
(`JsCompiler.Classes.cs`) stores per-member upvalue counts as ints in the
`ClassTemplate` constant (not bytecode), so only its `LoadUpvalue` index pushes
needed widening.

## Fix

Widen all **upvalue-index** operands and the **MakeClosure captured-count**
operand from u8 to u16 (little-endian), supporting up to 65535 captures per
function, and raise the two compile caps to 65535. Call arg counts
(`Call`/`CallMethod`/`New`) stay u8, matching wp:M3-29's scoping.

- `src/Starling.Js/Bytecode/Chunk.cs` — new `ChunkBuilder.EmitUpvalue(op, idx)`
  (mirrors `EmitSlot`): writes `op + u16 idx`, throws a clear
  `InvalidOperationException` past 65535. `MakeClosure`'s count now uses the
  existing `EmitU16Raw`.
- `src/Starling.Js/Bytecode/JsCompiler.cs` —
  - `AddUpvalue`: cap raised 255 → 65535 (message updated).
  - `EmitFunctionConstructor`: count cap raised 255 → 65535; the parent-upvalue
    capture push `LoadUpvalueCell` switched to `EmitUpvalue`; the `MakeClosure`
    count switched from `EmitU8Raw` → `EmitU16Raw`.
  - all `LoadUpvalue`/`StoreUpvalue` index emits → `EmitUpvalue` (7 sites).
- `src/Starling.Js/Bytecode/JsCompiler.Classes.cs` — `EmitUpvaluePushes`'
  `LoadUpvalue` index emit → `EmitUpvalue`.
- `src/Starling.Js/Bytecode/JsCompiler.Modules.cs` — `StoreModuleBinding`'s
  `StoreUpvalue` index emit → `EmitUpvalue`.
- `src/Starling.Js/Runtime/JsVm.cs` — `LoadUpvalue`/`StoreUpvalue`/
  `LoadUpvalueCell` now `ReadU16()` their index; `MakeClosure` now `ReadU16()`
  its captured count.
- `src/Starling.Js/Bytecode/Disassembler.cs` — moved the 3 upvalue opcodes into
  the u16-operand decode group; `MakeClosure` decodes its count as u16.
- `src/Starling.Js/Bytecode/Opcode.cs` — updated operand-width doc comments
  (`[u8 idx]` → `[u16 idx]`, `MakeClosure … [u16 nUpvalues]`).

## Tests added

`tests/Starling.Js.Tests/Runtime/JsManyUpvaluesTests.cs` — 3 tests (mirrors
`JsManyLocalsSlotTests`):

| Test | Pins |
|---|---|
| `Inner_function_capturing_over_255_upvalues_sums_all` | an inner closure captures 300 outer vars and sums them (`44850`) — compiles past the old 255 cap and every high upvalue index resolves to a distinct cell |
| `High_index_upvalue_resolves_to_correct_binding` | the 299th captured upvalue (index >255) resolves to the right binding (`598`), not a wrapped low index |
| `Write_back_through_high_index_captured_cell` | `StoreUpvalue` through a high-index (>255) captured cell mutates the shared cell so the outer function observes it (`5000`) |

Reproduce-first verified: before the fix the same sources threw `more than 255
upvalues per function not supported` at compile; after the fix all pass.

## Results

- `Starling.Js.Tests`: **1433 passed, 1 skipped** (baseline 1430+1 → +3 new).
- Builds green: `Starling.Js`, and `Starling.Headless` (pulls in
  `Starling.Engine` + `Starling.Bindings` + `Starling.Paint`).
- netclaw.dev render: the `more than 255 upvalues per function not supported`
  error is **gone** (0 occurrences); no `Cell`-cast or compile failures remain.
  Remaining `engine.js` warnings are unrelated third-party JS-level errors (out
  of scope — one fix only):
  - `googletagmanager.com/gtag/js`: `not a function: undefined (callee hint:
    'Nf') (at 293:133)` — gtag now **compiles** but hits a separate downstream
    runtime gap (different bug; track separately).
  - `static.cloudflareinsights.com/beacon.min.js`: `Uncaught script error
    [object Object]` (pre-existing).

## Handoff log

- **2026-05-21** agent-claude-cody: root-caused, fixed, tested; complete.
