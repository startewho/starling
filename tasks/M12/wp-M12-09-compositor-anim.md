---
id: wp:M12-09-compositor-anim
milestone: M12
status: "available"
claimed_by: ""
claimed_at: ""
completed_at: ""
branch: "main"
depends_on:
  - wp:M12-06-invalidation
  - wp:M12-07-compositor-thread
blocks: []
subsystem: Starling.Paint
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-09-compositor-anim — Run transform/opacity animations on compositor

## Goal

CSS animations / transitions on `transform` and `opacity` of a *promoted*
layer must run on the compositor thread without invalidating the layer's
tiles. The layer's bitmap is painted once; the composite step applies the
animated matrix or opacity per frame.

Today, `wp:M5-css-09-animation-compositor` (despite the name) only routes
animations through the main-thread frame loop and re-paints every frame.
This WP makes the optimization real: transform/opacity animation on a
promoted layer = pure composite, no paint, no layout, no style recomputation
(except the timeline tick).

## Inputs

- `wp:M12-06-invalidation` shipped: animations can drive transform without
  bumping any tile's version.
- `wp:M12-07-compositor-thread` shipped: compositor reads from immutable
  snapshots; animations write to a sidecar timeline the compositor reads
  per frame.
- Animation engine from M5 (`Tessera.Css.Animation`).

## Outputs

- A `CompositorAnimationChannel`: per-layer ring buffer of
  `(timestamp, transform, opacity)` samples published by the animation
  engine on the main thread, consumed by the compositor at composite time.
- `CompositorLayer.EffectiveTransform(time) → Matrix3x2` and
  `EffectiveOpacity(time) → float` interpolating between samples.
- A new fast-path in `Compositor.Render`: if no display-list change since
  the last frame AND every dirty layer is dirty *only* because of an
  animated transform/opacity, then skip paint entirely and only re-blit
  layer caches with new transforms.
- A diagnostics gauge `paint.compositor.anim_only_frames` distinguishing
  paint-free animation frames from paint frames.

## Acceptance

- A test with a `transform: translateX(...)` keyframe animation on a
  promoted div runs 60 fps composite frames with zero entries in
  `paint.invalidation.tiles_dropped` and zero `paint.tile.cache_miss`.
- An animation on a *non*-promoted box still re-paints (regression guard:
  this WP must not silently promote everything).
- Pausing the animation while scrolling: composite continues to read the
  last sample; no main-thread round-trip.
- `dotnet build && dotnet test` green.

## Notes

- This closes the "good demoable" loop for #3: smooth scroll, smooth
  CSS animations, no paint storm when one button hover-fades.
- Web Animations integration is out of scope; this is a CSS-only fast
  path. WA can route through the same channel later.
- After this WP, write a brief `browser-plan/08_FONTS_PAINT.md` section
  documenting the compositor + layer-tree + tile cache + animation
  channel architecture end to end so M13+ contributors can plug in.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
