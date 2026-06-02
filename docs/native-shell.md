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

## Native services (phase 4)

These are the things the Avalonia shell gives for free. The native shell builds
them incrementally. Status below; the rest is tracked so it stays resumable.

- **Clipboard — done.** Copy, cut, and paste on the focused field, through GLFW.
- **Accessibility — managed core + macOS bridge done; native tuning open.** The
  accessibility tree (roles, names, values, bounds, focus) is a tested,
  engine-agnostic builder in `Starling.Gui.Core` (`AccessibilityTreeBuilder`,
  13 tests). A macOS `NSAccessibility` bridge exposes it on the content view and
  is pushed on load/navigation/relayout. The role and label path is exact; the
  on-screen frame conversion is best-effort and wants VoiceOver tuning on a real
  Mac. Tracked as `wp:NS-02-accessibility`.
- **Input method editor (IME) — commit works; preedit open.** Commit-style IME
  already reaches a focused field, because GLFW delivers committed composed
  characters through its character callback. The composition model
  (`Starling.Gui.Core.Text.ImeComposition`, 9 tests) is built and shaped like
  `NSTextInputClient`. The remaining native work is the preedit driver (a custom
  `NSView` or swizzling, since GLFW has no preedit callback) and inline preedit
  rendering. Tracked as `wp:NS-01-ime`.
- **Shell parity — open.** Tabs, history navigation in the UI, the find bar, the
  devtools panels, context menus, drag and drop, and multi-window. Most of this
  can be engine-rendered chrome, which is the elegant part of owning the window.
  Tracked as `wp:NS-03-chrome-parity`.
- **Render fidelity parity — open.** The native shell does not yet apply `:hover`
  style overrides or sample CSS animations into the layer tree the way the
  Avalonia `WebviewPanel` does. The hooks are in place (`styleOverride`,
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
