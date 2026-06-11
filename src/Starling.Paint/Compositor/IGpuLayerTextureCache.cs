// SPDX-License-Identifier: Apache-2.0
using Starling.Paint.Backend;

namespace Starling.Paint.Compositor;

internal interface IGpuLayerTextureCache
{
    bool HasResidentTexture(long contentHash, int width, int height);

    GpuPaintDevice GpuDevice { get; }

    void AdoptTexture(long contentHash, GpuPaintTexture texture);

    /// <summary>
    /// True when <see cref="ApplyLayerFilters"/> can run every function in
    /// <paramref name="filters"/> on the GPU. The compositor checks this BEFORE
    /// rastering a filtered layer so an unsupported chain takes the legacy
    /// per-tile path without a wasted whole-layer raster.
    /// </summary>
    bool SupportsLayerFilters(IReadOnlyList<DisplayList.FilterFunction> filters) => false;

    /// <summary>
    /// Runs a resolved CSS filter chain over <paramref name="source"/> on the
    /// GPU and returns a NEW straight-alpha texture — possibly smaller than the
    /// source (large blurs run at reduced resolution; the blend quad's linear
    /// sampling upscales). Always consumes/disposes <paramref name="source"/>.
    /// Returns null when the chain could not run; the caller falls back.
    /// </summary>
    GpuPaintTexture? ApplyLayerFilters(GpuPaintTexture source,
        IReadOnlyList<DisplayList.FilterFunction> filters, float scale) => null;
}
