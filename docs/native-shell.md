# Native shell (Silk.NET + wgpu)

`Starling.Shell.Native` is a browser shell that does not use Avalonia. It owns
the window itself and presents the page straight to a wgpu swapchain. There is
no readback and no second copy. The old Avalonia path renders the page to a CPU
bitmap, copies it into a `WriteableBitmap`, and lets Skia upload it again. The
native shell skips all of that.

The Avalonia shell (`Starling.Gui`) still works and ships. The native shell grows
beside it until it reaches the same features. Then we can pick one.

## Why this exists

The GPU compositor (`wp:M12-13-gpu-composite-blend`) builds each frame on the
GPU. The Avalonia path then reads it back to the CPU and re-uploads it. That
round trip is the cost we want gone. Living inside Avalonia's renderer makes the
round trip hard to remove — sharing a GPU texture across wgpu and Skia needs a
shared OS surface and per-platform plumbing.

Owning the window removes the problem. wgpu can present straight to the window's
own surface. The bindings we already have expose this on every desktop platform
(`SurfaceDescriptorFromMetalLayer` on macOS, `...FromWindowsHWND`,
`...FromXlibWindow`, `...FromWaylandSurface`). So the present path is a solved,
fully supported swapchain present — not a native shim we have to build.

## How it works

The pipeline, bottom to top:

- **`GpuBlendEngine`** (`Starling.Paint`) — the reusable wgpu blend engine: the
  device, a render pipeline per target color format, the sampler, and the
  per-layer texture cache keyed by slice content hash. A layer uploads once and
  stays resident across frames.
- **`GpuLayerCompositor`** (`Starling.Paint`) — the offscreen front end. Blends
  into a texture and reads it back to a CPU bitmap. This is the M12-13 path that
  the Avalonia shell and the tests use. Its output is unchanged.
- **`GpuSurfacePresenter`** (`Starling.Paint`) — the on-screen front end. Builds
  a surface-compatible device from a window and blends each frame's layers
  straight into the swapchain texture, then presents. No readback. The only
  per-frame transfer left is uploading a layer whose content actually changed.
- **`Compositor.RenderToSurface` / `AppendSurfaceOps`** (`Starling.Paint`) —
  build the same paint-ordered blend ops as the offscreen render, but hand them
  to the presenter. `AppendSurfaceOps` offsets a layer tree into a sub-region of
  the surface, so several documents (chrome plus page) blend into one present.
- **`NativeViewportRenderer`** (`Starling.Gui.Core`) — the Avalonia-free render
  seam. Builds a page's layer tree and presents it. `PresentComposited` draws
  engine-rendered chrome above the page in one frame. This is the boundary that
  lets the engine render without depending on any shell.
- **`NativeBrowserWindow`** (`Starling.Shell.Native`) — the window, the live
  loop, and input.

The native dylibs (wgpu and GLFW) copy into the shell's output on their own,
through the `Starling.Engine` → `Starling.Paint` → `Silk.NET.WebGPU` reference
chain. No custom build step.

## What works today (phases 1–3 plus one phase-4 service)

- Zero-copy present of the GPU compositor to the window. Proven at Retina scale
  (device pixel ratio 2.0), with no surface-reconfigure failures.
- Real page load through `BrowserSession` (any `file://`, `http://`, or
  `https://` URL).
- The live loop: the JavaScript pump, relayout when the document changes, and the
  animation tick.
- Input through Silk.NET: scroll, hover (the OS cursor follows the hit-tested
  element), click (link navigation, text-field focus, and DOM click dispatch),
  and typing into a focused field.
- Engine-rendered chrome: the URL bar is its own Starling document, composited
  above the page in the same present. No overlay and no "airspace" clipping
  problem, because the chrome is drawn by the engine, not a foreign control.
- Clipboard: copy, cut, and paste on the focused field, through GLFW.

Run it: `dotnet run --project src/Starling.Shell.Native -- --browser`. The flags
`--spike` (clear-and-present only) and `--frames N` (auto-close after N frames)
help with smoke tests.

## The remaining native-services tail (phase 4)

These are the things the Avalonia shell gives for free and the native shell must
still build. They are tracked so the work is resumable.

- **Input method editor (IME).** Composition for Chinese, Japanese, Korean, and
  dead keys. Silk.NET does not provide this. It is native and per-platform. Easy
  to underestimate. Tracked as `wp:NS-01-ime`.
- **Accessibility.** Avalonia exposes a platform accessibility tree for free.
  Rebuilding it is the largest single item and the hardest. Decide early whether
  it is required. Tracked as `wp:NS-02-accessibility`.
- **Shell parity.** Tabs, history navigation in the UI, the find bar, the
  devtools panels, context menus, drag and drop, and multi-window. Most of this
  can be engine-rendered chrome, which is the elegant part of owning the window.
  Tracked as `wp:NS-03-chrome-parity`.
- **Render fidelity parity.** The native shell does not yet apply `:hover` style
  overrides or sample CSS animations into the layer tree the way the Avalonia
  `WebviewPanel` does. The hooks are in place (`styleOverride`,
  `isAnimatingLayerRoot`); they need wiring. Tracked as `wp:NS-04-style-parity`.

## Notes and limits

- The chrome document is laid out with the default text measurer, not the paint
  backend's measurer, so its text metrics can differ slightly. The page itself
  uses the engine's own measurer through `BrowserSession`, so the page is exact.
- Headless tests cannot capture a native-window present. The CPU readback path
  (`GpuLayerCompositor`) stays as the test and headless harness, and its
  byte-for-byte parity with the CPU blend is pinned by `GpuCompositeParityTests`.
- A window needs a display. On a headless host the present cannot run, but the
  offscreen compositor still works.
