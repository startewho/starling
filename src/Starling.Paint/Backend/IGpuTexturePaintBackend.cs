// SPDX-License-Identifier: Apache-2.0

using LayoutRect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Backend;

/// <summary>
/// Paint backend extension for GPU surface sessions. It renders a display list
/// into a texture on the caller's WebGPU device, with no CPU pixel readback.
/// </summary>
internal interface IGpuTexturePaintBackend : IPaintBackend
{
    GpuPaintTexture RenderTexture(
        PaintList list,
        LayoutRect viewport,
        float scale,
        bool opaqueBackground,
        GpuPaintDevice device);
}
