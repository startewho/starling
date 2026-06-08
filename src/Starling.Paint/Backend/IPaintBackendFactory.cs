// SPDX-License-Identifier: Apache-2.0
using Starling.Layout.Text;

namespace Starling.Paint.Backend;

/// <summary>
/// Pairs a paint backend with its matching text measurer so layout-time width
/// and raster-time glyphs always come from the same font stack. The registration
/// point a non-ImageSharp backend (e.g. Vello) plugs into — implement this and
/// add a <see cref="PaintBackendKind"/> arm in <see cref="PaintBackendSelector"/>.
/// </summary>
internal interface IPaintBackendFactory
{
    PaintBackendKind Kind { get; }

    IPaintBackend CreateBackend(FontResolver fonts, FontFaceRegistry? webFonts);

    ITextMeasurer CreateMeasurer(FontResolver fonts, FontFaceRegistry? webFonts);
}
