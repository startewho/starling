# Compositor thread — scope

## The problem

Animations freeze while you click and drag (for example, a drag to select text).
On the GPU surface path, every pointer-move presents a frame straight from the
user-interface (UI) thread, and that present is synchronous. It blocks the UI
thread on the swapchain's `nextDrawable` wait (first-in-first-out vsync, up to
~1 second under starvation). The animation pump is a 16 ms `DispatcherTimer` at
Background priority — below pointer Input. So a steady drag keeps the UI thread
busy with higher-priority input and blocking presents, and the timer never runs.
The animation clock stops advancing until the drag ends, then jumps to catch up.

The fix scoped here removes the present from the UI thread entirely. A dedicated
**compositor thread** owns the GPU surface and presents on its own loop. Input
can never starve or block it.

This is the browser **main-thread / compositor-thread** split. It helps well
beyond this one bug.

## What can and cannot leave the UI thread

The Starling JavaScript engine, the DOM, layout, and the CSS animation engine
are single-threaded and hold no locks. They must stay on one thread. The overlay
highlights are Avalonia controls, also UI-thread-bound.

So the split is:

- **Producer (UI thread)** keeps everything that reads live state: the
  JavaScript frame pump, relayout, animation sampling, the layer-tree build
  (`LayerTreeBuilder.Build` walks the live layout tree), tile raster and upload,
  and the overlay snapshot. It produces an immutable **frame packet** and
  publishes it.
- **Compositor thread** does only `presenter.PresentOps`: acquire the drawable,
  blend the already-resident textures, present. Then it paces itself like the
  native shell loop does (`NativeBrowserWindow.cs:820-937`).

The present pipeline already cuts cleanly at this line. `Compositor.RenderToSurface`
builds the ops list and uploads tiles, then calls `PresentOps`
(`Compositor.cs:186-216`). We split it into build-and-publish versus present.

## The frame packet

`FramePacket` is an immutable value handed from producer to compositor:

- **Ops** — a copy of the `LayerBlend[]` the producer built. `LayerBlend` is
  already a readonly struct (`GpuLayerCompositor.cs:20`). The producer copies the
  list so it can build the next frame without touching the in-flight one.
- **DeviceWidth, DeviceHeight** — `ceil(viewport × scale)`. These alone drive
  `SurfaceConfigure`. The producer must clamp them to at least 1 and at most the
  GPU's max texture size before publishing, or `Configure` throws on the
  compositor thread and kills the loop.
- **Scale** — carried only to check it against the screen scale the UI thread set.
  The compositor never touches the `NSView` or its `CAMetalLayer`.
- **Generation** — a counter the producer bumps on every publish.
- **ReferencedHashes** — the set of texture content hashes the ops name. This is
  the pin set the retirement rule needs (below).
- **CpuPixelRefs** — the `RenderedBitmap` references for any pixel-bearing ops.
  The producer must not recycle or change a bitmap that an in-flight packet still
  points at.

The mailbox is a single slot, latest-wins. At most one packet is in flight, so
the pin set is small: the live packet plus the one the compositor holds.

## Sync protocol

**Pacing.** The compositor paces present at vsync (FIFO already blocks for us)
plus a short sleep for idle frames. The producer is paced by an **off-dispatcher
clock thread** that wakes about every 16 ms and posts `produceFrame` to the UI
thread at Render priority, with a one-in-flight guard so an input burst can't
queue a backlog. The clock thread is never a dispatcher queue item, so it keeps
ticking through a drag. This is the load-bearing change — a plain
`DispatcherTimer`, even at Render priority, does not fix the macOS freeze.

**Dirty model.** Two producer bools replace today's present-everywhere calls,
mirroring the native shell's `needsPresent` flag:

- `_needsRebuild` — content, layout, scroll, hover, or animation changed. The
  producer rebuilds the layer tree and republishes.
- `_needsPresent` — only an overlay changed (caret blink, selection, find). The
  producer reuses the last ops, takes a fresh overlay snapshot, bumps the
  generation, and republishes. No rebuild, no raster.

