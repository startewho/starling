---
id: "wp:M10-01-diagnostics-hot-path"
milestone: "M10"
status: "claimed"
claimed_by: "agent-codex-cody"
claimed_at: "2026-06-03T12:57:06Z"
completed_at: ""
branch: "perf-loop"
depends_on: []
blocks: []
subsystem: "cross-cutting"
plan_refs:
  - "browser-plan/13_MILESTONES.md#m10--hardening-security-perf"
---

# wp:M10-01 — Remove hot-path diagnostics

## Goal

Remove detailed logging, spans, counters, and timing from tight loops. Keep
HTTP/network and failure signals available by default.

## Inputs

- The Starling diagnostics facade in `src/Starling.Common/Diagnostics`.
- Engine, layout, paint, DOM, JS, GUI, and telemetry call sites found during the
  hot-path diagnostics audit.

## Outputs

- A small diagnostics mode switch for host-level telemetry sinks.
- Hot-path call sites removed, not just hidden behind flag checks.
- Normal browser and headless runs keep useful high-level signals.

## Acceptance

- JS virtual machine opcode, native-call, and string-boxing counters are removed.
- Per-element, per-inline, per-display-item, per-tile, DOM event, and GUI frame
  diagnostics are removed from the normal path.
- HTTP telemetry, including DNS/TCP/TLS/HTTP/1/HTTP/2 detail, stays on.
- GUI in-memory telemetry sinks and process resource sampling are not always on.
- Headless does not allocate console trace spans when trace output is disabled.
- `dotnet build` passes.

## Notes

- Source-generated `ILogger` is not the main fix. Most hot calls go through
  `IDiagnostics` with already-built strings.
- Keep HTTP and failure logs. They are useful and low volume.

## Handoff log

- 2026-06-03T12:55Z — created after hot-path diagnostics audit (agent-codex-cody)
- 2026-06-03T12:57:06Z — claimed by agent-codex-cody, working on perf-loop
- 2026-06-03T13:27:51Z — removed tight-loop diagnostics from JS, CSS/layout,
  paint, DOM events, and GUI frames. Kept HTTP protocol telemetry. Scoped builds
  and tests pass. Full build is blocked by existing `PageRendererHost` errors in
  `tests/Starling.Gui.Headless.Tests`.
