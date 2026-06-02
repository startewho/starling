// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace Starling.Paint.Backend;

/// <summary>
/// GPU-resident paint result backed by an owned ImageSharp WebGPU render target.
/// The compositor adopts this object and releases it when the texture cache entry
/// is replaced or evicted.
/// </summary>
internal sealed class GpuPaintTexture : IDisposable
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

    public GpuPaintTexture(WebGPURenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        _target = target;
        Width = target.Width;
        Height = target.Height;
        Format = target.Format;
    }

    public int Width { get; }

    public int Height { get; }

    public WebGPUTextureFormat Format { get; }

    public nint TextureHandle => GetNativeHandle(TextureHandleProperty);

    public nint TextureViewHandle => GetNativeHandle(TextureViewHandleProperty);

    private WebGPURenderTarget Target
        => _target ?? throw new ObjectDisposedException(nameof(GpuPaintTexture));

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
