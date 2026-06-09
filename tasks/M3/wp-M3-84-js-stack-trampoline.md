---
id: "wp:M3-84-js-stack-trampoline"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "in_progress"
phase: "3"
subsystem: "Starling.Js"
depends_on:
  - "wp:M3-04g-js-async-generators"
blocks: []
claimed_by: "agent-claude-cody"
claimed_at: "2026-06-09"
branch: "feat/js-stack-trampoline"
plan_refs:
  - browser-plan/09_JS_ENGINE.md#vm
---

# wp:M3-84 — JS: decouple call frames from the native .NET stack (barrier trampoline)

## Goal
Rebuild the Starling VM call path so a JS→JS call does **not** recurse on the
native C# stack. JS→JS calls push a small heap `CallFrame` onto an
engine-managed chain and the single dispatch loop switches frames on
call/return. Only native→JS re-entries (getters/setters, `ToPrimitive`,
iterator `next`, Proxy traps, class field inits, super-ctor, cross-realm) keep
recursing, bounded by a "barrier" — the same model V8 (Ignition) and
SpiderMonkey use. Done when deep-but-finite pure JS→JS recursion (thousands of
frames) runs instead of throwing a spurious `RangeError`, real engine semantics
are preserved, and Test262 / perf gates do not regress.

## Why (evidence)
Loading `https://x.com` runs `main.368dc35a.js` + `vendor.b6dfc27a.js`, which
walk the webpack module graph (`__webpack_require__ → factory →
__webpack_require__ → …`) — deep-but-finite recursion (dozens–thousands of pure
JS→JS calls). Chrome and Firefox run it fine. Starling throws `RangeError:
Maximum call stack size exceeded`.

Root cause (diagnosed, confirmed live): the bytecode VM runs each JS call as a
recursive C# call into `JsVm.RunInner` — one ~2,800-line method whose nested
local helpers (`Push`, `Pop`, `ReadU16`, `Finish`, `CaptureJsStack`, …) capture
all its locals into a large JIT closure display. Each native frame is ~20–50 KB.
On the deferred-script path,
`RuntimeHelpers.TryEnsureSufficientExecutionStack()` trips at only **~26 nested
JS calls** (measured live; the exact host thread and its stack size were not
pinned down, but the ceiling is real). The logical cap `MaxCallDepth = 1000` is
never reached. Raising it does nothing.

## Inputs
- `wp:M3-04g-js-async-generators` complete: generators/async suspend **on the
  calling thread**. The `Suspend` opcode snapshots the live frame into a
  `ContinuationFrameState` and returns natively out of `RunInner`. `Resume`
  re-enters `Run` and restores the snapshot
  (`src/Starling.Js/Runtime/SuspendedFrame.cs`, `SnapshotFrame` ~657,
  `RunContinuationPrelude` ~839). There are **no worker threads** — an older
  revision used them, and stale doc XML under `bin/` still describes that model.
  Ignore it. This puts `SuspendedFrame.cs` **in scope**: the snapshot/restore
  code must track the CallFrame refactor (see Outputs).
- Diagnostic prerequisite (commit `a22a7219`, branch
  `fix/js-uncaught-stack-passthrough`) is **already merged into
  `feat/jint-tier1-parity`** (verified 2026-06-09; not yet in main): the JS
  stack flows through `ScriptThrow.JsStack` and logs as a `{JsStack}` field on
  `UncaughtScriptError`, so this class of failure is visible instead of
  `exception: null`. Branch this work off `feat/jint-tier1-parity` or any later
  point that contains it.

## Architecture (confirmed by exploration; line numbers re-checked 2026-06-09
on `feat/jint-tier1-parity`)
- VM is a **bytecode stack machine**: `JsVm.RunInner`
  (`src/Starling.Js/Runtime/JsVm.cs` ~496–3331) is a
  `while(true){ var op=(Opcode)code[ip++]; switch(op){…} }`.
- Per-call state is currently C# locals: operand `stack[]` (ArrayPool, 1024
  slots, ~517), `sp` (519), `locals[]` (520), `ip` (528),
  `code`/`constants`/`chunk` (params), `upvalues`, `thisV` (529), `frameStrict`
  (533), `tryStack` (`Stack<TryFrame>`, 617; `TryFrame` struct ~4560),
  `withStack` (627), `frameVarStore` (544), `currentFunction`/`newTarget`
  (params), `initDepth` (553).
