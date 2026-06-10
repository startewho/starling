namespace Starling.Layout.Scroll;

/// <summary>
/// Per-box overflow/position classification used by scroll measurement,
/// memoized on <see cref="Box.Box.ScrollFlags"/> so the keyword resolution
/// (two overflow reads plus a position read per box) happens once per box
/// lifetime instead of once per layout pass. Safe to memoize because a box's
/// <see cref="Box.Box.Style"/> never changes — a style change rebuilds the box.
/// </summary>
[Flags]
internal enum ScrollBoxFlags : byte
{
    None = 0,

    /// <summary>The flags below have been computed for this box.</summary>
    Computed = 1,

    /// <summary>Either overflow axis computes to <c>auto</c> or <c>scroll</c> —
    /// the v1 scroll-container test (see ScrollOverflowMeasurer remarks).</summary>
    ScrollContainer = 2,

    /// <summary>Either overflow axis clips
    /// (<c>auto</c>/<c>scroll</c>/<c>hidden</c>/<c>clip</c>): the box owns its
    /// inner overflow, so extent walks stop at its border box.</summary>
    ClipsOverflow = 4,

    /// <summary><c>position: fixed</c> — viewport-anchored, contributes to no
    /// scrolling area (CSS Overflow 3 §2.2).</summary>
    FixedPosition = 8,
}