Input handlers set a bool instead of presenting inline. The selection-flash fix
already added `_suppressPresent` coalescing — this model subsumes it. An overlay
mutation just sets `_needsPresent`, and the once-per-tick drain collapses the
clear-then-rebuild into one packet. That fixes the flash structurally.

**Upload-before-present ordering.** The producer finishes every tile upload for
generation N before it publishes. Publishing is the happens-before edge. The
compositor's first GPU write for N comes after it reads the packet. Same queue,
ordered writes, so upload-for-N happens before present-for-N.

**The `_gate` lock.** `GpuSurfacePresenter._gate` already serializes
`Configure`, `PresentOps`, `AdoptTexture`, and `Dispose`. After the split it is
the cross-thread guard for the shared GPU device and the plain `_textures`
dictionary. One caution: the producer must not call a `_gate` method while the
compositor sits in a ~1 second drawable stall, or the next frame's upload blocks
on the lock and re-couples production to present. Finish uploads before handoff
and keep each locked section short.

**Resize, scale, teardown.** `SurfaceConfigure` becomes compositor-thread-only —
remove the UI-thread `Configure` in `EnsureSurfaceTarget` (`WebviewPanel.cs:584`)
and let the first packet drive it. `NSView` geometry and screen-scale writes stay
on the UI thread. A bad acquire status during resize (Outdated or Lost) must skip
and reconfigure, not throw (today it throws at `GpuSurfacePresenter.cs:172-179`).
A `surface-alive` flag flips false in `PageSurfaceHost.DestroyNativeControlCore`.
`WebviewPanel.Dispose` and detach must signal-stop and join the compositor thread
before disposing the surface, so no present runs on a freed layer.

## The hard part: texture retirement

This is the one piece that is genuinely new and the long pole. Two adversarial
reviews returned **partial** here — the abstract idea is sound, but the engine
has none of the machinery today, so the design must add it.

Resident layer textures live in `GpuBlendEngine._textures`, a plain dictionary on
the surface's GPU device. The Avalonia host has one device per surface — raster,
upload, and present all share it. Eviction frees the GPU texture by age and by a
byte budget, and runs at the end of `PresentOps` (`GpuBlendEngine.cs:803-841`).
None of it knows about generations.

The danger is not concurrent device access — `_gate` covers that. It is
**use-after-free of the ops list**. While the compositor blends packet N, the
producer can build packet N+1, and a changed or evicted tile frees a texture
packet N still names. `RecordBlend` then binds a freed texture and the GPU
crashes.

The protocol to add:

1. Stamp each packet with a generation and its referenced hashes (the pin set).
2. The compositor records the highest generation it has fully presented (a
   volatile watermark).
3. Eviction and overwrite never free a hash in the pin set. Frees become
   deferred: move the texture to a retire queue tagged with its last-using
   generation, and reclaim only when that generation is at or below the
   watermark. Under FIFO the GPU may still read a just-presented texture, so the
   free trails by one fully-presented generation (or waits on a device-done
   signal).
4. Reclaim runs on the UI thread at the top of the next build, so the unlocked
   `_textures` dictionary is never touched by two threads.
5. If a hash is somehow missing at draw time, skip the op and request a fresh
   frame instead of throwing. A protocol bug then drops one tile, it does not
   crash the compositor.

## Phased plan

Each phase ships behind `STARLING_COMPOSITOR_THREAD`, default off, with today's
inline path as the fallback.

- **P0 — Frame packet and pipeline cut (~1-2 days).** Add `FramePacket`. Split
  `RenderToSurface` into `BuildPacket` and `PresentPacket`. The inline path calls
  both back-to-back, so behavior is identical. This proves the cut line.
- **P1 — Retirement protocol, still single-threaded (~3-4 days).** Add the pin
  set, the watermark, deferred frees, and skip-not-throw. Prove it on the inline
  path before any thread exists. This is the long pole and should stay in the
  main tree, not a parallel worktree — it touches the unlocked texture dictionary
  and the GPU device.
