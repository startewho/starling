---
id: "wp:M3-84-js-stack-trampoline"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "complete"
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
  calling thread** *on this branch lineage*. The `Suspend` opcode snapshots the
  live frame into a `ContinuationFrameState` and returns natively out of
  `RunInner`. `Resume` re-enters `Run` and restores the snapshot
  (`src/Starling.Js/Runtime/SuspendedFrame.cs`). **The threading model splits
  by branch** (verified 2026-06-09): `main` still runs generator bodies on a
  dedicated worker thread (`SuspendedFrame.Start()` spawns a `Thread`, with
  `_resume`/`_yield` handoff) — the `feat/jint-tier1-parity` lineage replaced
  that with the same-thread continuation model this work builds on. **Merge
  constraint:** the trampoline must reach `main` through the parity lineage.
  A direct cherry-pick onto main's worker-thread `SuspendedFrame` will not
  work. This puts `SuspendedFrame.cs` **in scope**: the snapshot/restore code
  must track the CallFrame refactor (see Outputs).
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

Two Stage B choices that differ from the design sketch above:
- The try stack stays `Stack<TryFrame>`. The explicit unwinder reuses the
  Stage A catch/finally logic verbatim, so the `TryFrame[]`+`TryCount`
  conversion would not simplify it. Revisit in Stage C if the per-frame
  allocation shows up in the bench.
- The suspension snapshot stays `ContinuationFrameState`. Parking the
  `CallFrame` itself was the other option. The snapshot path was already
  correct and the smaller change won.
- `Disposition.SuperCtor` coerces exactly like `Construct`. A `super(...)`
  call is a [[Construct]] of the parent, and the derived-vs-base split is
  read off the callee's `ConstructorKind` at pop. The enum value only marks
  how the frame was entered.

Two choices that differ from the final plan revision (the
`prancy-wobbling-dawn` plan-mode doc), reviewed against it 2026-06-09:
- **Execution state is `[ThreadStatic]` static, not a per-VM
  `ThreadLocal<ExecState>`.** The plan wanted per-(VM, thread) state for two
  reasons. Both dissolve here. Generators have no worker threads on this
  lineage (and `[ThreadStatic]` is per-thread anyway). Cross-realm — a second
  VM on the same thread — is safe because every cross-VM entry is a barrier
  that saves and absolutely restores `t_current`/`t_frameDepth`/
  `t_barrierDepth`, `IsTrampolinable` rejects foreign-realm callees, and a
  barrier frame's `Caller` link is never traversed (pop exits the loop, the
  unwinder rethrows natively). Net difference: depth caps pool per thread
  instead of per VM — the stricter direction. Validated by
  `CrossRealmExecutionTests` (including the plan's named
  `Function_from_foreign_eval_runs_with_foreign_realm_active`) and the
  cross-realm Test262 scope.
- **Trampoline scope is wide, not the plan's narrow default.** The plan
  recommended pushing only `Call`/`CallMethod`/`New` in v1 and leaving
  `CallApply`/`CallApplyMethod`/`NewApply`/`CallSuperCtor` on the barrier,
  pending confirmation. The implementation converted all seven. The wide set
  passed every gate, and it is what makes deep spread-call and super-chain
  recursion work (narrow would still RangeError on those at barrier depth).
- The plan's "copy args into `Locals` then return the pooled buffer at push"
  instruction was found unsafe and not followed: `frame.Args` is read after
  entry by rest-param materialization, `arguments` creation, and spread
  collection, so the buffer is parked on the frame and released at pop —
  the same lifetime the old recursive model had.

