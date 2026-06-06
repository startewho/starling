// SPDX-License-Identifier: Apache-2.0
namespace Starling.Paint.Backend;

/// <summary>
/// Renderer-neutral handles to the compositor's GPU device and queue (native
/// wgpu pointers). A GPU paint backend receives this to allocate its own render
/// surface; it carries no backend type, so the compositor never names ImageSharp.
/// </summary>
internal readonly record struct GpuPaintDevice(nint Device, nint Queue);
