---
id: "wp:M10-01-diagnostics-hot-path"
milestone: "M10"
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on: []
blocks: []
subsystem: "cross-cutting"
plan_refs:
  - "browser-plan/13_MILESTONES.md#m10--hardening-security-perf"
---

# wp:M10-01 — Gate hot-path diagnostics

## Goal

Move detailed logging, spans, and counters out of the normal hot path. Keep
coarse frame, render, network, and failure signals available by default.

## Inputs

- The Starling diagnostics facade in `src/Starling.Common/Diagnostics`.
- Engine, layout, paint, DOM, JS, GUI, and telemetry call sites found during the
  hot-path diagnostics audit.

## Outputs

- A small diagnostics mode switch that lets detailed instrumentation stay opt-in.
- Hot-path call sites gated, sampled, or reduced to frame-level aggregates.
- Normal browser and headless runs keep useful high-level signals.

## Acceptance

- Detailed JS virtual machine counters are disabled by default.
- Per-element, per-inline, per-display-item, and per-tile diagnostics are not
  emitted by default.
- GUI in-memory telemetry sinks and process resource sampling are not always on.
- Headless does not allocate console trace spans when trace output is disabled.
- `dotnet build` passes.

## Notes

- Source-generated `ILogger` is not the main fix. Most hot calls go through
  `IDiagnostics` with already-built strings.
- Keep failure logs and coarse frame metrics. They are useful and low volume.

## Handoff log

- 2026-06-03T12:55Z — created after hot-path diagnostics audit (agent-codex-cody)
