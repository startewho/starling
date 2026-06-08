// SPDX-License-Identifier: Apache-2.0
namespace Starling.Paint.Backend;

/// <summary>
/// Renderer-neutral GPU-resident paint result: native wgpu texture and
/// texture-view handles plus size and format. The compositor adopts this object
/// and disposes it (releasing the backend-owned surface via <see cref="_owner"/>)
/// when the texture-cache entry is replaced or evicted. Carries no backend type,
/// so any GPU backend can produce one.
/// </summary>
internal sealed class GpuPaintTexture : IDisposable
{
    private IDisposable? _owner;

    public GpuPaintTexture(
        nint textureHandle,
        nint textureViewHandle,
        int width,
        int height,
        PaintTextureFormat format,
        IDisposable owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        TextureHandle = textureHandle;
        TextureViewHandle = textureViewHandle;
        Width = width;
        Height = height;
        Format = format;
        _owner = owner;
    }

    public nint TextureHandle { get; }

    public nint TextureViewHandle { get; }

    public int Width { get; }

    public int Height { get; }

    public PaintTextureFormat Format { get; }

    public void Dispose()
    {
        _owner?.Dispose();
        _owner = null;
    }
}
