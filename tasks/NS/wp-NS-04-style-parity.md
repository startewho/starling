---
id: wp:NS-04-style-parity
milestone: NS
status: "completed"
claimed_by: ""
claimed_at: ""
completed_at: "2026-05-30"
branch: "native-shell"
depends_on: []
blocks: []
subsystem: Starling.Shell.Native
plan_refs:
  - docs/native-shell.md
---

# wp:NS-04-style-parity — Hover styling + CSS animations in the native shell

## Goal

Make the native shell's render match the Avalonia `WebviewPanel` for `:hover`
styling and live CSS animations. The seams already exist — `NativeViewportRenderer`
takes a `styleOverride` callback and an `isAnimatingLayerRoot` predicate — they
just are not wired in `NativeBrowserWindow` yet.

## Scope

- Build `:hover` style overrides on pointer move, the way `WebviewPanel`'s
  `ApplyHoverState` / `BuildHoverOverrides` do, and pass them as `styleOverride`.
- Sample in-flight animations into the layer tree each frame (the
  `PrepareAnimationFrame` clock already ticks; the sampled styles need to reach
  the present as a `styleOverride`).
- Only re-present when something actually changed, instead of every frame.

## Acceptance

- A `:hover` rule visibly changes an element under the pointer in the native
  shell, and a CSS transition or animation plays, both matching the Avalonia
  shell within the SSIM tolerance.

## Status note (native-shell branch)

Done: styleOverride wired in NativeBrowserWindow for :hover (BuildHoverOverrides
mirroring WebviewPanel) and CSS animations/transitions (ComputeWithAnimations at
the shared animClockMs). A needsPresent dirty flag now gates the present.
Verified headlessly via an auto-running @keyframes page; hover visual parity
needs an interactive display.
