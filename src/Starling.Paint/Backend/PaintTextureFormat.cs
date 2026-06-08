// SPDX-License-Identifier: Apache-2.0
namespace Starling.Paint.Backend;

/// <summary>
/// Renderer-neutral pixel format of a GPU paint texture handed to the
/// compositor. Maps to the backend's native surface format (e.g. wgpu
/// <c>Rgba8Unorm</c>) without exposing the backend's own enum across the seam.
/// </summary>
internal enum PaintTextureFormat
{
    Rgba8Unorm,
    Bgra8Unorm,
}
