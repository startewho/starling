namespace Starling.Paint.Backend;

/// <summary>
/// A solid-colour rectangle drawn on top of the page on the GPU surface present path —
/// the text caret, selection highlight, and find-match flash that the readback path
/// renders as Avalonia controls over the bitmap. Coordinates are page (document) space,
/// the same space as the display list; the compositor applies the viewport transform.
/// Colour is straight (non-premultiplied) RGBA, alpha included so a translucent
/// selection tint blends over the page. The compositor turns each rect into one
/// resident solid-colour quad — see <c>Compositor.RenderToSurface</c>.
/// </summary>
public readonly record struct SurfaceOverlayRect(double X, double Y, double W, double H, byte R, byte G, byte B, byte A);
