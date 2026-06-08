// SPDX-License-Identifier: Apache-2.0
using System.Reflection;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace Starling.Paint.Backend;

/// <summary>
/// Adapter-internal owner of an ImageSharp WebGPU render target. It reflects the
/// non-public native texture/texture-view handles out of the target so the
/// neutral <see cref="GpuPaintTexture"/> can carry them across the seam without
/// any SixLabors type. Disposing releases the underlying render target.
/// </summary>
internal sealed class ImageSharpGpuTexture : IDisposable
{
    private static readonly PropertyInfo TextureHandleProperty = typeof(WebGPURenderTarget).GetProperty(
        "TextureHandle",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ImageSharp WebGPU TextureHandle property was not found.");

    private static readonly PropertyInfo TextureViewHandleProperty = typeof(WebGPURenderTarget).GetProperty(
        "TextureViewHandle",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ImageSharp WebGPU TextureViewHandle property was not found.");

    private WebGPURenderTarget? _target;

    public ImageSharpGpuTexture(WebGPURenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        Width = target.Width;
        Height = target.Height;
    }

    public int Width { get; }

    public int Height { get; }

    public nint TextureHandle => GetNativeHandle(TextureHandleProperty);

    public nint TextureViewHandle => GetNativeHandle(TextureViewHandleProperty);

    private WebGPURenderTarget Target
        => _target ?? throw new ObjectDisposedException(nameof(ImageSharpGpuTexture));

    private nint GetNativeHandle(PropertyInfo property)
    {
        var handle = property.GetValue(Target) as SafeHandle
            ?? throw new InvalidOperationException($"ImageSharp WebGPU {property.Name} was not a safe handle.");
        return handle.DangerousGetHandle();
    }

    public void Dispose()
    {
        _target?.Dispose();
        _target = null;
    }
}
