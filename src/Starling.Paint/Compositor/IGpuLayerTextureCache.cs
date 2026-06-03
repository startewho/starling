// SPDX-License-Identifier: Apache-2.0
using Starling.Paint.Backend;

namespace Starling.Paint.Compositor;

internal interface IGpuLayerTextureCache
{
    bool HasResidentTexture(long contentHash, int width, int height);

    GpuPaintDeviceContext ImageSharpContext { get; }

    void AdoptTexture(long contentHash, GpuPaintTexture texture);
}
