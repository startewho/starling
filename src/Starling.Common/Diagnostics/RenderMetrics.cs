namespace Starling.Common.Diagnostics;

/// <summary>
/// Canonical names for the render/paint/host telemetry the engine emits through
/// <see cref="IDiagnostics"/>. Producers (compositor, layer-tree builder, GPU
/// blend, the Avalonia host loop) and the telemetry daemon both reference these
/// constants so a metric name never drifts between the side that records it and
/// the side that queries/correlates it.
///
/// Convention: <see cref="IDiagnostics.Span"/> takes (area, operation) and the
/// resulting span name is <c>area.operation</c>; <see cref="IDiagnostics.Counter"/>
/// and <see cref="IDiagnostics.Gauge"/> take the full instrument name. The
/// constants below follow that split — area/op pairs for spans, full names for
/// counters and gauges.
/// </summary>
public static class RenderMetrics
{
    // ── Span areas (IDiagnostics.Span first argument) ──────────────────────
    public const string PaintArea = "paint";
    public const string GuiArea = "gui";
    public const string LayoutArea = "layout";
    public const string ShellArea = "shell";

    // ── Span operations (second argument) → span name "<area>.<operation>" ──
    /// <summary>gui.frame — host per-frame loop span; the root every paint phase nests under.</summary>
    public const string FrameOp = "frame";
    /// <summary>gui.render — one Avalonia viewport render/present.</summary>
    public const string RenderOp = "render";
    /// <summary>paint.layertree.build — rebuild of the layer tree from the box tree.</summary>
    public const string LayerTreeBuildOp = "layertree.build";
    /// <summary>paint.contenthash.compute — per-layer display-list content hash walk.</summary>
    public const string ContentHashOp = "contenthash.compute";
    /// <summary>paint.tile.raster — aggregate per-frame tile raster cost (missed tiles).</summary>
    public const string TileRasterOp = "tile.raster";
    /// <summary>paint.composite — compositor output assembly (alloc + fill + blend).</summary>
    public const string CompositeOp = "composite";
    /// <summary>paint.gpu.readback — blocking GPU→CPU readback on the render thread.</summary>
    public const string GpuReadbackOp = "gpu.readback";
    /// <summary>paint.present.acquire — block waiting for the swapchain's next drawable (SurfaceGetCurrentTexture / Metal nextDrawable).</summary>
    public const string PresentAcquireOp = "present.acquire";
    /// <summary>paint.present.encode — upload changed layers, build + submit the blend command buffer (QueueSubmit).</summary>
    public const string PresentEncodeOp = "present.encode";
    /// <summary>paint.present.swap — SurfacePresent: hand the rendered frame to the compositor for display.</summary>
    public const string PresentSwapOp = "present.swap";
    /// <summary>layout.relayout — full relayout triggered by a layout-invalidation bump or resize.</summary>
    public const string RelayoutOp = "relayout";

    // ── Counters (monotonic) ───────────────────────────────────────────────
    public const string FrameBudgetOverrun = "gui.frame.budget_overrun";
    public const string TileCacheHit = "paint.tile.cache_hit";
    public const string TileCacheMiss = "paint.tile.cache_miss";
    public const string TileEvict = "paint.tile.evict";
    public const string TileRastersPerFrame = "paint.tile.rasters_per_frame";
    public const string LayerContentHashChanged = "paint.layer.content_hash_changed";
    public const string SurfaceDeclined = "gui.surface.declined";
    public const string PresentCause = "gui.present.cause";

    // ── Gauges (sampled level) ──────────────────────────────────────────────
    /// <summary>End-to-end frame time on the host loop; the primary interactivity signal.</summary>
    public const string FrameTimeMs = "gui.frame.time_ms";
    /// <summary>Fraction of visible tiles that missed the cache this frame (0..1).</summary>
    public const string TileMissRatio = "paint.tile.miss_ratio";
    /// <summary>Milliseconds the render thread blocked waiting on the GPU buffer map.</summary>
    public const string GpuMapWaitMs = "paint.gpu.map_wait_ms";
    /// <summary>Bytes allocated for the compositor output buffer this frame.</summary>
    public const string CompositeOutputAllocBytes = "paint.composite.output_alloc_bytes";
    /// <summary>Resident bytes in the session tile cache (LRU).</summary>
    public const string TileBytes = "paint.tile.bytes";

    // ── Process resource gauges (local machine usage for span correlation) ──
    // Emitted by ProcessResourceSampler on a fixed cadence. The daemon joins
    // these to each span's [start,end] window so a slow frame can be attributed
    // to CPU saturation or memory/GC pressure on the local computer.
    /// <summary>Process CPU utilization across all logical cores, 0..1.</summary>
    public const string ProcessCpuUtilization = "process.cpu.utilization";
    public const string ProcessCpuCores = "process.cpu.logical_cores";
    /// <summary>Resident working set in bytes.</summary>
    public const string ProcessMemoryWorkingSet = "process.memory.working_set";
    /// <summary>Managed (GC) heap size in bytes.</summary>
    public const string ProcessMemoryManaged = "process.memory.managed";
    public const string ProcessHeapBytes = "process.memory.gc_heap_bytes";
    public const string ProcessGcGen0 = "process.gc.gen0_collections";
    public const string ProcessGcGen1 = "process.gc.gen1_collections";
    public const string ProcessGcGen2 = "process.gc.gen2_collections";
    public const string ProcessThreads = "process.threads";

    /// <summary>
    /// The metric name prefix that identifies a local-machine resource sample.
    /// The daemon uses this to recognise which measurements feed correlation.
    /// </summary>
    public const string ProcessPrefix = "process.";
}
