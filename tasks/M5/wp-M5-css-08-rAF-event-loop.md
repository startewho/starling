---
id: wp:M5-css-08-rAF-event-loop
milestone: M5
status: "available"
claimed_by: null
claimed_at: null
branch: "main"
depends_on: []
subsystem: Starling.Loop
plan_refs:
  - browser-plan/10_WEB_APIS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-08-rAF-event-loop — requestAnimationFrame in the event loop

## Goal

Add `requestAnimationFrame` / `cancelAnimationFrame` to `WebEventLoop` and the
JS Window binding, with the per-frame timestamp + ordering semantics the HTML
spec requires.

## Inputs

- `Tessera.Loop.WebEventLoop` (microtasks + timers today).
- `Tessera.Bindings.WindowBinding`.
- HTML spec §"event loop processing model" step 11 (run-animation-frame-callbacks).

## Outputs

- `WebEventLoop` gains:
  - `int RequestAnimationFrame(Action<double> callback)` returning a handle.
  - `bool CancelAnimationFrame(int handle)`.
  - `void RunFrame(long nowMs)` (or `AdvanceToFrame(long nowMs)`): sets the
    monotonic clock, drains microtasks, fires due timers, drains the rAF
    queue (snapshotted; callbacks scheduled *during* drain run next frame),
    drains microtasks again.
  - All rAF callbacks in one frame see the same `nowMs` value.
- `WindowBinding.requestAnimationFrame(JsFunction)` /
  `WindowBinding.cancelAnimationFrame(JsNumber)` route through the loop.
- Tests:
  - One rAF fires once on next frame.
  - Nested rAF schedules for the *following* frame, not the current.
  - Cancel before fire removes the callback.
  - All callbacks in a frame observe the same timestamp.

## Acceptance

- Existing `WebEventLoop` tests stay green.
- Window binding test calls `requestAnimationFrame(cb)`, then
  `loop.RunFrame(16)`, asserts `cb` ran with timestamp 16.

## Notes

- No engine wiring here — `TesseraEngine.RenderFrame` is wp:M5-css-10.
- Keep `AdvanceBy` for back-compat; have it call `RunFrame(_nowMs + n)` for
  the rAF phase to fire.

## Handoff log

- 2026-05-19T16:25Z — created (agent-copilot-claude-opus-4.7).
