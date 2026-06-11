using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Layout;
using Starling.Paint.Backend;
using Starling.Paint.Cache;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint.Compositor;

/// <summary>
/// Paints a <see cref="CompositorLayer"/> tree into a single viewport bitmap.
/// Each layer's display-list slice is rasterized into its own layer-local bitmap
/// (served from the layer's <see cref="PictureCache"/> on a hit), then the layer
/// bitmaps are composited top-down with each layer's effective transform /
/// opacity / clip applied — composed with its ancestors'. Compositing is pure
/// managed: a layer bitmap is alpha-over blended into the output via an
/// inverse-mapped bilinear sample, so an upright layer raster lands rotated /
/// scaled exactly where its transform places it.
/// </summary>
internal sealed class Compositor
{
    private readonly IPaintBackend _backend;
    // Session-scoped per-layer tile cache. Supplied by the host so tiles
    // persist across frames; one-shot renders / tests get a private grid so they still
    // tile (and stay self-contained) without cross-frame reuse.
    private readonly TileGrid _tileGrid;

    // Per-frame tile cache accounting. A Compositor instance is created per present
    // (NativeViewportRenderer / PageRendererHost build a fresh one each frame), so
    // these accumulate exactly one frame's tiles — the telemetry daemon reads the
    // miss ratio to spot whole-layer invalidation defeating the per-tile cache.
    private int _frameTileHits;
    private int _frameTileMisses;

