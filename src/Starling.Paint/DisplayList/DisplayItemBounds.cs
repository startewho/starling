using Starling.Layout;

namespace Starling.Paint.DisplayList;

/// <summary>
/// Painted-extent (page-coord AABB) of a single <see cref="DisplayItem"/>, in the
/// item's own local space before any enclosing transform. Shared by the
/// LayerTreeBuilder's layer-bounds union and the per-tile content hash
/// (<see cref="DisplayListContentHash"/>) so both reason about an item's coverage
/// identically. Shadow bounds already include the blur /
/// spread / offset halo; text bounds cover the glyph run ascent + a small
/// descent. Bracket items (Push/Pop Transform/Clip) have no paint extent and
/// return false.
/// </summary>
internal static class DisplayItemBounds
{
    public static bool TryGet(DisplayItem item, out Rect bounds)
    {
        switch (item)
        {
            case FillRect f: bounds = f.Bounds; return true;
            case FillGradient g: bounds = g.Bounds; return true;
            case StrokeRect s: bounds = s.Bounds; return true;
            case FillRoundedRect rf: bounds = rf.Bounds; return true;
            case StrokeRoundedRect rs: bounds = rs.Bounds; return true;
            case DrawBoxShadow { Inset: true } ish:
                // Inner shadows are clipped to the padding box the item carries.
                bounds = ish.Bounds;
                return true;
            case DrawBoxShadow sh:
                // The painted shadow is the box grown by spread+blur, offset.
                var pad = sh.Spread + sh.Blur;
                bounds = new Rect(
                    sh.Bounds.X + sh.OffsetX - pad,
                    sh.Bounds.Y + sh.OffsetY - pad,
                    sh.Bounds.Width + 2 * pad,
                    sh.Bounds.Height + 2 * pad);
                return true;
            case DrawImage i: bounds = i.Bounds; return true;
            // Per-side styled borders paint inside the border box (dots are
            // inset, the corner arc pen stays within the band).
            case DrawBorderSides bs: bounds = bs.Bounds; return true;
            case DrawText t:
                // Glyph run sits on the baseline; cover ascent above and a small
                // descent below so the AABB encloses the rasterized glyphs.
                bounds = new Rect(t.X, t.Y - t.FontSize, EstimateTextWidth(t), t.FontSize * 1.3);
                return true;
            case DrawTextDecoration d:
                // Decoration lines span the full glyph box.
                bounds = new Rect(d.X, d.BaselineY - d.FontSize, d.Width, d.FontSize * 1.3);
                return true;
            case DrawTextShadow s:
                // Offset + blurred copy of the glyph run.
                bounds = new Rect(
                    s.X + s.OffsetX - s.Blur,
                    s.Y - s.FontSize + s.OffsetY - s.Blur,
                    EstimateShadowWidth(s) + 2 * s.Blur,
                    s.FontSize * 1.3 + 2 * s.Blur);
                return true;
            default:
                bounds = Rect.Empty;
                return false;
        }
    }

    private static double EstimateTextWidth(DrawText t)
        => t.Shaped is { } run && run.Advance > 0 ? run.Advance : t.Text.Length * t.FontSize * 0.6;

    private static double EstimateShadowWidth(DrawTextShadow s)
        => s.Shaped is { } run && run.Advance > 0 ? run.Advance : s.Text.Length * s.FontSize * 0.6;
}
