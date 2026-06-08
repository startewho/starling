---
id: wp:M12-15-webgpu-shutdown-lifetime
milestone: M12
status: "complete"
claimed_by: "agent-codex-cody"
claimed_at: "2026-06-03T14:41:42Z"
completed_at: "2026-06-03T14:44:22Z"
branch: "main"
depends_on:
  - wp:M12-13-gpu-composite-blend
blocks: []
subsystem: Starling.Paint
plan_refs:
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
  - layer-tree-plan.md
---

# wp:M12-15-webgpu-shutdown-lifetime — Dispose borrowed WebGPU state before shutdown

## Goal

Fix the close-time macOS crash where ImageSharp's WebGPU process-exit hook
clears an uncaptured-error callback on a Starling-owned device after Starling
has released that device.

## Inputs

- `wp:M12-13-gpu-composite-blend` added Starling-owned WebGPU devices.
- The ImageSharp WebGPU context can wrap an external device and keeps
  process-wide state for that device.

## Outputs

- Starling removes ImageSharp's device-state cache entry before releasing a
  Starling-owned WebGPU device.
- The lifetime path stays scoped to the WebGPU paint/compositor path.

## Acceptance

- Closing Starling.Gui after rendering on the WebGPU surface does not crash in
  `wgpuDeviceSetUncapturedErrorCallback`.
- A paint test proves disposing a GPU engine removes ImageSharp's device-state
  cache entry for the external device.
- `dotnet build` and the focused paint tests pass.

## Handoff log

- 2026-06-03T14:41:42Z — claimed by agent-codex-cody. Crash report points to
  ImageSharp WebGPU process-exit cleanup touching a released Starling device.
- 2026-06-03T14:44:22Z — complete. Starling now removes ImageSharp's borrowed
  WebGPU device state before releasing the Starling device. Added a paint test
  that proves the cache entry is gone after disposal. Focused Paint and GUI
  builds pass. `Starling.Paint.Tests` passes. Full solution build is blocked by
  existing headless GUI tests that still reference `PageRendererHost`.
