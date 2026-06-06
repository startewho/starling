// SPDX-License-Identifier: Apache-2.0
using Starling.Layout.Text;
using Starling.Paint.Backend;

namespace Starling.Paint.NeutralStub;

/// <summary>
/// Registers the SixLabors-free <see cref="NeutralStubPaintBackend"/> behind the
/// paint-backend seam. Reuses the heuristic <see cref="DefaultTextMeasurer"/> so
/// no font library is needed. Call <see cref="PaintBackendSelector.RegisterFactory"/>
/// (the documented plug-in point) to make it selectable.
/// </summary>
internal sealed class NeutralStubBackendFactory : IPaintBackendFactory
{
    public PaintBackendKind Kind => PaintBackendKind.NeutralStub;

    public IPaintBackend CreateBackend(FontResolver fonts, FontFaceRegistry? webFonts)
        => new NeutralStubPaintBackend();

    public ITextMeasurer CreateMeasurer(FontResolver fonts, FontFaceRegistry? webFonts)
        => DefaultTextMeasurer.Instance;
}