    public Compositor(IPaintBackend backend, TileGrid? tileGrid = null)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
        _tileGrid = tileGrid ?? new TileGrid();
    }

    /// <summary>
    /// Test-only switch (LTF-05): when set, every layer takes the general
    /// inverse-mapped bilinear composite path even when it would qualify for the
    /// fast integer-aligned blit. Used to prove the two paths are byte-identical.
    /// </summary>
    internal bool DisableFastBlit { get; init; }

    internal bool DisableGpuBlend { get; init; }

    public RenderedBitmap Render(CompositorLayer root, LayoutRect viewport, float scale)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        using var composite = StarlingTelemetry.Span(RenderMetrics.PaintArea, RenderMetrics.CompositeOp);
        StarlingTelemetry.Gauge(RenderMetrics.CompositeOutputAllocBytes, (double)width * height * 4);
        var output = new byte[checked(width * height * 4)];
        FillWhite(output);

        var ops = new List<LayerBlend>();
        CollectOps(root, ops, viewport, scale,
            ancestorTransform: Matrix2D.Identity, ancestorOpacity: 1f, ancestorClip: null,
            textureCache: null);

        if (ops.Count > 0)
        {
            if (DisableGpuBlend)
            {
                foreach (var op in ops)
                {
                    if (op.IsBackdropFilter)
                    {
                        ApplyBackdropCpu(op, output, width, height);
                    }
                    else
                    {
                        BlendOp(op, output, width, height, fastBlit: !DisableFastBlit);
                    }
                }
            }
            else
            {
                var gpu = GpuLayerCompositor.Shared
                    ?? throw new InvalidOperationException("WebGPU composite blend is unavailable for the selected paint backend.");
                gpu.Composite(output, width, height, ops);
            }
        }

        EmitTileFrameMetrics();
        return new RenderedBitmap(width, height, output);
    }

    public RenderedBitmap RenderGpuTextures(
        CompositorLayer root,
        LayoutRect viewport,
        float scale,
        IReadOnlyList<SurfaceOverlayLayer>? drawingOverlays = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        var gpu = GpuLayerCompositor.Shared
            ?? throw new InvalidOperationException("WebGPU composite blend is unavailable for the selected paint backend.");

        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        using var composite = StarlingTelemetry.Span(RenderMetrics.PaintArea, RenderMetrics.CompositeOp);
        StarlingTelemetry.Gauge(RenderMetrics.CompositeOutputAllocBytes, (double)width * height * 4);
        var output = new byte[checked(width * height * 4)];
        FillWhite(output);

        var ops = new List<LayerBlend>();
        CollectOps(root, ops, viewport, scale,
            ancestorTransform: Matrix2D.Identity, ancestorOpacity: 1f, ancestorClip: null,
            textureCache: gpu);
        var gpuOverlayLayers = BuildGpuOverlayLayers(viewport, scale, drawingOverlays);

        if (ops.Count > 0 || gpuOverlayLayers is { Count: > 0 })
        {
            gpu.Composite(output, width, height, ops, gpuOverlayLayers);
        }

        EmitTileFrameMetrics();
        return new RenderedBitmap(width, height, output);
    }

    public bool RenderToSurface(
        CompositorLayer root,
        LayoutRect viewport,
        float scale,
        GpuSurfacePresenter presenter,
        IReadOnlyList<SurfaceOverlayLayer>? drawingOverlays = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(presenter);
        if (viewport.Width <= 0 || viewport.Height <= 0)
            throw new ArgumentException("Viewport must have positive dimensions.", nameof(viewport));
        if (!(scale > 0f))
            throw new ArgumentException("Scale must be positive.", nameof(scale));

        var width = (int)Math.Ceiling(viewport.Width * scale);
        var height = (int)Math.Ceiling(viewport.Height * scale);

        var ops = new List<LayerBlend>();
        var textureCache = new SurfaceTextureCache(presenter);
        CollectOps(root, ops, viewport, scale,
            ancestorTransform: Matrix2D.Identity, ancestorOpacity: 1f, ancestorClip: null,
            textureCache: textureCache);

        var gpuOverlayLayers = BuildGpuOverlayLayers(viewport, scale, drawingOverlays);

        var presented = presenter.PresentOps(width, height, ops, gpuOverlayLayers);
        if (!presented)
            throw new InvalidOperationException("GPU surface presenter did not present the frame.");

        EmitTileFrameMetrics();
        return presented;
    }


    private static List<GpuOverlayLayer>? BuildGpuOverlayLayers(
        LayoutRect viewport,
        float scale,
        IReadOnlyList<SurfaceOverlayLayer>? overlays)
    {
        if (overlays is not { Count: > 0 })
        {
            return null;
        }

        var s = (double)scale;
        var pageToDevice = Matrix2D.Translate(-viewport.X * s, -viewport.Y * s).Multiply(Matrix2D.Scale(s, s));
        List<GpuOverlayLayer>? layers = null;
        foreach (var overlay in overlays)
        {
            if (overlay.Scene.Width <= 0 ||
                overlay.Scene.Height <= 0 ||
                overlay.Opacity <= 0 ||
                overlay.Scene.Commands.Count == 0)
            {
                continue;
            }

            var sceneToDevice = pageToDevice.Multiply(overlay.SceneToPage);
            var clipDevice = overlay.ClipPage is { } clip
                ? new Rect(
                    (clip.X - viewport.X) * s,
                    (clip.Y - viewport.Y) * s,
                    clip.Width * s,
                    clip.Height * s)
                : (Rect?)null;
            (layers ??= []).Add(new GpuOverlayLayer(
                overlay.Scene,
                sceneToDevice,
                Math.Clamp(overlay.Opacity, 0f, 1f),
                clipDevice));
        }

        return layers;
    }

    /// <summary>
    /// Appends one layer tree's blend ops into a shared list for a surface frame
    /// that composites several documents (e.g. engine-rendered chrome above the
    /// page). Each op's device mapping is post-translated by
    /// (<paramref name="destOriginXDevice"/>, <paramref name="destOriginYDevice"/>)
    /// so the tree lands in a sub-region of the swapchain, and clipped to
    /// <paramref name="regionClipDevice"/> so it can't draw outside that region.
    /// The presenter blends the combined list in one render pass, one present.
    /// </summary>
    internal void AppendSurfaceOps(
        CompositorLayer root,
        LayoutRect viewport,
        float scale,
        double destOriginXDevice,
        double destOriginYDevice,
        Rect? regionClipDevice,
        List<LayerBlend> ops,
        GpuSurfacePresenter presenter)
    {
        var local = new List<LayerBlend>();
        var textureCache = new SurfaceTextureCache(presenter);
        CollectOps(root, local, viewport, scale,
            ancestorTransform: Matrix2D.Identity, ancestorOpacity: 1f, ancestorClip: null,
            textureCache: textureCache);

        var offset = Matrix2D.Translate(destOriginXDevice, destOriginYDevice);
        foreach (var op in local)
        {
            var mapped = offset.Multiply(op.LocalToDevice);
            // The op's own clip is already in device space; shift it into the
            // region and intersect with the region bounds.
            Rect? clip = op.ClipDevice is { } c
                ? IntersectDeviceRect(c.Translate(destOriginXDevice, destOriginYDevice), regionClipDevice)
                : regionClipDevice;
            ops.Add(op.WithGeometry(mapped, clip));
        }
    }

    private static Rect? IntersectDeviceRect(Rect a, Rect? b)
    {
        if (b is not { } r) return a;
        var x = Math.Max(a.X, r.X);
        var y = Math.Max(a.Y, r.Y);
        var right = Math.Min(a.Right, r.Right);
        var bottom = Math.Min(a.Bottom, r.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private void CollectOps(
        CompositorLayer layer,
        List<LayerBlend> ops,
        LayoutRect viewport,
        float scale,
        Matrix2D ancestorTransform,
        float ancestorOpacity,
        Rect? ancestorClip,
        IGpuLayerTextureCache? textureCache)
    {
        // Effective transform = ancestor × this layer's transform (post-multiply
        // so the ancestor's frame wraps the descendant, matching CSS Transforms
        // 1 §6.1 and the flat builder's nesting).
        var effectiveTransform = ancestorTransform.Multiply(layer.Transform);
        var effectiveOpacity = ancestorOpacity * layer.Opacity;

        // Clip is intersected in page space. The layer's own clip rect is in
        // page coords; ancestor clips were transformed into the same space as
        // they were intersected, so combine by plain rect intersection.
        var effectiveClip = IntersectClip(ancestorClip, layer.Clip);

        // Filter Effects 2 §6 — backdrop-filter. The op goes into the stream
        // BEFORE everything in this stacking context (including negative-z
        // child layers — they paint above the filtered backdrop), so the blend
        // path snapshots everything composited under the element, filters it,
        // and the element's content then paints over it and stays sharp.
        if (layer.BackdropFilters is { Count: > 0 } backdropFilters && effectiveOpacity > 0f)
        {
            EmitBackdropFilterOp(layer, backdropFilters, ops, viewport, scale,
                effectiveTransform, effectiveClip, textureCache);
        }

        // Children are already in paint order (sorted by z-index, then tree order,
        // at build time), so negative-z layers are the prefix. CSS-Position-3 §9:
        // they paint BEHIND this layer's own slice; non-negative layers paint over
        // it. Without this split a negative-z layer (e.g. a full-viewport `z-index:-1`
        // background overlay) would draw over the parent's in-flow text.
        var childIndex = 0;
        for (; childIndex < layer.Children.Count && layer.Children[childIndex].ZIndex < 0; childIndex++)
        {
            CollectOps(layer.Children[childIndex], ops, viewport, scale,
                effectiveTransform, effectiveOpacity, effectiveClip, textureCache);
        }

        if (layer.Bounds.Width > 0 && layer.Bounds.Height > 0 && effectiveOpacity > 0f)
        {
            // A layer-root `filter` chain rides the layer (LTF; see
            // CompositorLayer.Filters) and is applied ONCE to the whole rastered
            // layer here — not re-run inside every tile raster it overlaps.
            if (layer.Filters is { Count: > 0 } filters)
            {
                EmitFilteredLayer(layer, filters, ops, viewport, scale,
                    effectiveTransform, effectiveOpacity, effectiveClip, textureCache);
            }
            else
            {
                EmitLayerTiles(layer, ops, viewport, scale,
                    effectiveTransform, effectiveOpacity, effectiveClip, textureCache);
            }
        }

        for (; childIndex < layer.Children.Count; childIndex++)
        {
            CollectOps(layer.Children[childIndex], ops, viewport, scale,
                effectiveTransform, effectiveOpacity, effectiveClip, textureCache);
        }
    }

    /// <summary>
    /// Emits one <see cref="LayerBlend"/> per <em>visible</em> tile of the layer
    /// (wp:M12-05-tile-grid). The layer's slice is tiled into a grid of
    /// <see cref="TileGrid.TileWidthDevice"/>×<see cref="TileGrid.TileHeightDevice"/>
    /// device-px tiles; only tiles intersecting the output viewport (plus a 1-tile
    /// overdraw ring) are rastered. Each tile's device origin is an integer multiple
    /// of the tile size from the layer origin, so a tile rasters the same device-pixel
    /// grid the single full-layer bitmap would — the tiles' union is byte-identical to
    /// it for an upright layer (integer translation → <see cref="BlitIntegerAligned"/>),
    /// and SSIM-identical under a transform. Tiles are cached across frames in the
    /// session <see cref="TileGrid"/> keyed by (layer id, col, row, scale); a scroll
    /// re-blits cached tiles and only rasters the newly-exposed row/column. No tile
    /// ever exceeds the tile size, so the wgpu max-texture-dimension bug is impossible.
    /// </summary>
    private void EmitLayerTiles(
        CompositorLayer layer, List<LayerBlend> ops,
        LayoutRect viewport, float scale, Matrix2D effectiveTransform,
        float effectiveOpacity, Rect? effectiveClip, IGpuLayerTextureCache? textureCache)
    {
        var s = (double)scale;
        var bounds = layer.Bounds;
        // The layer's full extent in device px — the size the single-bitmap path used.
        var layerDevW = Math.Max(1, (int)Math.Ceiling(bounds.Width * s));
        var layerDevH = Math.Max(1, (int)Math.Ceiling(bounds.Height * s));

        var outW = (int)Math.Ceiling(viewport.Width * s);
        var outH = (int)Math.Ceiling(viewport.Height * s);

        var pageToDevice = Matrix2D.Translate(-viewport.X * s, -viewport.Y * s).Multiply(Matrix2D.Scale(s, s));
        // layer-local device px (0..layerDevW, 0..layerDevH) -> output device px.
        var layerLocalToDevice = pageToDevice.Multiply(effectiveTransform)
            .Multiply(Matrix2D.Translate(bounds.X, bounds.Y).Multiply(Matrix2D.Scale(1d / s, 1d / s)));

        // Device AABB of the page-space clip — applied to every tile (same as the
        // single-bitmap path applied one clip to the whole layer).
        Rect? clipDev = effectiveClip is { } cp
            ? TransformedAabb(new Rect(cp.X, cp.Y, cp.Width, cp.Height), pageToDevice)
            : null;

        const int TW = TileGrid.TileWidthDevice;
        const int TH = TileGrid.TileHeightDevice;
        var maxCol = (layerDevW - 1) / TW;
        var maxRow = (layerDevH - 1) / TH;

        // Default to the whole layer; viewport-cull only a LARGE, untransformed layer
        // (the page root is the case that matters — a long page must not raster its
        // full height). The inverse-transform cull is exact for an identity transform;
        // a transformed layer (an animating element) is small and cheap, so render all
        // its tiles rather than risk the fragile rotated-AABB cull dropping it.
        int col0 = 0, col1 = maxCol, row0 = 0, row1 = maxRow;
        var manyTiles = (long)(maxCol + 1) * (maxRow + 1) > MaxUnculledTiles;
        if (manyTiles && effectiveTransform.IsIdentity
            && !TryVisibleTileRange(layerLocalToDevice, outW, outH, layerDevW, layerDevH,
                out col0, out col1, out row0, out row1))
            return; // layer entirely outside the viewport

        // Pre-walk the slice once so each tile can be keyed by the content that
        // actually paints into IT (not the whole layer). This is the fix for the
        // whole-layer-invalidation lag: a localized change (one text node, a hover
        // colour) only changes the hash of the tiles it overlaps, so every other
        // visible tile still serves from cache.
        var prepared = _tileGrid.GetOrPrepare(layer.ContentHash, layer.Items);

        for (var row = row0; row <= row1; row++)
        {
            var th = Math.Min(TH, layerDevH - row * TH);
            if (th <= 0)
            {
                continue;
            }

            for (var col = col0; col <= col1; col++)
            {
                var tw = Math.Min(TW, layerDevW - col * TW);
                if (tw <= 0)
                {
                    continue;
                }

                // Tile page origin is the layer origin plus an integer device offset
                // (col*TW, row*TH) converted to CSS — keeps the tile on the layer's
                // device-pixel grid for seam-free, byte-identical abutment.
                var tilePageX = bounds.X + col * TW / s;
                var tilePageY = bounds.Y + row * TH / s;

                var tileRectPage = new LayoutRect(tilePageX, tilePageY, tw / s, th / s);
                // Freshness key is this tile's own content hash — only the items
                // that paint into this tile (plus bracket items) — so an unchanged
                // tile stays cached even when another tile's content changed.
                var tileHash = prepared.HashForTile(tileRectPage);

                var key = new TileKey(layer.LayerId, col, row, scale);
                var opHash = TileOpHash(tileHash, col, row);
                var tileLocalToDevice = pageToDevice.Multiply(effectiveTransform)
                    .Multiply(Matrix2D.Translate(tilePageX, tilePageY).Multiply(Matrix2D.Scale(1d / s, 1d / s)));

                if (textureCache is not null)
                {
                    EmitGpuTextureTile(ops, layer.Items, tileRectPage, scale, key, tileHash, opHash,
                        tileLocalToDevice, effectiveOpacity, clipDev, textureCache);
                    continue;
                }

                if (_tileGrid.TryGetTile(key, tileHash, out var tileBmp))
                {
                    _frameTileHits++;
                }
                else
                {
                    _frameTileMisses++;
                    tileBmp = _backend.Render(layer.Items, tileRectPage, scale, opaqueBackground: false);
                    _tileGrid.PutTile(key, tileHash, tileBmp);
                }

                // GPU texture key stays position-mixed so two distinct tiles never
                // alias one texture; seeded by the per-tile hash so a tile's texture
                // re-uploads only when ITS content changes.
                ops.Add(LayerBlend.Bitmap(tileBmp, opHash,
                    tileLocalToDevice, effectiveOpacity, clipDev));
            }
        }
    }

    private void EmitGpuTextureTile(
        List<LayerBlend> ops,
        DisplayList.DisplayList items,
        LayoutRect tileRectPage,
        float scale,
        TileKey key,
        long tileHash,
        long opHash,
        Matrix2D tileLocalToDevice,
        float effectiveOpacity,
        Rect? clipDev,
        IGpuLayerTextureCache textureCache)
    {
        if (_tileGrid.TryGetResidentTile(key, tileHash, out var resident)
            && textureCache.HasResidentTexture(opHash, resident.Width, resident.Height))
        {
            _frameTileHits++;
            ops.Add(LayerBlend.ResidentTexture(
                resident.Width,
                resident.Height,
                opHash,
                tileLocalToDevice,
                effectiveOpacity,
                clipDev));
            return;
        }

        _frameTileMisses++;
        if (_backend is not IGpuTexturePaintBackend gpuBackend)
        {
            throw new InvalidOperationException("GPU surface rendering requires a GPU texture paint backend.");
        }

        GpuPaintTexture? texture = null;
        try
        {
            texture = gpuBackend.RenderTexture(
                items,
                tileRectPage,
                scale,
                opaqueBackground: false,
                textureCache.GpuDevice);

            textureCache.AdoptTexture(opHash, texture);
            _tileGrid.PutResidentTile(key, tileHash, texture.Width, texture.Height);
            ops.Add(LayerBlend.ResidentTexture(
                texture.Width,
                texture.Height,
                opHash,
                tileLocalToDevice,
                effectiveOpacity,
                clipDev));
            texture = null;
        }
        finally
        {
            texture?.Dispose();
        }
    }

    /// <summary>
    /// Emits a layer whose root carries a CSS <c>filter</c> chain
    /// (<see cref="CompositorLayer.Filters"/>). The slice is rastered ONCE into a
    /// halo-padded whole-layer surface, the chain runs once over it (on the GPU
    /// when the texture cache supports it, else through the backend's CPU chain),
    /// and the result is cached and blended as a single quad — instead of
    /// re-running the chain inside every tile raster the group overlaps.
    /// Oversized surfaces and chains the GPU path can't run fall back to the
    /// legacy per-tile path by re-wrapping the slice in its PushFilter bracket.
    /// </summary>
    private void EmitFilteredLayer(
        CompositorLayer layer, IReadOnlyList<DisplayList.FilterFunction> filters, List<LayerBlend> ops,
        LayoutRect viewport, float scale, Matrix2D effectiveTransform,
        float effectiveOpacity, Rect? effectiveClip, IGpuLayerTextureCache? textureCache)
    {
        var s = (double)scale;
        var pad = DisplayList.FilterFunction.HaloPadding(filters);
        var bounds = layer.Bounds;
        var padded = new LayoutRect(bounds.X - pad, bounds.Y - pad, bounds.Width + 2 * pad, bounds.Height + 2 * pad);
        var devW = Math.Max(1, (int)Math.Ceiling(padded.Width * s));
        var devH = Math.Max(1, (int)Math.Ceiling(padded.Height * s));

        // Same surface guard as the legacy offscreen group path, plus the wgpu
        // texture-dimension cap; an unsupported chain on the GPU path also falls
        // back (checked BEFORE rastering so nothing is wasted).
        var oversized = (long)devW * devH > 64L * 1024 * 1024 || devW > MaxFilterSurfaceDimension || devH > MaxFilterSurfaceDimension;
        if (oversized || (textureCache is not null && !textureCache.SupportsLayerFilters(filters)))
        {
            EmitLayerTiles(WrapInFilterBracket(layer, filters, padded), ops, viewport, scale,
                effectiveTransform, effectiveOpacity, effectiveClip, textureCache);
            return;
        }

        // Viewport cull on the padded, transformed extent — the single-surface
        // equivalent of the per-tile cull. The halo is part of the extent, so a
        // layer whose blur bleeds into view still paints.
        var pageToDevice = Matrix2D.Translate(-viewport.X * s, -viewport.Y * s).Multiply(Matrix2D.Scale(s, s));
        var pageTransform = pageToDevice.Multiply(effectiveTransform);
        var devAabb = TransformedAabb(new Rect(padded.X, padded.Y, padded.Width, padded.Height), pageTransform);
        var outW = (int)Math.Ceiling(viewport.Width * s);
        var outH = (int)Math.Ceiling(viewport.Height * s);
        if (devAabb.Right < 0 || devAabb.Bottom < 0 || devAabb.X > outW || devAabb.Y > outH)
            return;

        Rect? clipDev = effectiveClip is { } cp
            ? TransformedAabb(new Rect(cp.X, cp.Y, cp.Width, cp.Height), pageToDevice)
            : null;

        // Whole-surface cache key: column/row -1 mark the filtered single-surface
        // entry so it can never collide with a real (col, row) tile of this layer.
        // A layer without an element id (a filtered ::before glow) keys by its
        // content hash instead, so two such layers never thrash one slot —
        // identical content+chain sharing a surface is correct, not a collision.
        // ContentHash already folds the filter chain; the surface size folds in so
        // a zoom that changes the padded extent re-keys the cached result.
        var key = new TileKey(layer.LayerId != 0 ? layer.LayerId : layer.ContentHash, -1, -1, scale);
        var opHash = FilteredOpHash(layer.ContentHash, devW, devH);

        if (textureCache is not null)
        {
            if (_tileGrid.TryGetResidentTile(key, opHash, out var resident)
                && textureCache.HasResidentTexture(opHash, resident.Width, resident.Height))
            {
                _frameTileHits++;
                ops.Add(LayerBlend.ResidentTexture(resident.Width, resident.Height, opHash,
                    FilteredLocalToDevice(pageTransform, padded, resident.Width, resident.Height),
                    effectiveOpacity, clipDev));
                return;
            }

            _frameTileMisses++;
            if (_backend is not IGpuTexturePaintBackend gpuBackend)
            {
                throw new InvalidOperationException("GPU surface rendering requires a GPU texture paint backend.");
            }

            var source = gpuBackend.RenderTexture(layer.Items, padded, scale, opaqueBackground: false, textureCache.GpuDevice);
            // ApplyLayerFilters consumes the source either way; a null result means
            // the chain unexpectedly failed at run time — re-raster the legacy way
            // rather than present unfiltered content.
            var filtered = textureCache.ApplyLayerFilters(source, filters, scale);
            if (filtered is null)
            {
                EmitLayerTiles(WrapInFilterBracket(layer, filters, padded), ops, viewport, scale,
                    effectiveTransform, effectiveOpacity, effectiveClip, textureCache);
                return;
            }

            textureCache.AdoptTexture(opHash, filtered);
            _tileGrid.PutResidentTile(key, opHash, filtered.Width, filtered.Height);
            ops.Add(LayerBlend.ResidentTexture(filtered.Width, filtered.Height, opHash,
                FilteredLocalToDevice(pageTransform, padded, filtered.Width, filtered.Height),
                effectiveOpacity, clipDev));
            return;
        }

        if (_tileGrid.TryGetTile(key, opHash, out var bmp))
        {
            _frameTileHits++;
        }
        else
        {
            _frameTileMisses++;
            bmp = _backend.RenderFiltered(layer.Items, padded, scale, filters);
            _tileGrid.PutTile(key, opHash, bmp);
        }

        ops.Add(LayerBlend.Bitmap(bmp, opHash,
            FilteredLocalToDevice(pageTransform, padded, bmp.Width, bmp.Height),
            effectiveOpacity, clipDev));
    }

    /// <summary>
    /// Emits the layer's `backdrop-filter` op (Filter Effects 2 §6) into the
    /// blend stream: the element's border box under the effective transform,
    /// padded by the chain's blur halo so the Gaussian has real neighbours at
    /// the visible edge, clamped to the output — built as a pure integer device
    /// translation so the blend paths can snapshot exactly that region. The op
    /// is scissored back to the UNPADDED element rect (rounded corners are a v1
    /// limitation — the clip is rectangular). The backdrop re-filters every
    /// frame; nothing is cached, because the op cannot know when the content
    /// composited under it changed.
    /// </summary>
    private static void EmitBackdropFilterOp(
        CompositorLayer layer, IReadOnlyList<DisplayList.FilterFunction> filters, List<LayerBlend> ops,
        LayoutRect viewport, float scale, Matrix2D effectiveTransform, Rect? effectiveClip,
        IGpuLayerTextureCache? textureCache)
    {
        // The GPU paths run the chain through the shared filter engine; a chain
        // it can't run (drop-shadow) keeps today's behavior — no backdrop
        // effect — rather than a wrong one. The CPU blend path supports every
        // chain via the backend.
        if (textureCache is not null && !textureCache.SupportsLayerFilters(filters))
            return;

        var bounds = layer.BackdropBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var s = (double)scale;
        var pageToDevice = Matrix2D.Translate(-viewport.X * s, -viewport.Y * s).Multiply(Matrix2D.Scale(s, s));
        var pageTransform = pageToDevice.Multiply(effectiveTransform);
        // Device AABB of the element's border box. A rotated element filters
        // the AABB of its box — a v1 approximation, same as the clip handling.
        var devRect = TransformedAabb(new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), pageTransform);

        var outW = (int)Math.Ceiling(viewport.Width * s);
        var outH = (int)Math.Ceiling(viewport.Height * s);
        var pad = DisplayList.FilterFunction.HaloPadding(filters) * s;
        var x0 = Math.Max(0, (int)Math.Floor(devRect.X - pad));
        var y0 = Math.Max(0, (int)Math.Floor(devRect.Y - pad));
        var x1 = Math.Min(outW, (int)Math.Ceiling(devRect.Right + pad));
        var y1 = Math.Min(outH, (int)Math.Ceiling(devRect.Bottom + pad));
        if (x1 <= x0 || y1 <= y0)
            return;

        // The filtered patch paints back clipped to the element rect (the halo
        // margin feeds the blur but never paints), intersected with the
        // inherited clip.
        Rect? clipDev = devRect;
        if (effectiveClip is { } cp)
            clipDev = IntersectDeviceRect(devRect, TransformedAabb(new Rect(cp.X, cp.Y, cp.Width, cp.Height), pageToDevice));
        if (clipDev is { Width: <= 0 } or { Height: <= 0 })
            return;

        var layerKey = layer.LayerId != 0 ? layer.LayerId : layer.ContentHash;
        ops.Add(LayerBlend.BackdropFilter(x1 - x0, y1 - y0,
            BackdropOpHash(layerKey, x0, y0),
            Matrix2D.Translate(x0, y0), clipDev, filters, scale));
    }

    // Op key for a backdrop's filtered-snapshot texture. Mixes the region
    // origin so two identical-content backdrop layers never alias one texture;
    // the tag keeps it clear of tile, filtered-surface, and overlay-swatch keys.
    private static long BackdropOpHash(long layerKey, int x, int y)
        => unchecked(((layerKey ^ 0x4241434B44524F50L) * 1099511628211L) ^ (((long)x << 21) | (uint)y));

    /// <summary>
    /// CPU-blend twin of the GPU backdrop segmentation: snapshots the op's
    /// region of the composited output, runs the chain over it via the backend,
    /// and alpha-blends the result back clipped to the element rect. Runs only
    /// on the test-only <see cref="DisableGpuBlend"/> path.
    /// </summary>
    private void ApplyBackdropCpu(in LayerBlend op, byte[] output, int outWidth, int outHeight)
    {
        if (op.BackdropFilters is not { } filters)
            return;
        if (!IsIntegerTranslation(op.LocalToDevice, out var dx, out var dy))
            return; // built as a pure translation; anything else is unexpected

        var x0 = Math.Max(0, dx);
        var y0 = Math.Max(0, dy);
        var x1 = Math.Min(outWidth, dx + op.Width);
        var y1 = Math.Min(outHeight, dy + op.Height);
        if (x1 <= x0 || y1 <= y0)
            return;

        var w = x1 - x0;
        var h = y1 - y0;
        var patch = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Array.Copy(output, (((y0 + row) * outWidth) + x0) * 4, patch, row * w * 4, w * 4);

        var filtered = _backend.FilterBitmap(new RenderedBitmap(w, h, patch), filters, op.FilterScale);
        try
        {
            // The filtered result may be a different size (CPU drop-shadow
            // rebuilds); map its texel space back onto the region rect and let
            // the general blend sample it, scissored by the op's clip.
            var draw = LayerBlend.Bitmap(filtered, op.ContentHash,
                Matrix2D.Translate(x0, y0).Multiply(
                    Matrix2D.Scale((double)w / Math.Max(1, filtered.Width), (double)h / Math.Max(1, filtered.Height))),
                op.Opacity, op.ClipDevice);
            BlendOp(draw, output, outWidth, outHeight, fastBlit: false);
        }
        finally
        {
            filtered.Dispose();
        }
    }

    // WebGPU's guaranteed minimum maxTextureDimension2D; a filtered surface past
    // it must tile (the legacy bracket path) instead of failing texture creation.
    private const int MaxFilterSurfaceDimension = 8192;

    /// <summary>
    /// Maps a filtered surface's texel space (0..texW, 0..texH) onto output
    /// device px. Unlike a tile (always 1 texel = 1/scale page px), the filtered
    /// result may be smaller than its padded page rect (large blurs render at
    /// reduced resolution), so the scale is derived from the rect/texture ratio
    /// and the blend quad's linear sampling upscales.
    /// </summary>
    private static Matrix2D FilteredLocalToDevice(Matrix2D pageTransform, LayoutRect padded, int texW, int texH)
        => pageTransform.Multiply(
            Matrix2D.Translate(padded.X, padded.Y)
                .Multiply(Matrix2D.Scale(padded.Width / Math.Max(1, texW), padded.Height / Math.Max(1, texH))));

    // Whole-surface op key for a filtered layer. The tag keeps it out of every
    // TileOpHash (which mixes non-negative col/row) and the overlay-swatch range.
    private static long FilteredOpHash(long layerContentHash, int devW, int devH)
        => unchecked(((layerContentHash ^ (long)0x46494C5445520000) * 1099511628211L)
                     ^ (((long)devW << 21) | (uint)devH));

    /// <summary>
    /// Legacy fallback: re-wraps a filtered layer's slice in its PushFilter
    /// bracket — the form the per-tile raster path applies inline — with bounds
    /// padded for the blur halo so the tiles cover the bleed. Keeps oversized
    /// surfaces and GPU-unsupported chains rendering exactly as before.
    /// </summary>
    private static CompositorLayer WrapInFilterBracket(
        CompositorLayer layer, IReadOnlyList<DisplayList.FilterFunction> filters, LayoutRect paddedBounds)
    {
        var items = new DisplayList.DisplayList();
        // The bracket seeds the group-bounds union with the element's border box
        // when the source box is known (what Visit used to emit), else the
        // painted union — either is a valid seed, the union only grows.
        var bracketBounds = layer.SourceBox is { } src
            ? new Rect(layer.OriginParentX + src.Frame.X, layer.OriginParentY + src.Frame.Y, src.Frame.Width, src.Frame.Height)
            : layer.Bounds;
        items.Add(new DisplayList.PushFilter(bracketBounds, filters));
        var slice = layer.Items.Items;
        for (var i = 0; i < slice.Count; i++)
            items.Add(slice[i]);
        items.Add(DisplayList.PopFilter.Instance);
        return new CompositorLayer(items, paddedBounds, layer.Transform, layer.Opacity, layer.Clip,
            [], layer.ContentHash, layer.LayerId,
            layer.SourceBox, layer.OriginParentX, layer.OriginParentY, layer.ZIndex, layer.InheritedClip);
    }

    private readonly struct SurfaceTextureCache(GpuSurfacePresenter presenter) : IGpuLayerTextureCache
    {
        public bool HasResidentTexture(long contentHash, int width, int height)
            => presenter.HasResidentTexture(contentHash, width, height);

        public GpuPaintDevice GpuDevice => presenter.GpuDevice;

        public void AdoptTexture(long contentHash, GpuPaintTexture texture)
            => presenter.AdoptTexture(contentHash, texture);

        public bool SupportsLayerFilters(IReadOnlyList<DisplayList.FilterFunction> filters)
            => GpuSurfacePresenter.SupportsLayerFilters(filters);

        public GpuPaintTexture? ApplyLayerFilters(GpuPaintTexture source,
            IReadOnlyList<DisplayList.FilterFunction> filters, float scale)
            => presenter.ApplyLayerFilters(source, filters, scale);
    }

    /// <summary>
    /// Flushes the per-frame tile metrics for the multi-document surface path
    /// (<see cref="AppendSurfaceOps"/> ×N then a single present), which has no
    /// single Render/RenderToSurface exit to hook. The host calls this once after
    /// the last AppendSurfaceOps; a Compositor is one frame, so the accumulated
    /// hit/miss tally is exactly this frame's.
    /// </summary>
    internal void FlushTileFrameMetrics() => EmitTileFrameMetrics();

    // Emits the per-frame tile cache signals once the frame's ops are collected:
    // the miss ratio (≈1.0 while only a small region changed is the smoking gun for
    // whole-layer invalidation) and how many tiles this frame had to raster.
    private void EmitTileFrameMetrics()
    {
        var total = _frameTileHits + _frameTileMisses;
        if (total == 0) return;
        StarlingTelemetry.Gauge(RenderMetrics.TileMissRatio, (double)_frameTileMisses / total);
        StarlingTelemetry.Counter(RenderMetrics.TileRastersPerFrame, _frameTileMisses);
    }

    // A layer spanning this many tiles or fewer renders all of them (no viewport cull) —
    // small/transformed layers are cheap and culling them is fragile under rotation.
    private const int MaxUnculledTiles = 4;

    // Per-tile op key for the GPU texture cache: unique per (tile position, content),
    // so each tile uploads one texture and re-uploads when the layer content changes.
    // FNV-style mix; stays out of the 0xC0FFEE… overlay-swatch range.
    private static long TileOpHash(long layerContentHash, int col, int row)
        => unchecked((layerContentHash * 1099511628211L) ^ (((long)col << 21) | (uint)row));

    /// <summary>
    /// Computes the inclusive tile column/row range of <paramref name="layerLocalToDevice"/>
    /// whose tiles intersect the output rect (<paramref name="outW"/>×<paramref name="outH"/>),
    /// expanded by a 1-tile overdraw ring and clamped to the layer's grid. Returns false
    /// when the transform is degenerate or the layer is wholly outside the viewport.
    /// </summary>
    private static bool TryVisibleTileRange(
        Matrix2D layerLocalToDevice, int outW, int outH, int layerDevW, int layerDevH,
        out int col0, out int col1, out int row0, out int row1)
    {
        col0 = col1 = row0 = row1 = 0;
        if (!TryInvert(layerLocalToDevice, out var inv))
            return false;

        // Inverse-map the output rect corners into layer-local device space, take AABB.
        var (ax, ay) = inv.Transform(0, 0);
        var (bx, by) = inv.Transform(outW, 0);
        var (cx, cy) = inv.Transform(outW, outH);
        var (dx, dy) = inv.Transform(0, outH);
        var minX = Math.Max(0d, Math.Min(Math.Min(ax, bx), Math.Min(cx, dx)));
        var minY = Math.Max(0d, Math.Min(Math.Min(ay, by), Math.Min(cy, dy)));
        var maxX = Math.Min((double)layerDevW, Math.Max(Math.Max(ax, bx), Math.Max(cx, dx)));
        var maxY = Math.Min((double)layerDevH, Math.Max(Math.Max(ay, by), Math.Max(cy, dy)));
        if (maxX <= minX || maxY <= minY) return false;

        const int TW = TileGrid.TileWidthDevice;
        const int TH = TileGrid.TileHeightDevice;
        var maxCol = (layerDevW - 1) / TW;
        var maxRow = (layerDevH - 1) / TH;
        col0 = Math.Clamp((int)Math.Floor(minX / TW) - 1, 0, maxCol);
        col1 = Math.Clamp((int)Math.Floor((maxX - 1e-3) / TW) + 1, 0, maxCol);
        row0 = Math.Clamp((int)Math.Floor(minY / TH) - 1, 0, maxRow);
        row1 = Math.Clamp((int)Math.Floor((maxY - 1e-3) / TH) + 1, 0, maxRow);
        return col1 >= col0 && row1 >= row0;
    }

    /// <summary>
    /// Managed alpha-over blend of one <see cref="LayerBlend"/> into
    /// <paramref name="output"/> — the CPU blend path.
    /// Inverse-maps each output device pixel back to a source sample using the
    /// op's precomputed <see cref="LayerBlend.LocalToDevice"/> so rotation /
    /// scaling are exact regardless of the transform.
    /// </summary>
    private static void BlendOp(LayerBlend op, byte[] output, int outWidth, int outHeight, bool fastBlit)
    {
        var local = op.RequireLocalPixels();
        var localToDevice = op.LocalToDevice;
        var opacity = op.Opacity;
        var clipDev = op.ClipDevice;

        if (!TryInvert(localToDevice, out var deviceToLocal))
            return; // Degenerate (scale 0 / collapsed) transform paints nothing.

        // Device-space AABB of the transformed local bitmap, clamped to output.
        var srcRect = new Rect(0, 0, local.Width, local.Height);
        var devAabb = TransformedAabb(srcRect, localToDevice);

        // Fast path (LTF-05): a layer that lands as a pure integer-pixel
        // translation at full opacity (no rotation/scale/skew) skips the matrix
        // inverse + bilinear sample and blits source rows directly. The 1/scale
        // and scale factors cancel for an upright layer, so localToDevice's linear
        // part is exactly identity here and an integer-translation check suffices.
        // Byte-identical to the general path: a bilinear sample at integer
        // alignment returns the exact source pixel, and the same AlphaOver runs.
        if (fastBlit && opacity >= 1f && IsIntegerTranslation(localToDevice, out var tx, out var ty))
        {
            BlitIntegerAligned(local, output, outWidth, outHeight, tx, ty, clipDev);
            return;
        }

        var minX = Math.Max(0, (int)Math.Floor(devAabb.X));
        var minY = Math.Max(0, (int)Math.Floor(devAabb.Y));
        var maxX = Math.Min(outWidth, (int)Math.Ceiling(devAabb.Right));
        var maxY = Math.Min(outHeight, (int)Math.Ceiling(devAabb.Bottom));
        if (clipDev is { } cd)
        {
            minX = Math.Max(minX, (int)Math.Floor(cd.X));
            minY = Math.Max(minY, (int)Math.Floor(cd.Y));
            maxX = Math.Min(maxX, (int)Math.Ceiling(cd.Right));
            maxY = Math.Min(maxY, (int)Math.Ceiling(cd.Bottom));
        }

        var srcStride = local.Width * 4;
        var src = local.Rgba;
        var dstStride = outWidth * 4;

        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                // Sample at the pixel centre for stable mapping.
                var (sx, sy) = deviceToLocal.Transform(x + 0.5, y + 0.5);
                if (sx < 0 || sy < 0 || sx >= local.Width || sy >= local.Height) continue;

                Sample(src, srcStride, local.Width, local.Height, sx, sy, out var sr, out var sg, out var sb, out var sa);
                if (sa == 0) continue;

                var a = sa * opacity;
                if (a <= 0f) continue;

                var di = (y * dstStride) + (x * 4);
                AlphaOver(output, di, sr, sg, sb, a);
            }
        }
    }

    /// <summary>Bilinear sample of straight-alpha RGBA at fractional (sx, sy).</summary>
    private static void Sample(byte[] src, int stride, int w, int h, double sx, double sy,
        out float r, out float g, out float b, out float a)
    {
        var x0 = (int)Math.Floor(sx - 0.5);
        var y0 = (int)Math.Floor(sy - 0.5);
        var fx = (float)(sx - 0.5 - x0);
        var fy = (float)(sy - 0.5 - y0);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        x0 = Math.Clamp(x0, 0, w - 1);
        x1 = Math.Clamp(x1, 0, w - 1);
        y0 = Math.Clamp(y0, 0, h - 1);
        y1 = Math.Clamp(y1, 0, h - 1);

        // Premultiply before interpolation so a transparent neighbour doesn't
        // leak colour into the edge.
        Premul(src, (y0 * stride) + x0 * 4, out var r00, out var g00, out var b00, out var a00);
        Premul(src, (y0 * stride) + x1 * 4, out var r10, out var g10, out var b10, out var a10);
        Premul(src, (y1 * stride) + x0 * 4, out var r01, out var g01, out var b01, out var a01);
        Premul(src, (y1 * stride) + x1 * 4, out var r11, out var g11, out var b11, out var a11);

        var w00 = (1 - fx) * (1 - fy);
        var w10 = fx * (1 - fy);
        var w01 = (1 - fx) * fy;
        var w11 = fx * fy;

        var pr = r00 * w00 + r10 * w10 + r01 * w01 + r11 * w11;
        var pg = g00 * w00 + g10 * w10 + g01 * w01 + g11 * w11;
        var pb = b00 * w00 + b10 * w10 + b01 * w01 + b11 * w11;
        var pa = a00 * w00 + a10 * w10 + a01 * w01 + a11 * w11;

        a = pa;
        if (pa > 0f)
        {
            r = pr / pa;
            g = pg / pa;
            b = pb / pa;
        }
        else
        {
            r = g = b = 0f;
        }
    }

    private static void Premul(byte[] src, int i, out float r, out float g, out float b, out float a)
    {
        a = src[i + 3] / 255f;
        r = src[i] / 255f * a;
        g = src[i + 1] / 255f * a;
        b = src[i + 2] / 255f * a;
    }

    /// <summary>Straight-alpha source over straight-alpha destination, in place.</summary>
    private static void AlphaOver(byte[] dst, int i, float sr, float sg, float sb, float sa)
    {
        var da = dst[i + 3] / 255f;
        var outA = sa + da * (1 - sa);
        if (outA <= 0f)
        {
            dst[i] = dst[i + 1] = dst[i + 2] = dst[i + 3] = 0;
            return;
        }
        var dr = dst[i] / 255f;
        var dg = dst[i + 1] / 255f;
        var db = dst[i + 2] / 255f;
        var or = (sr * sa + dr * da * (1 - sa)) / outA;
        var og = (sg * sa + dg * da * (1 - sa)) / outA;
        var ob = (sb * sa + db * da * (1 - sa)) / outA;
        dst[i] = ToByte(or);
        dst[i + 1] = ToByte(og);
        dst[i + 2] = ToByte(ob);
        dst[i + 3] = ToByte(outA);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    private static void FillWhite(byte[] buf)
    {
        for (var i = 0; i < buf.Length; i++)
            buf[i] = 255;
    }

    private static Rect? IntersectClip(Rect? a, Rect? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        var x = Math.Max(a.Value.X, b.Value.X);
        var y = Math.Max(a.Value.Y, b.Value.Y);
        var right = Math.Min(a.Value.Right, b.Value.Right);
        var bottom = Math.Min(a.Value.Bottom, b.Value.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    private static Rect TransformedAabb(Rect r, Matrix2D m)
    {
        var (x0, y0) = m.Transform(r.X, r.Y);
        var (x1, y1) = m.Transform(r.X + r.Width, r.Y);
        var (x2, y2) = m.Transform(r.X + r.Width, r.Y + r.Height);
        var (x3, y3) = m.Transform(r.X, r.Y + r.Height);
        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// True when <paramref name="m"/> is a pure integer-pixel translation (identity
    /// linear part, integer offsets), the precondition for the LTF-05 fast blit.
    /// </summary>
    private static bool IsIntegerTranslation(Matrix2D m, out int dx, out int dy)
    {
        const double eps = 1e-6;
        dx = 0; dy = 0;
        if (Math.Abs(m.A - 1d) > eps || Math.Abs(m.D - 1d) > eps
            || Math.Abs(m.B) > eps || Math.Abs(m.C) > eps)
            return false;
        var rx = Math.Round(m.E);
        var ry = Math.Round(m.F);
        if (Math.Abs(m.E - rx) > eps || Math.Abs(m.F - ry) > eps)
            return false;
        dx = (int)rx;
        dy = (int)ry;
        return true;
    }

    /// <summary>
    /// Blits <paramref name="local"/> into <paramref name="output"/> offset by the
    /// integer device translation (<paramref name="dx"/>, <paramref name="dy"/>),
    /// clamped to the output and to <paramref name="clipDev"/>. Opaque source
    /// pixels copy straight through; partially-transparent pixels alpha-over —
    /// the same blend the general path applies, so the result is byte-identical.
    /// </summary>
    private static void BlitIntegerAligned(RenderedBitmap local, byte[] output, int outWidth, int outHeight, int dx, int dy, Rect? clipDev)
    {
        var minX = Math.Max(0, dx);
        var minY = Math.Max(0, dy);
        var maxX = Math.Min(outWidth, dx + local.Width);
        var maxY = Math.Min(outHeight, dy + local.Height);
        if (clipDev is { } cd)
        {
            minX = Math.Max(minX, (int)Math.Floor(cd.X));
            minY = Math.Max(minY, (int)Math.Floor(cd.Y));
            maxX = Math.Min(maxX, (int)Math.Ceiling(cd.Right));
            maxY = Math.Min(maxY, (int)Math.Ceiling(cd.Bottom));
        }
        if (maxX <= minX || maxY <= minY) return;

        var src = local.Rgba;
        var srcStride = local.Width * 4;
        var dstStride = outWidth * 4;
        for (var y = minY; y < maxY; y++)
        {
            var srcRow = (y - dy) * srcStride;
            var dstRow = y * dstStride;
            for (var x = minX; x < maxX; x++)
            {
                var si = srcRow + (x - dx) * 4;
                var sa = src[si + 3];
                if (sa == 0) continue;
                var di = dstRow + (x * 4);
                if (sa == 255)
                {
                    output[di] = src[si];
                    output[di + 1] = src[si + 1];
                    output[di + 2] = src[si + 2];
                    output[di + 3] = 255;
                }
                else
                {
                    AlphaOver(output, di, src[si] / 255f, src[si + 1] / 255f, src[si + 2] / 255f, sa / 255f);
                }
            }
        }
    }

    /// <summary>Inverts a 2D affine <see cref="Matrix2D"/>; false if singular.</summary>
    private static bool TryInvert(Matrix2D m, out Matrix2D inverse)
    {
        var det = m.A * m.D - m.B * m.C;
        if (Math.Abs(det) < 1e-12)
        {
            inverse = Matrix2D.Identity;
            return false;
        }
        var invDet = 1d / det;
        var a = m.D * invDet;
        var b = -m.B * invDet;
        var c = -m.C * invDet;
        var d = m.A * invDet;
        var e = -(a * m.E + c * m.F);
        var f = -(b * m.E + d * m.F);
        inverse = new Matrix2D(a, b, c, d, e, f);
        return true;
    }
}