- Native recursion choke point: `Call` opcode (~1801) →
  `AbstractOperations.Call` (`AbstractOperations.cs:308`) → `JsVm.CallFunction`
  (274) → `CallFunctionLocal` (291) → `Run` (449) → `_callDepth++` (477) →
  `RunInner` (480). Guard at 467–472.
- ~113 call sites across 19 files invoke `AbstractOperations.Call`/`Construct`,
  but most are **native→JS** (getters/setters, `ToPrimitive`, iterator `next`,
  Proxy traps, class field inits, super-ctor, cross-realm `WithActiveVm`). The
  x.com bug is the **pure JS→JS** `Call`/`Return` path only.
- Error stacks are already heap-resident (`JsThrow.StackFrames`, captured by
  `CaptureJsStack` ~603; formatted by `FormatJsStack` ~3355) — survives the
  refactor.
- Thread model: `Realm.ActiveVm` set/restored around `Run`; `[ThreadStatic]` arg
  pools (51); no global lock. WASM already uses a 16 MB dedicated thread
  (`WebAssemblyBinding.cs` 35, 695) — proof the codebase tolerates per-thread JS
  execution.

## Approach: barrier trampoline
Introduce a heap `CallFrame`, an explicit frame chain, and a single dispatch
loop that caches hot fields in C# locals and switches frames on call/return
without native recursion. Native→JS entry points run a **bounded nested loop**
("barrier") that returns one value or throws — these alone consume native stack.

### CallFrame (new type in JsVm.cs)
Holds: `Chunk`, cached `Code`/`Constants`, `Locals[]`, `Upvalues`,
`CurrentFunction`, `NewTarget`, `ThisV`, `FrameStrict`, `Suspension?`; mutable
`Ip`, `Sp`, `MaxSp`, `Stack[]`, `StackBase`, `InitDepth`, `FrameVarStore?`,
`DerivedThis` (moves the existing `_currentDerivedThis` side-channel onto the
frame); side structures `TryFrame[] TryStack` + `TryCount` (replaces
`Stack<TryFrame>`), `WithStack?`; bookkeeping `Caller?`, `IsBarrier`,
`Disposition` (`Call`/`Construct`/`SuperCtor`, governs return coercion).

### VM additions
`[ThreadStatic]` `_current` (frame chain head), `_frameDepth`, `_barrierDepth`.
`MaxFrameDepth = 10_000` (logical cap on pure JS→JS heap recursion — catchable
`RangeError`, no native cost). `MaxBarrierDepth = 1_000` + keep
`TryEnsureSufficientExecutionStack()` as the guard on native re-entry nesting.
The old single `_callDepth`/`MaxCallDepth` splits into these two. **Thread-static
is mandatory** because other threads run VMs concurrently (the 16 MB WASM
invocation thread, tests) and must not share one frame chain.

### Hot-field caching
Cache `ip`, `sp`, `stack`, `code`, `constants`, `locals` in C# locals in the
dispatch method (touched per-opcode). Keep cold fields (`thisV`, `frameStrict`,
`tryStack`, …) on `_current`, read directly by rare opcodes. On **push** (JS→JS
call): flush `ip`/`sp`/`maxSp` to the outgoing frame, allocate callee frame, set
`Caller`, reload cache from callee, `continue`. On **pop** (return): run
finalizers, release frame resources, restore caller, reload cache, deliver
coerced return value to caller's operand stack, `continue`.