- **P2 — Compositor thread, mailbox, clock thread (~3-4 days).** Move
  `PresentOps` onto the thread. Add the single-slot mailbox and the off-dispatcher
  clock. Make `Configure` compositor-only. Skip-and-reconfigure on a bad acquire.
- **P3 — Dirty-flag model (~2-3 days).** Convert the ~20 inline present call sites
  to set `_needsRebuild` or `_needsPresent`. Add overlay-only republish. Subsume
  `_suppressPresent`. Route caret blink to `_needsPresent` so it stops forcing a
  full rebuild twice a second.
- **P4 — Teardown, resize, rollout (~2-3 days).** Surface-alive flag, join-before-
  dispose, `XInitThreads` on Linux. Soak the drag-freeze repro. Flip the default
  on for macOS first.

Overall about 11-16 working days for one engineer.

## Files touched

- `Starling.Paint/Compositor/FramePacket.cs` — new immutable packet.
- `Starling.Paint/Compositor/Compositor.cs:186-216` — split into `BuildPacket` and
  `PresentPacket`; clamp device size.
- `Starling.Paint/Compositor/GpuBlendEngine.cs:803-841,509-515` — pin set,
  watermark, deferred frees; skip-not-throw in `RecordBlend` (`:759`).
- `Starling.Paint/Compositor/GpuSurfacePresenter.cs:151-269` — skip-and-reconfigure
  on bad acquire; advance the watermark on present; `Configure` stays gated but
  compositor-only.
- `Starling.Gui.Core/Rendering/NativeViewportRenderer.cs:65-104` — split build from
  present; own the generation counter.
- `Starling.Gui.Core/Rendering/RenderSession.cs:325-351` — expose build and present
  so the panel can publish a packet.
- `Starling.Gui/Controls/WebviewPanel.cs` — compositor loop, clock thread, mailbox,
  surface-alive flag; the dirty-flag model; start the thread in
  `EnsureSurfaceTarget`; join before dispose.
- `Starling.Gui/Controls/PageSurfaceHost.cs:93` — flip surface-alive on destroy;
  `XInitThreads` on Linux.

## Test plan

Most of this can be tested without a GPU, which matters — the WebviewPanel
headless tests cannot run without a real surface in a sandbox (see the memory
note).

- Retirement protocol with a fake engine that records frees: a hash named by the
  live or in-flight packet is never freed; a hash frees only after its generation
  clears the watermark; an overwrite of a still-referenced hash defers the free.
- Single-slot mailbox: the consumer never sees a generation go backward, never
  reads a torn packet, and dropped intermediate packets free no pinned texture.
- Dirty-flag drain logic: rebuild absorbs present; overlay-only republish reuses
  ops and bumps the generation; a clear-then-rebuild publishes one final packet
  (the flash invariant), counted by the re-pointed `OverlayPresentRequestsForTest`.
- Producer/consumer stress with a stub presenter that sleeps a random 0-1000 ms
  inside `PresentOps`: the producer's publish never blocks for long, generations
  advance, and `surface-alive=false` mid-present makes the next loop skip cleanly.
- Teardown ordering: the thread is joined before the presenter is disposed in both
  orders, and no present runs after the flag flips.
- Manual on macOS: reproduce the drag freeze on an animated page, confirm the
  animation keeps advancing and the frame rate stays near 60 during the drag with
  the flag on, and confirm the inline path still passes with the flag off.

## Open decisions (need a human call)

1. **Watermark depth** — free at one generation behind the watermark (simple,
   one extra resident generation) versus wait on a GPU device-done signal
   (tighter memory, more native code). Recommend one-behind.
2. **Mailbox depth** — single-slot latest-wins (recommended) versus a 2-deep ring
   (smoother under jitter, doubles the pin set).
3. **Clock model** — confirm the dedicated clock thread. A higher-priority
   `DispatcherTimer` alone does not fix the macOS freeze.
4. **Rollout** — keep the inline path permanently for non-surface hosts, or
   remove it once macOS, Windows, and Linux all prove out.
5. **Producer's own GPU device for raster** — would remove all shared-queue
   contention, but it is a larger change. Likely a later initiative. Recorded
   here so it is not lost.
