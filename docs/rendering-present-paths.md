# Rendering present paths

The Avalonia GUI has **two** ways to get a rendered page onto the screen. They
are **not** equal — one is the path, the other is a legacy fallback.

## 1. Zero-copy GPU surface — *the one true present path*

The page is composited by the layer-tree compositor (`NativeViewportRenderer` →
`Compositor.RenderToSurface` → `GpuSurfacePresenter`) **straight onto a GPU
swapchain** embedded in the page region (a `CAMetalLayer` on macOS). Each
promoted layer's raster is cached as a GPU texture keyed by its per-tile content
hash, so an animation- or scroll-only frame re-blits resident textures in one
pass. **No GPU→CPU readback, no `WriteableBitmap` re-upload.**

This is the path whenever a GPU adapter is available — which is essentially
always on a normal desktop. It handles scrolling, inner `overflow:scroll`
containers (per-container offsets are threaded into the layer tree), animation,
overlays (caret/selection/find), and chrome compositing.

## 2. Readback bitmap — *legacy fallback only*

The page is composited offscreen, **read back to a CPU bitmap** (a blocking
GPU→CPU map on the UI thread), wrapped in a `WriteableBitmap`, and handed to
Avalonia (`_pageImage` + `RenderPageBitmap`).

**This path is legacy and must never be the steady state on a GPU host.** The
synchronous readback blocks the render thread on GPU completion every frame and,
on a stalled/lost device, can freeze the window. It exists only so the browser
still renders where the zero-copy surface genuinely cannot:

- **no GPU adapter / surface** (CI, a headless sandbox, a host whose
  `CAMetalLayer` never materialised, or swapchain setup threw); or
- **explicit developer opt-in** — `STARLING_FORCE_READBACK=1` — for debugging the
  GPU path or working around a driver bug.

If you find the readback path running on a normal GPU machine, that is a **bug**,
not a configuration. Historically two things pushed pages onto it needlessly and
are now fixed:

- **Whole-layer cache invalidation.** The tile cache validated freshness against
  the *whole layer's* content hash, so any change (a caret blink, a hover, one
  text node) re-rastered every visible tile. Tiles now key on a **per-tile**
  content hash (`DisplayListContentHash.Prepare` / `HashForTile`), so a localized
  change only re-rasters the tiles it overlaps.
- **Scroll declined the surface.** A page with a scrolled `overflow:scroll`
  subtree used to *decline* the surface path (it didn't thread per-container
  scroll offsets) and drop to readback permanently after the first inner scroll.
  Those offsets are now threaded through `LayerTreeBuilder` →
  `DisplayListBuilder.BuildLayerSlice`, so the surface path renders inner-scrolled
  pages and no longer declines.

## Forcing the readback path (debugging only)

```bash
STARLING_FORCE_READBACK=1 dotnet run --project src/Starling.Gui
```

Use this only to compare the two paths or to isolate a GPU/driver issue. Leave it
unset in normal use.