## Handoff log
- 2026-06-09 — follow-up: grow-on-demand operand stacks (agent-claude-cody).
  Every frame rented a fixed 1024-slot operand stack. At trampoline depth
  that meant ~491 MB of peak heap, because the array pool cannot hold
  10,000 arrays of one size. Frames now rent 32 slots and double on demand,
  up to the same 1024 ceiling with the same overflow error. The grow helper
  is NoInlining and writes the new array to both the dispatch loop's cached
  local (by ref) and `frame.Stack`. Push takes the frame and the stack by
  ref, and DispatchCold / ExecArith / ExecCompare / ExecYieldDelegate take
  the stack by ref so a grow propagates. The unwinder's catch-value push
  routes through Push because a try frame's StackBase can equal the array
  length. Measured at 9,998 frames in Debug: peak heap delta 491 MB → 20 MB,
  allocation 248 MB → 11 MB (about 24,800 → 1,140 bytes per frame). A new
  regression test pins per-frame allocation under 4 KB. Unit suite 2,237
  green. Test262 unchanged at 95.61%. fib gate 540.5 µs after vs 574.0 µs
  before (ShortRun). Both sides used BenchmarkDotNet's in-process toolchain
  on the same machine — stale copies under `.claude/worktrees` break the
  default toolchain's project lookup.
- 2026-06-09 — gates passed, P0 review finding fixed, task complete
  (agent-claude-cody, commit `5494fb24`). An adversarial review of the full
  diff found one real bug: a derived constructor that finishes without
  running `super()` throws its ReferenceError during the pop coercion, and
  on a barrier pop the dispatch catch then released the already-released
  frame — the same pooled operand stack went to the array pool twice, and
  later unrelated code computed wrong values (`Reflect.construct(B, [])`
  then `1 + g()` gave NaN). Fixed by returning the raw value from barrier
  pops and coercing in `RunBarrier`, outside the dispatch catch, which is
  where the old native model ran it. Three regression tests added (2,235
  unit tests green, Test262 unchanged at 95.61%). Perf gates measured
  against pre-change baseline `6ef6e37c` with identical ShortRun harnesses:
  `Run_FibRecursive_15` 817 µs → 540 µs (−34%), `StarlingScalingBench`
  −26% to −37% at every N ≥ 100, allocation growth exactly one 96-byte
  `CallFrame` per JS→JS call at all scales. Stage C judged unnecessary —
  the bench got faster, not slower. End-to-end: x.com in the live GUI
  (Starling engine) loads and renders with zero console errors and zero
  RangeError/uncaught entries in the engine logs — before this work the
  same load died in the webpack module graph at ~26 native frames. Review
  also documented two pre-existing parity gaps that are NOT regressions
  (class ctors callable without `new`, `IsConstructor` true for arrows)
  and a benign pooled-args leak on throw-past-call that matches the old
  code. Known limit: x.com's title/hydration still doesn't complete — a
  separate bindings gap, same class as the angular.dev hydration issue.
- 2026-06-09 — Stage B landed (agent-claude-cody). JS→JS calls no longer
  recurse on the native stack. The dispatch loop (`Dispatch`) switches heap
  `CallFrame`s on call/return: `Call`/`CallMethod`/`New`/`CallApply`/
  `CallApplyMethod`/`NewApply`/`CallSuperCtor` push a trampolined frame for an
  ordinary same-realm plain `JsFunction`, and `Return`/`ReturnUndefined`/
  return-through-finally pop it. Native→JS entries go through `RunBarrier`
  (the old private `Run`), which pushes an `IsBarrier` frame and runs the
  loop until that frame pops. The in-loop `catch (JsThrow)` is now an
  explicit unwinder that walks the frame chain, runs each frame's
  try/finally machinery, releases its pooled arrays, and rethrows natively
  at the barrier. The construct return coercion (and the derived-class
  bound `this`, now `CallFrame.DerivedThis`) runs at frame pop, shared by
  the trampoline and the barrier. Depth caps: `MaxFrameDepth` 10,000
  (catchable RangeError, no native cost) and `MaxBarrierDepth` 1,000 plus
  the execution-stack probe. All three counters are thread-static. Measured:
  pure JS recursion on the default test thread went 232 → 9,998 (the cap).
  Unit suite 2,232 green (6 new depth tests). Test262 `language` 95.61% —
  unchanged from Stage A. fib(20) ×20 went 152 ms → 114 ms in Release (the
  trampoline is ~25% faster than native call recursion). End-to-end x.com
  re-test still pending (needs the GUI host rebuilt).
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
