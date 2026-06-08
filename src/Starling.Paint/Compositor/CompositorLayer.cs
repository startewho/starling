using Starling.Css.Values;
using Starling.Layout;
using Starling.Layout.Box;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Compositor;

/// <summary>
/// One node of the compositor layer tree built by <see cref="LayerTreeBuilder"/>:
/// a promotable box (or the implicit document root) and the slice of the display
/// list its subtree paints, excluding anything inside descendant layers.
/// </summary>
/// <remarks>
/// Coordinate spaces: <see cref="Items"/> and <see cref="Bounds"/> are in
/// <em>page coords</em> (CSS px, document origin) — the same space the flat
/// <see cref="DisplayList.DisplayListBuilder"/> emits. The layer's
/// <see cref="Transform"/> / <see cref="Opacity"/> / <see cref="Clip"/> are the
/// effective cascade values for the layer-root box and are applied at
/// <em>composite</em> time (by <see cref="Compositor"/>), never baked into the
/// slice — that is why a promoted transformed box paints upright into its own
/// bitmap and the final composite rotates it.
/// </remarks>
internal sealed class CompositorLayer
{
    public CompositorLayer(
        PaintList items,
        Rect bounds,
        Matrix2D transform,
        float opacity,
        Rect? clip,
        IReadOnlyList<CompositorLayer> children,
        long contentHash = 0,
        long layerId = 0,
        Box? sourceBox = null,
        double originParentX = 0,
        double originParentY = 0)
    {
        Items = items;
        Bounds = bounds;
        Transform = transform;
        Opacity = opacity;
        Clip = clip;
        Children = children;
        ContentHash = contentHash;
        LayerId = layerId;
        SourceBox = sourceBox;
        OriginParentX = originParentX;
        OriginParentY = originParentY;
    }

    /// <summary>
    /// The layout box this layer was built from (the layer-root box), or null for
    /// layers with no backing box. Kept so the live renderer can rebuild just this
    /// layer's node on an animation frame (see
    /// <see cref="LayerTreeBuilder.RefreshAnimating"/>) instead of rebuilding the
    /// whole tree.
    /// </summary>
    public Box? SourceBox { get; }

    /// <summary>Page-coord content origin of the box that contains
    /// <see cref="SourceBox"/> — the origin <see cref="LayerTreeBuilder"/> needs to
    /// rebuild this layer node in isolation.</summary>
    public double OriginParentX { get; }

    /// <summary>See <see cref="OriginParentX"/>.</summary>
    public double OriginParentY { get; }

    /// <summary>Returns a copy of this layer with a different child list, reusing
    /// every other field (slice, bounds, transform, hash). Used by the incremental
    /// refresh to swap a refreshed descendant subtree without rebuilding this
    /// layer's own slice.</summary>
    public CompositorLayer WithChildren(IReadOnlyList<CompositorLayer> children)
        => new(Items, Bounds, Transform, Opacity, Clip, children, ContentHash, LayerId,
            SourceBox, OriginParentX, OriginParentY);

    /// <summary>Page-coord union of the painted items in this layer's slice.</summary>
    public Rect Bounds { get; }

    /// <summary>This layer's display-list slice (descendant-layer items excluded).</summary>
    public PaintList Items { get; }

    /// <summary>
    /// Effective document-space transform of the layer-root box
    /// (<c>T(+origin) × M × T(-origin)</c>, centre origin), or
    /// <see cref="Matrix2D.Identity"/> when the box has no transform.
    /// </summary>
    public Matrix2D Transform { get; }

    /// <summary>Effective opacity (1.0 when the box has no <c>opacity</c>).</summary>
    public float Opacity { get; }

    /// <summary>
    /// Page-coord clip rect from <c>overflow: hidden</c>, else null.
    /// Border-radius clipping is deferred (WP note).
    /// </summary>
    public Rect? Clip { get; }

    /// <summary>
    /// Child layers in PAINT ORDER. The z-index sort (CSS-Position-3 §9:
    /// negative z below, then auto/0 in tree order, then positive) happens in
    /// <see cref="LayerTreeBuilder"/>, so the compositor draws them as-is.
    /// </summary>
    public IReadOnlyList<CompositorLayer> Children { get; }

    /// <summary>
    /// Stable cross-frame id of this layer (from its root element via
    /// <see cref="TileGrid.LayerIdFor"/>), used as the tile cache key's layer
    /// component. 0 when the layer has no element (no cross-frame tile reuse).
    /// </summary>
    public long LayerId { get; }

    /// <summary>
    /// 64-bit content hash of this layer's slice (LTF-02), used as the picture
    /// cache's version key instead of the global page version. The slice excludes
    /// the layer-root's composite-time transform/opacity, so a transform/opacity-
    /// only frame hashes identically and re-blits from cache; a content change
    /// (color/text/size) changes the hash and re-rasters this layer alone.
    /// </summary>
    public long ContentHash { get; }
}