### Barrier protocol
`CallFunction`/`ConstructFunction` (called from the ~113 native sites) call
`RunBarrier`, which saves `_current`, pushes a frame marked `IsBarrier`, bumps
`_barrierDepth` (guarded), runs the dispatch loop until that barrier frame pops,
and returns its value (or rethrows the C# `JsThrow`). Composes with itself for
native→JS→native→JS nesting. Cross-realm dispatch (`WithActiveVm`, a different VM
instance) and DirectEval **stay on the barrier path** in v1 (rare, not the hot
path).

### Explicit exception unwinding
Replace the in-loop `catch(JsThrow)` "set rethrow; native unwind" with an
explicit loop: `CaptureJsStack(ex, _current)` (preserve order), try to handle in
`_current.TryStack` (reuse today's TryBody→catch / finally-as-
`PendingCompletion.Throw` logic verbatim); if unhandled, release the frame and
unwind into `Caller`; at a barrier, rethrow the C# `JsThrow` to the native
caller. `finally`/return-in-finally/break-across-try semantics are preserved
because `DivertReturnThroughFinally`/`DivertBranchThroughFinally`/`EndFinally`/
`PendingCompletion` all operate per-frame and run before a frame pops.

## Implementation stages (one effort, sequenced)

**Stage A — extract CallFrame + de-closure helpers (still recurses; low risk).**
Move `RunInner`'s per-call locals onto a `CallFrame` allocated at entry. Keep the
hot set in C# locals. Convert the nested local functions into static helpers /
`CallFrame`/VM methods so they stop creating a JIT closure display. Call/Return
untouched — same recursion model, same exception path. This alone collapses the
per-native-frame size and should raise the practical depth from ~26 to several
hundred. Verify nothing regresses, measure the depth gain, re-test x.com.

**Stage B — trampoline Call/Return + barrier + explicit unwinding (high risk).**
Add `_current`/`_frameDepth`/`_barrierDepth` (thread-static). Rename `RunInner` →
`Dispatch(barrier)`. For ordinary same-realm `JsFunction` callees, make
`Call`/`CallMethod`/`New`/`CallApply*`/`NewApply`/`CallSuperCtor` **push a
CallFrame and `continue`** instead of calling
`AbstractOperations.Call`/`Construct`. Non-`JsFunction`/native/bound/Proxy and
cross-realm callees keep going through `AbstractOperations` → `RunBarrier`.
Convert `Return`/`ReturnUndefined`/finally-return to **pop**. Make `Suspend`
pop the current barrier frame and return the suspension sentinel (a suspendable
body is always entered natively from `SuspendedFrame.Resume` → `Run`, so its
frame is the barrier frame — see Riskiest traps). Rewrite the throw
handler as the explicit multi-frame unwind. Implement `RunBarrier`. Split the
depth caps. Move `_currentDerivedThis` and the Construct return-coercion onto the
frame. DirectEval and cross-realm stay barriers in v1.

**Stage C — shared growable value stack (optional, perf only).**
Replace per-frame ArrayPool-rented 1024-slot operand arrays with one per-thread
growable `_valueStack` + per-frame `StackBase` (the real-engine model; removes
pool churn for deep stacks). Gate strictly on `StarlingScalingBench`; skip if
Stage B perf is already fine.

## Outputs / critical files
- `src/Starling.Js/Runtime/JsVm.cs` — the whole change (CallFrame, dispatch
  loop, Call/Construct/Return opcodes, barrier, unwinding, depth caps).
- `src/Starling.Js/Runtime/AbstractOperations.cs` — `Call`/`Construct` route
  native→JS through `RunBarrier`; cross-realm stays a barrier.
- `src/Starling.Js/Runtime/SuspendedFrame.cs` — **in scope**.
  `ContinuationFrameState` mirrors the per-call locals (`Stack[]`, `Ip`, `Sp`,
  `Stack<TryFrame>`, …), so `SnapshotFrame`/`RunContinuationPrelude` must move
  to the CallFrame representation in Stage A and the `TryFrame[]` try stack in
  Stage B. Likely simplification: the snapshot becomes (or wraps) the parked
  `CallFrame` itself.
- `tests/Starling.Js.Tests/Runtime/JsVmTests.cs` — existing recursion tests +
  new ones.
- `bench/Starling.Bench/JsBench.cs` (`Run_FibRecursive_15`),
  `bench/Starling.JsEngineBench/StarlingScalingBench.cs` — perf gates.

## Riskiest correctness traps (design preserves each)
finally / return-in-finally / break-continue-across-try (per-frame, run before
pop); super-ctor return-value coercion (moves to `CoerceReturn` +
`CallFrame.DerivedThis`); tagged-template/spread calls (arg-marshal then push,
identical); getter throwing mid-expression (runs under a barrier, C# JsThrow
propagates as today); generator `yield`/`await` (safe by construction:
`yield`/`await` appear only in the body's own bytecode, and the body frame is
always entered natively from `SuspendedFrame.Resume` → `Run`, so `Suspend`
fires on a barrier frame — pop it and return the suspension; a `yield` inside
try/finally must round-trip the per-frame try stack in its new representation);
cross-realm dispatch (must stay a
barrier, never a trampoline push); abort token (check moves to top of `Dispatch`,
throws past barriers via normal C# unwinding).

## Acceptance
- **Unit:** `dotnet test tests/Starling.Js.Tests/Starling.Js.Tests.csproj`. Keep
  `Unbounded_recursion_throws_catchable_RangeError…` and
  `Recursion_RangeError_is_catchable_from_JS` green. Add: (1) ~5,000-deep pure JS
  recursion now **succeeds** (would have died at ~26); (2) a getter/ToPrimitive
  that recurses ~1,500 deep still throws a catchable RangeError (barrier guard);
  (3) deep recursion inside try/finally still runs finalizers.
- **Conformance:** Test262 harness `tests/Starling.Js.Test262.Tests/` — focus
  `language` (try/finally, generators, async, classes/super, eval) and
  Proxy/cross-realm (`$262.createRealm`). No regression vs. the pre-change
  baseline pass rate.
- **Perf gates:** `Run_FibRecursive_15` must not regress past an agreed threshold
  (proves hot-loop caching held); `StarlingScalingBench` flat or better.
- **End-to-end (the actual bug):** after Stage A and again after Stage B, drive
  the live Starling browser via its MCP (`browser_navigate` → `https://x.com`,
  then `browser_console` / `browser_inspect` / `browser_telemetry_logs`) and
  confirm the `RangeError: Maximum call stack size exceeded` against
  `main.368dc35a.js` is gone and the page renders. Rebuild + restart the `gui`
  Aspire resource first.

## Out of scope
Redesigning the generator/async model (stays continuation-based —
suspend/resume on the calling thread; only the snapshot plumbing moves to
CallFrame), DirectEval and
cross-realm trampolining (stay barriers in v1), NaN-boxing JsValue, tail-call
optimization.

## Notes
Scope decided with the user: implement the **full trampoline (Stage A + Stage B)
in one effort** — no big-stack-thread stopgap. Stage A is the internal
prerequisite for Stage B. Stage C is optional and gated on the perf bench.

## Handoff log
- 2026-06-09 — Stage A landed (agent-claude-cody). Added the heap `CallFrame`
  and removed every closure-capturing local function from `RunInner`. The
  measured cause of the tiny depth was not the closure display alone: RyuJIT
  gives every IL local in `RunInner` its own stack slot (no slot packing in a
  method this big), so the ~280 cold-arm locals cost ~7 KB per JS call frame.
  Fixed by moving cold opcode arms into `DispatchCold` and the operator arms
  into `ExecArith`/`ExecCompare`/`ExecYieldDelegate` — those frames are
  transient, so they do not multiply with JS depth. Per-call native cost fell
  from ~40 KB (~24 frames) to ~6 KB. Depth on the default Release test thread:
  92 → 232. On a 2 MB thread: 320. On an 8 MB thread the native stack no
  longer binds — recursion now hits the logical `MaxCallDepth` (1000) cap.
  Test262 `language` is 95.61%. New regression test:
  `JsRecursionDepthTests`. Note for Stage B: `SnapshotFrame` builds
  `ContinuationFrameState` from the `CallFrame` (the snapshot could become the
  parked frame itself), suspend sites flush ip/sp/maxSp to the frame first,
  and the try stack is still `Stack<TryFrame>` — the `TryFrame[]` conversion
  is still open.
- 2026-06-09 — created (agent-claude-cody); persisted from the
  `~/.claude/plans/prancy-wobbling-dawn.md` plan-mode doc (session 475e6ac5,
  2026-06-08). Diagnostic prerequisite (`ScriptThrow.JsStack` passthrough) is on
  branch `fix/js-uncaught-stack-passthrough`. Trampoline not yet started — no
  branch has `CallFrame`/`RunBarrier`/`_frameDepth` in `JsVm.cs`.
- 2026-06-09 — accuracy review (agent-claude-cody, fan-out verification against
  `feat/jint-tier1-parity`). Fixed the one significant error: generators/async
  do **not** run on worker threads (that was an older revision — stale doc XML
  under `bin/` still describes it). They suspend on the calling thread via
  `ContinuationFrameState`, which puts `SuspendedFrame.cs` in scope and changes
  the `yield`-vs-barrier argument (see Riskiest traps). Also: `a22a7219` is
  already merged into `feat/jint-tier1-parity`; refreshed drifted line numbers;
  re-entry call-site count is ~113, not ~56. Everything else checked out
  (test/bench names, Test262 harness + 37.77% baseline, `#vm` anchor, MCP tool
  names, `gui` Aspire resource, no existing trampoline code).
