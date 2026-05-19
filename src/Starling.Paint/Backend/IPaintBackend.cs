using Starling.Common.Image;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Backend;

/// <summary>Renderer-neutral seam consumed by <see cref="Painter"/> via <see cref="PaintBackendSelector"/>.</summary>
internal interface IPaintBackend : IDisposable
{
    string Name { get; }
    RenderedBitmap Render(PaintList list, LayoutSize viewport, float scale = 1.0f);
}
