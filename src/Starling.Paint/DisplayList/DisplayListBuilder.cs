using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Layout.Tree;

namespace Starling.Paint.DisplayList;

/// <summary>
/// Walks a laid-out box tree and emits a display list in paint order.
/// </summary>
/// <remarks>
/// Emits paint items for backgrounds, borders, images, text, shadows,
/// transforms, scroll clips, and compositor layer slices. The compositor can
/// split the same display list into cached layers without changing the paint
/// commands.
/// </remarks>
public sealed class DisplayListBuilder
{
    /// <summary>
    /// Overdraw margin (CSS px) added around the viewport before culling.
    /// Items whose AABB falls within this margin of the visible region are
    /// still emitted so partial-pixel scroll and sub-pixel rounding don't pop
    /// content at the edges.
    /// </summary>
    private const double OverdrawMargin = 64d;

    /// <summary>Host abort signal — see Painter.LayoutDocumentWithStyle.
    /// Observed by the box-tree walk so a Stop arriving during paint of a very
    /// large page unwinds between sibling boxes instead of running to completion.</summary>
    private readonly CancellationToken _abort;

    /// <summary>
    /// Page-coordinate origin of the current viewport (the scroll offset), or
    /// null when no viewport is set (full-page paint). Used to anchor
    /// <c>position: fixed</c> subtrees to the viewport instead of the page —
    /// the layout pass writes their frame in viewport-relative coordinates
    /// against the initial containing block, and the painter translates that
    /// by (viewport.X, viewport.Y) so the box paints at the right page coord
    /// for the current scroll position.
    /// </summary>
    private double _viewportX;
    private double _viewportY;
    private bool _hasViewport;

    /// <summary>
    /// Lookup that, for a given Element, returns its current local scroll
    /// offset inside an <c>overflow: scroll | auto</c> container. The painter
    /// emits a translation transform of <c>(-X, -Y)</c> around the container's
    /// children so the scrolled-to portion lands inside the container's
    /// (cull-clipped) frame. Null when the host doesn't track per-element
    /// scrolling (tests / headless renders), in which case every container is
    /// painted at offset (0, 0).
    /// </summary>
    private Func<Element, (double X, double Y)>? _scrollOffsets;

    public DisplayListBuilder() : this(CancellationToken.None) { }

    public DisplayListBuilder(CancellationToken abort)
    {
        _abort = abort;
    }

    /// <summary>
    /// Builds a display list from a laid-out box tree (no culling — every item
    /// is emitted). <paramref name="styleOverride"/> is an optional per-box
    /// hook used to swap in fresh styles without re-laying out — interactive
    /// shells call it with hover/focus-recascaded styles so
    /// <c>a:hover { color: red }</c> repaints in red at the same glyph
    /// positions. Returning <c>null</c> for a box keeps the layout-time
    /// <see cref="Box.Style"/>.
    /// </summary>
    public DisplayList Build(BlockBox root, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null)
        => Build(root, viewport: null, styleOverride, images, scrollOffsets: null);

    /// <summary>
    /// Builds a display list, optionally culling to a page-coordinate
    /// <paramref name="viewport"/>. When <paramref name="viewport"/> is
    /// non-null only items whose (post-transform) page-coordinate AABB
    /// intersects the viewport (expanded by <see cref="OverdrawMargin"/>) are
    /// emitted, and transformed subtrees with no surviving items skip their
    /// <see cref="PushTransform"/>/<see cref="PopTransform"/> bracket entirely
    /// so the cost stays O(items on screen). When null, every item is emitted
    /// (the full-page behavior the headless screenshot path relies on).
    /// </summary>
    public DisplayList Build(BlockBox root, Rect? viewport, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null, Func<Element, (double X, double Y)>? scrollOffsets = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var list = new DisplayList();
        Rect? cull = viewport is { } v
            ? new Rect(v.X - OverdrawMargin, v.Y - OverdrawMargin, v.Width + 2 * OverdrawMargin, v.Height + 2 * OverdrawMargin)
            : null;
        if (viewport is { } vp)
        {
            _viewportX = vp.X;
            _viewportY = vp.Y;
            _hasViewport = true;
        }
        else
        {
            _hasViewport = false;
        }
        _scrollOffsets = scrollOffsets;
        Visit(box: root, list, originX: 0, originY: 0, current: Matrix2D.Identity, cull, styleOverride, images, slice: null);
        return list;
    }

    /// <summary>
    /// Builds the display-list <em>slice</em> for a single compositor layer
    /// rooted at <paramref name="sliceRoot"/>: the items <paramref name="sliceRoot"/>
    /// and its subtree paint, EXCLUDING any box (other than the slice root
    /// itself) for which <paramref name="isLayerBoundary"/> returns true — those
    /// boxes are descendant layers and the <see cref="Compositor.LayerTreeBuilder"/>
    /// emits them into their own slices. <paramref name="originX"/> /
    /// <paramref name="originY"/> are the page-coord content origin of the slice
    /// root's PARENT (i.e. the same origin its enclosing <see cref="Visit"/> call
    /// would have used), so a slice is painted in the SAME page coordinate space
    /// as the flat build — the layer's transform/opacity are applied later, at
    /// composite time, not baked into the slice.
    /// <para>
    /// When <paramref name="suppressRootTransform"/> is true the slice root's own
    /// CSS <c>transform</c> bracket is skipped: the layer carries that transform
    /// (<see cref="Compositor.CompositorLayer.Transform"/>) and applies it at
    /// composite time, so the slice holds the untransformed (upright) content.
    /// Nested non-promoted transformed descendants inside the slice still get
    /// their normal push/pop brackets.
    /// </para>
    /// </summary>
    internal DisplayList BuildLayerSlice(
        Box sliceRoot,
        double originX,
        double originY,
        Func<Box, bool> isLayerBoundary,
        bool suppressRootTransform,
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        Func<Element, (double X, double Y)>? scrollOffsets = null)
    {
        ArgumentNullException.ThrowIfNull(sliceRoot);
        ArgumentNullException.ThrowIfNull(isLayerBoundary);
        var list = new DisplayList();
        // Per-container scroll offsets so a slice that contains an overflow:scroll
        // subtree paints it at the user-scrolled position (the Visit recursion
        // brackets the scrolled children in a -offset transform). Lets the zero-copy
        // surface path render inner-scrolled pages instead of declining to readback.
        _scrollOffsets = scrollOffsets;
        var slice = new LayerSlice(sliceRoot, isLayerBoundary);
        if (suppressRootTransform)
        {
            // The slice root's transform is owned by its layer, so paint its
            // own box + children directly (no transform bracket) in local space.
            var frameX = originX + sliceRoot.Frame.X;
            var frameY = originY + sliceRoot.Frame.Y;
            PaintBoxAndChildren(sliceRoot, list, frameX, frameY, Matrix2D.Identity, cull: null, styleOverride, images, slice);
        }
        else
        {
            Visit(sliceRoot, list, originX, originY, Matrix2D.Identity, cull: null, styleOverride, images, slice);
        }
        return list;
    }

    /// <summary>
    /// Threaded through the recursion during a layer-slice build. Carries the
    /// slice root (which is always emitted even though it is itself a layer
    /// boundary) and the predicate identifying descendant layer boundaries to
    /// exclude. Null on the flat full-document build, where every box is emitted.
    /// </summary>
    private sealed record LayerSlice(Box Root, Func<Box, bool> IsBoundary);

    private static ComputedStyle? EffectiveStyle(Box box, Func<Box, ComputedStyle?>? styleOverride)
        => styleOverride?.Invoke(box) ?? box.Style;

    /// <summary>
    /// Whether this box paints its own content (CSS 2.2 §11.2). `visibility:
    /// hidden`/`collapse` suppresses the box's own background, border, text and
    /// replaced content; children are unaffected (they may flip back to
    /// `visible`). Boxes with no style (rare) are treated as visible.
    /// </summary>
    private static bool IsSelfVisible(Box box, Func<Box, ComputedStyle?>? styleOverride)
        => EffectiveStyle(box, styleOverride) is not { } style
           || style.Get(PropertyId.Visibility) is not CssKeyword k
           || k.Name.Equals("visible", StringComparison.OrdinalIgnoreCase);

    private static bool IsFixedPositioned(Box box, Func<Box, ComputedStyle?>? styleOverride)
        => EffectiveStyle(box, styleOverride) is { } style
           && style.Get(PropertyId.Position) is CssKeyword k
           && k.Name.Equals("fixed", StringComparison.OrdinalIgnoreCase);

    private void Visit(Box box, DisplayList list, double originX, double originY, Matrix2D current, Rect? cull, Func<Box, ComputedStyle?>? styleOverride, IImageResolver? images, LayerSlice? slice)
    {
        // Host abort (Stop button, navigation supersede). One check per visited
        // box keeps the cancellation latency bounded across a deep DOM without
        // touching the inner-emit hot loops below.
        _abort.ThrowIfCancellationRequested();

        // Layer-slice mode: a descendant that is itself a layer root paints into
        // its OWN slice, not this one. The slice root is exempt — it is the box
        // this slice is rooted at, so it must paint here.
        if (slice is not null && !ReferenceEquals(box, slice.Root) && slice.IsBoundary(box))
            return;

        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        // `position: fixed` anchors the box to the initial containing block (the
        // viewport). Layout writes the resolved frame in viewport-relative
        // coordinates — e.g. `inset-block-start: 0` becomes Y=0 in the page —
        // but when the user scrolls the painter is asked for a viewport whose
        // page-Y is non-zero. Adding the viewport origin here shifts the entire
        // fixed subtree onto the page coords the painter is sampling, so the
        // box stays glued to the viewport edge regardless of scroll position.
        // Skipped when no viewport is set (full-page screenshots) so headless
        // captures keep their pre-existing "fixed boxes at Y=0" output.
        if (_hasViewport && IsFixedPositioned(box, styleOverride))
        {
            frameX += _viewportX;
            frameY += _viewportY;
        }

        // CSS `transform` is applied around the box's transform-origin (default
        // 50% 50% 0). Layout is unaffected: the box keeps its frame coordinates
        // and paint composes T(+origin) × M × T(-origin) on top of every
        // primitive in this subtree (paint of this box and every descendant).
        // Transform-origin parsing is a follow-up; the default centre is the
        // most common case and matches the spec's initial value.
        var transformMatrix = TryGetTransformMatrix(box, styleOverride, frameX, frameY);
        var transformed = transformMatrix is not null;

        if (transformed)
        {
            // Items inside the transform must be culled using their
            // post-transform AABB, so compose the matrix onto the active one
            // and paint the subtree into a scratch list. Only if it produced
            // visible items do we emit the bracket — an entirely off-screen
            // transformed subtree contributes nothing and stays O(on-screen).
            var composed = current.Multiply(transformMatrix!.Value);
            var scratch = new DisplayList();
            PaintBoxAndChildren(box, scratch, frameX, frameY, composed, cull, styleOverride, images, slice);
            if (scratch.Items.Count > 0)
            {
                list.Add(new PushTransform(transformMatrix.Value));
                foreach (var item in scratch.Items)
                    list.Add(item);
                list.Add(PopTransform.Instance);
            }
            return;
        }

        PaintBoxAndChildren(box, list, frameX, frameY, current, cull, styleOverride, images, slice);
    }

    /// <summary>
    /// Adds <paramref name="item"/> to <paramref name="list"/> unless culling is
    /// active and the item's page-coordinate AABB (the local <paramref name="bounds"/>
    /// transformed by <paramref name="current"/>) does not intersect the
    /// viewport.
    /// </summary>
    private static void Emit(DisplayList list, DisplayItem item, Rect bounds, Matrix2D current, Rect? cull)
    {
        if (cull is { } c && !Intersects(TransformedAabb(bounds, current), c)) return;
        list.Add(item);
    }

    private static Rect TransformedAabb(Rect r, Matrix2D m)
    {
        if (m.IsIdentity) return r;
        var (x0, y0) = m.Transform(r.X, r.Y);
        var (x1, y1) = m.Transform(r.X + r.Width, r.Y);
        var (x2, y2) = m.Transform(r.X + r.Width, r.Y + r.Height);
        var (x3, y3) = m.Transform(r.X, r.Y + r.Height);
        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static bool Intersects(Rect a, Rect b)
        => a.X < b.Right && a.Right > b.X && a.Y < b.Bottom && a.Bottom > b.Y;

    private void PaintBoxAndChildren(Box box, DisplayList list, double frameX, double frameY, Matrix2D current, Rect? cull, Func<Box, ComputedStyle?>? styleOverride, IImageResolver? images, LayerSlice? slice)
    {
        // Backgrounds and borders for box-bearing boxes. Only BlockContainer
        // qualifies unconditionally; InlineBox qualifies only when it is an
        // atomic inline (display:inline-block) — flattened spans have a zero
        // frame and would otherwise emit a phantom rect at the origin.
        //
        // AnonymousBlock must NOT paint decorations: per CSS 2.2 §9.2.2.1 an
        // anonymous box inherits only inheritable properties from its enclosing
        // box; non-inherited ones (background, border, box-shadow, …) take their
        // initial values. Our AnonymousBlockBox carries the *parent's* full
        // ComputedStyle (for text/font inheritance), so painting its background
        // here re-painted the parent's — visible as a brighter rectangle behind
        // the text whenever that background was semi-transparent (e.g. a pill or
        // search field with rgba()/#rrggbbaa fill).
        // CSS 2.2 §11.2 — `visibility: hidden`/`collapse` makes a box invisible
        // (no background, border, text, or replaced content) while it still
        // takes part in layout. It is inherited, but a descendant may flip back
        // to `visible`, so we suppress only THIS box's own painting and always
        // recurse into children.
        var selfVisible = IsSelfVisible(box, styleOverride);

        var hasFrame = box.Frame.Width > 0 && box.Frame.Height > 0;
        var paintsBox = selfVisible
            && (box.Kind is BoxKind.BlockContainer
                || (box.Kind == BoxKind.Inline && hasFrame));

        // CSS Masking 1 §5 — when the box carries a paintable mask-image the
        // entire element (background + border + content + descendants) is
        // composited into an offscreen surface and then masked. Per the spec,
        // box-shadows paint OUTSIDE the masked group (they are not affected by
        // the element's own mask), so they are emitted first on the main list
        // and the PushMask bracket only wraps the box-interior paint.
        //
        // To wire this up: detect the mask here; emit outer shadows on the main
        // list; collect box-interior + children into `innerList`; wrap with
        // PushMask / PopMask and flush to the main list at the end.
        MaskGeometry? maskGeometry = null;
        if (paintsBox && EffectiveStyle(box, styleOverride) is { } maskCheckStyle)
        {
            maskGeometry = TryResolveMaskGeometry(box, frameX, frameY, maskCheckStyle, images);
        }

        // CSS Masking 1 §7 — clip-path clips the ENTIRE element rendering
        // (background + border + content + descendants), unlike overflow which
        // clips only descendants. Detect a non-none, non-url clip-path here and,
        // when present, wrap all interior paint in PushClipPath/PopClipPath.
        // box-shadows are NOT clipped by clip-path (they are outside the element).
        CssClipPath? clipPathValue = null;
        if (paintsBox && EffectiveStyle(box, styleOverride) is { } clipCheckStyle)
        {
            clipPathValue = TryResolveClipPath(clipCheckStyle);
        }

        // When there is a mask, innerList is a scratch list that collects all
        // box-interior paint (background + border + content + descendants).
        // When there is no mask, innerList == list — no extra allocation.
        var innerList = maskGeometry is not null ? new DisplayList() : list;

        // When clip-path applies, we need yet another scratch layer to collect
        // the items that go inside the clip bracket. The layering from outermost
        // to innermost is: list → [PushMask → innerList → [PushClipPath → clipList]].
        // When there is no mask, innerList == list, so the bracket collapses cleanly.
        DisplayList? clipList = null;
        if (clipPathValue is not null)
        {
            clipList = new DisplayList();
        }

        // activeList is the target for all box-interior paint (backgrounds, borders,
        // content, children). When clip-path is active it is the clipList scratch;
        // otherwise it is innerList (which may itself be a mask scratch or == list).
        var activeList = clipList ?? innerList;

        if (paintsBox && EffectiveStyle(box, styleOverride) is { } style)
        {
            var bounds = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);

            // CSS Backgrounds 3 §5 — the four corner radii (clamped to the box
            // per §5.1 so overlapping radii never exceed the box dimensions).
            var radii = ReadCornerRadii(style, box.Frame.Width, box.Frame.Height);

            // CSS Backgrounds 3 §6 — box-shadow. Outer shadows paint BEHIND the
            // box (before background + border), in reverse list order so the
            // first listed layer ends up on top. Inset shadows are parsed but
            // painted best-effort (the outer drop shadow is the supported path).
            // When the box is masked, shadows still paint on the MAIN list (they
            // are not part of the masked group per CSS Masking 1 §5).
            // Per CSS Masking 1 §7.2 box-shadow also paints OUTSIDE clip-path.
            EmitBoxShadows(box, bounds, radii, list, current, cull, style);

            // CSS Backgrounds 3 §3.8 — `background-clip: text`. The background
            // (gradient or solid color) is clipped to the union of the
            // element's text glyphs instead of filling the box. Detect it here
            // and, when present, emit a single glyph-clipped fill in place of
            // the normal background fill + background-image gradient.
            if (IsTextClip(style) && TryEmitBackgroundTextClip(box, frameX, frameY, activeList, current, cull, style, styleOverride))
            {
                // Borders still paint normally; only the background was
                // diverted to the glyph-clipped item.
                EmitBorders(box, frameX, frameY, activeList, current, cull, style, radii);
            }
            else
            {
                var bg = style.GetColor(PropertyId.BackgroundColor);
                if (bg is { A: > 0 })
                {
                    if (radii.IsZero)
                        Emit(activeList, new FillRect(bounds, bg, FillRectPixelAlignment.Preserve), bounds, current, cull);
                    else
                        Emit(activeList, new FillRoundedRect(bounds, radii, bg), bounds, current, cull);
                }

                // CSS Backgrounds 3 §3 — background-image paints inside the
                // box's padding box (border-box is the default origin per spec,
                // but at this layout fidelity the padding+border distinction
                // is unobservable and using the frame is correct).
                EmitBackgroundImage(box, frameX, frameY, activeList, current, cull, style, images, radii);

                // Borders. Painter renders one stroke per side that has a non-zero width.
                EmitBorders(box, frameX, frameY, activeList, current, cull, style, radii);
            }
        }

        // Inline content: text fragments live on TextBoxes, positioned in their
        // anonymous-block parent's content box.
        if (box is TextBox textBox)
        {
            if (selfVisible)
                EmitTextFragments(textBox, frameX, frameY, activeList, current, cull, styleOverride);
            FlushClipPathBracket(innerList, activeList, clipPathValue, new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height));
            if (maskGeometry is not null)
                FlushMaskBracket(list, innerList, maskGeometry);
            return; // Text boxes have no children.
        }

        // Replaced inline content (currently <img>): its Frame was set by the
        // inline formatting context relative to the anonymous-block container,
        // so frameX/frameY (which already includes that translation) is its
        // document-space top-left.
        if (box is ImageBox imageBox)
        {
            if (selfVisible)
            {
                var bounds = new Rect(frameX, frameY, imageBox.Frame.Width, imageBox.Frame.Height);
                Emit(activeList, new DrawImage(bounds, imageBox.Source), bounds, current, cull);
            }
            FlushClipPathBracket(innerList, activeList, clipPathValue, new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height));
            if (maskGeometry is not null)
                FlushMaskBracket(list, innerList, maskGeometry);
            return; // ImageBox has no children.
        }

        // Children paint after backgrounds. Their frames are in our content-box
        // coords, so push our padding+border origin.
        var contentOriginX = frameX + box.Border.Left + box.Padding.Left;
        var contentOriginY = frameY + box.Border.Top + box.Padding.Top;

        // A scroll container (`overflow: hidden | clip | scroll | auto` on
        // either axis) clips its descendants to its border box. We tighten
        // the cull rect (a binary optimisation that drops wholly off-screen
        // items before they reach the rasterizer) AND emit a real PushClip /
        // PopClip pair that the backend converts to a proper scissor/path
        // clip so items straddling the box edge are cropped, not painted past
        // the border. When the box also has rounded corners the clip path is
        // the rounded rectangle, implementing CSS Overflow 3 §2.4.
        var childCull = cull;
        ComputedStyle? overflowStyle = box.Kind != BoxKind.AnonymousBlock ? EffectiveStyle(box, styleOverride) : null;
        var clipsOverflow = overflowStyle is not null && ClipsOverflow(overflowStyle);

        // Per CSS Overflow 3 §2.4 a box with a rounded border also clips its
        // children to the rounded shape when overflow != visible — read the
        // radii unconditionally for the clipsOverflow path.
        CornerRadii clipRadii = CornerRadii.None;
        if (clipsOverflow)
        {
            var clipRect = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);
            childCull = cull is { } c ? IntersectRect(c, clipRect) : clipRect;

            // Include border-radius in the clip so rounded overflow boxes
            // crop their children to the rounded inner edge.
            if (overflowStyle is not null && box.Frame.Width > 0 && box.Frame.Height > 0)
                clipRadii = ReadCornerRadii(overflowStyle, box.Frame.Width, box.Frame.Height);
        }

        // `overflow: scroll | auto` containers carry a per-element scroll
        // offset maintained by the shell (wheel handler in WebviewPanel). The
        // painter translates this subtree by `(-X, -Y)` so the user-scrolled
        // position lands inside the (already clip-tightened) container frame.
        // The childCull stays in unscrolled page coords; the cull check
        // applies the composed matrix to each item before comparing, so the
        // visible band picks itself out automatically.
        (double X, double Y) scrollOffset = default;
        if (overflowStyle is not null
            && box.Element is { } scrollElement
            && _scrollOffsets is { } offsets
            && ScrollsOverflow(overflowStyle))
        {
            scrollOffset = offsets(scrollElement);
        }

        var refBox = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);

        if (scrollOffset.X != 0 || scrollOffset.Y != 0)
        {
            var scrollMatrix = Matrix2D.Translate(-scrollOffset.X, -scrollOffset.Y);
            var composed = current.Multiply(scrollMatrix);
            var scratch = new DisplayList();
            foreach (var child in box.Children)
                Visit(child, scratch, contentOriginX, contentOriginY, composed, childCull, styleOverride, images, slice);
            if (scratch.Items.Count > 0)
            {
                // Emit the clip bracket around the scrolled children so they
                // are cropped to the scroll container's border box.
                if (clipsOverflow && box.Frame.Width > 0 && box.Frame.Height > 0)
                {
                    var clipBounds = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);
                    activeList.Add(new PushClip(clipBounds, clipRadii));
                }
                activeList.Add(new PushTransform(scrollMatrix));
                foreach (var item in scratch.Items)
                    activeList.Add(item);
                activeList.Add(PopTransform.Instance);
                if (clipsOverflow && box.Frame.Width > 0 && box.Frame.Height > 0)
                    activeList.Add(PopClip.Instance);
            }
            FlushClipPathBracket(innerList, activeList, clipPathValue, refBox);
            if (maskGeometry is not null)
                FlushMaskBracket(list, innerList, maskGeometry);
            return;
        }

        // Emit PushClip / children / PopClip for non-scrolling overflow clips
        // so descendants are rasterizer-cropped to this box, not just cull-dropped.
        if (clipsOverflow && box.Frame.Width > 0 && box.Frame.Height > 0)
        {
            var clipBounds = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);
            activeList.Add(new PushClip(clipBounds, clipRadii));
            foreach (var child in box.Children)
                Visit(child, activeList, contentOriginX, contentOriginY, current, childCull, styleOverride, images, slice);
            activeList.Add(PopClip.Instance);
            FlushClipPathBracket(innerList, activeList, clipPathValue, refBox);
            if (maskGeometry is not null)
                FlushMaskBracket(list, innerList, maskGeometry);
            return;
        }

        foreach (var child in box.Children)
            Visit(child, activeList, contentOriginX, contentOriginY, current, childCull, styleOverride, images, slice);

        FlushClipPathBracket(innerList, activeList, clipPathValue, refBox);
        if (maskGeometry is not null)
            FlushMaskBracket(list, innerList, maskGeometry);
    }

    // ---- CSS Masking 1 §7 — clip-path helpers --------------------------------

    /// <summary>
    /// Returns the <see cref="CssClipPath"/> for the box when it is a non-none,
    /// non-url basic shape (or geometry-box-only) value. Returns null when
    /// clip-path is none, unset, or a url() reference (which is deferred).
    /// </summary>
    private static CssClipPath? TryResolveClipPath(ComputedStyle style)
    {
        var v = style.Get(PropertyId.ClipPath);
        if (v is not CssClipPath clip) return null;
        if (clip.IsNone) return null;
        if (clip.IsUrl) return null; // url(#id) requires SVG DOM resolution — deferred
        return clip;
    }

    /// <summary>
    /// When <paramref name="clipPath"/> is non-null, wraps the items accumulated
    /// in <paramref name="activeList"/> (the interior paint) with a
    /// <see cref="PushClipPath"/> / <see cref="PopClipPath"/> bracket and appends
    /// to <paramref name="target"/>. When <paramref name="clipPath"/> is null,
    /// this is a no-op (interior paint was already written directly to target).
    /// </summary>
    private static void FlushClipPathBracket(
        DisplayList target,
        DisplayList activeList,
        CssClipPath? clipPath,
        Rect referenceBox)
    {
        if (clipPath is null || ReferenceEquals(target, activeList)) return;
        if (activeList.Items.Count == 0) return; // nothing to clip
        target.Add(new PushClipPath(referenceBox, clipPath));
        foreach (var item in activeList.Items)
            target.Add(item);
        target.Add(PopClipPath.Instance);
    }

    private static bool ScrollsOverflow(ComputedStyle style)
        => OverflowKeywordScrolls(style.Get(PropertyId.OverflowX))
        || OverflowKeywordScrolls(style.Get(PropertyId.OverflowY));

    private static bool OverflowKeywordScrolls(CssValue? value) => value switch
    {
        CssKeyword { Name: var n } =>
            n.Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("auto", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static bool ClipsOverflow(ComputedStyle style)
        => OverflowKeywordClips(style.Get(PropertyId.OverflowX))
        || OverflowKeywordClips(style.Get(PropertyId.OverflowY));

    private static bool OverflowKeywordClips(CssValue? value) => value switch
    {
        CssKeyword { Name: var n } =>
            n.Equals("hidden", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("clip", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("scroll", StringComparison.OrdinalIgnoreCase) ||
            n.Equals("auto", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static Rect IntersectRect(Rect a, Rect b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
    }

    /// <summary>
    /// CSS Backgrounds 3 §3.8 — true when this box's <c>background-clip</c>
    /// resolves to the <c>text</c> keyword (set directly or via the
    /// <c>-webkit-background-clip: text</c> alias, both of which parse to the
    /// same keyword value).
    /// </summary>
    private static bool IsTextClip(ComputedStyle style)
        => style.Get(PropertyId.BackgroundClip) is CssKeyword k
           && k.Name.Equals("text", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CSS Backgrounds 3 §3.8 — paint the box background clipped to its text
    /// glyphs. Resolves the background paint (a gradient when
    /// <c>background-image</c> is a paintable gradient, otherwise the solid
    /// <c>background-color</c>), gathers every descendant glyph run, and emits a
    /// single <see cref="FillBackgroundTextClip"/>. Returns false (so the caller
    /// falls back to the normal background path) when there is no paintable
    /// background or no text to clip to.
    /// </summary>
    private bool TryEmitBackgroundTextClip(Box box, double frameX, double frameY, DisplayList list, Matrix2D current, Rect? cull, ComputedStyle style, Func<Box, ComputedStyle?>? styleOverride)
    {
        if (box.Frame.Width <= 0 || box.Frame.Height <= 0) return false;

        // The background paint: a paintable gradient takes priority over the
        // solid color (matching the layering order of `background`).
        CssGradient? gradient = null;
        if (style.Get(PropertyId.BackgroundImage) is CssFunctionValue gradientFn
            && CssGradientParser.TryParseFunction(gradientFn, out var g)
            && g.IsPaintable)
        {
            gradient = g;
        }

        var color = style.GetColor(PropertyId.BackgroundColor);
        if (gradient is null && color.A == 0) return false;

        // Gather descendant glyph runs in document space. The clip element's own
        // text lives in descendant text boxes whose frames are relative to this
        // box's content origin.
        var glyphs = new List<ClipGlyphRun>();
        var contentX = frameX + box.Border.Left + box.Padding.Left;
        var contentY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            CollectClipGlyphRuns(child, contentX, contentY, styleOverride, glyphs);

        if (glyphs.Count == 0) return false;

        var bounds = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);
        Emit(list, new FillBackgroundTextClip(bounds, gradient, color, glyphs), bounds, current, cull);
        return true;
    }

    /// <summary>
    /// Walk a subtree collecting every text fragment as a
    /// <see cref="ClipGlyphRun"/> in document-space coordinates. <paramref name="originX"/>
    /// / <paramref name="originY"/> are the document-space top-left of
    /// <paramref name="box"/>'s parent content box, matching how the painter
    /// pushes the content origin when it descends.
    /// </summary>
    private void CollectClipGlyphRuns(Box box, double originX, double originY, Func<Box, ComputedStyle?>? styleOverride, List<ClipGlyphRun> glyphs)
    {
        var frameX = originX + box.Frame.X;
        var frameY = originY + box.Frame.Y;

        if (box is TextBox textBox)
        {
            if (textBox.Fragments.Count == 0) return;
            var style = EffectiveStyle(textBox, styleOverride);
            var fontSize = style?.Get(PropertyId.FontSize) switch
            {
                CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
                _ => 16d,
            };
            var spec = FontSpec.FromStyle(style);
            foreach (var frag in textBox.Fragments)
            {
                if (frag.Text.Length == 0 || string.IsNullOrWhiteSpace(frag.Text)) continue;
                glyphs.Add(new ClipGlyphRun(
                    frag.Text,
                    frameX + frag.X,
                    frameY + frag.Y,
                    fontSize,
                    spec.Families,
                    spec.Bold,
                    spec.Italic,
                    frag.Shaped));
            }
            return; // Text boxes have no children.
        }

        if (box is ImageBox) return; // Replaced content contributes no glyphs.

        var contentX = frameX + box.Border.Left + box.Padding.Left;
        var contentY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            CollectClipGlyphRuns(child, contentX, contentY, styleOverride, glyphs);
    }

    /// <summary>
    /// Resolve <c>background-image</c> via <paramref name="images"/> and emit
    /// a sliced <see cref="DrawImage"/> using <c>background-position</c> +
    /// <c>background-size</c> to compute the source rectangle. This is the
    /// path that makes CSS sprite sheets render — the box's frame defines the
    /// visible window, the sprite PNG is the source, and bg-position picks
    /// which slice maps to the window.
    /// </summary>
    private static void EmitBackgroundImage(Box box, double frameX, double frameY, DisplayList list, Matrix2D current, Rect? cull, ComputedStyle style, IImageResolver? images, CornerRadii radii = default)
    {
        var bgImage = style.Get(PropertyId.BackgroundImage);

        // CSS Backgrounds 3 §3.4 — `background-image` may be a comma-separated
        // list of layers. The first layer is the topmost, so paint the list
        // back-to-front (later layers sit behind earlier ones). Per-layer
        // position/size are read from the matching index of those longhands.
        if (bgImage is CssValueList layers && layers.Values.Count > 0)
        {
            var posList = style.Get(PropertyId.BackgroundPosition);
            var sizeList = style.Get(PropertyId.BackgroundSize);
            for (var i = layers.Values.Count - 1; i >= 0; i--)
                EmitOneBackgroundLayer(box, frameX, frameY, list, current, cull, images, radii,
                    layers.Values[i], LayerValueAt(sizeList, i), LayerValueAt(posList, i));
            return;
        }

        EmitOneBackgroundLayer(box, frameX, frameY, list, current, cull, images, radii,
            bgImage, style.Get(PropertyId.BackgroundSize), style.Get(PropertyId.BackgroundPosition));
    }

    /// <summary>Index into a layered background longhand: returns the i-th
    /// layer's value when <paramref name="value"/> is a list (clamping to the
    /// last entry when short), or the value itself for a single, shared value.</summary>
    private static CssValue? LayerValueAt(CssValue? value, int i)
        => value is CssValueList l
            ? (l.Values.Count == 0 ? null : l.Values[Math.Min(i, l.Values.Count - 1)])
            : value;

    private static void EmitOneBackgroundLayer(Box box, double frameX, double frameY, DisplayList list, Matrix2D current, Rect? cull, IImageResolver? images, CornerRadii radii, CssValue? layerImage, CssValue? layerSize, CssValue? layerPosition)
    {
        // CSS Images 3 §3 — `background-image: <gradient>`. Gradients paint
        // directly from the typed value and don't need an image resolver; map
        // the recognised gradient functions (linear, radial, conic, and their
        // repeating- variants) to a FillGradient over the box. Anything that
        // doesn't parse (malformed syntax) fails soft, matching the
        // unresolved-image path below. Pass the box's corner radii so the
        // backend clips the gradient fill to the rounded rectangle (CSS
        // Backgrounds 3 §5 border-radius).
        if (layerImage is CssFunctionValue gradientFn
            && CssGradientParser.TryParseFunction(gradientFn, out var gradient)
            && gradient.IsPaintable
            && box.Frame.Width > 0 && box.Frame.Height > 0)
        {
            var gbounds = new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);
            Emit(list, new FillGradient(gbounds, gradient, radii), gbounds, current, cull);
            return;
        }

        if (images is null) return;
        var url = layerImage switch
        {
            CssUrl u => u.Value,
            _ => null,
        };
        if (url is null || !images.TryResolveUrl(url, out var decoded) || decoded is null) return;
        if (box.Frame.Width <= 0 || box.Frame.Height <= 0) return;
        if (decoded.Width <= 0 || decoded.Height <= 0) return;

        var boxW = box.Frame.Width;
        var boxH = box.Frame.Height;

        // background-size — default `auto auto` keeps the image at native
        // dimensions. A single length applies to width with height = auto.
        var (renderW, renderH) = ResolveBackgroundSize(layerSize, boxW, boxH, decoded.Width, decoded.Height);
        if (renderW <= 0 || renderH <= 0) return;

        // background-position — where the rendered image's top-left lands
        // inside the box. A single value is the X offset (Y defaults to
        // centred per the spec, but the sprite use case keys on X only, so
        // we default Y to 0 for the single-value case to match common
        // sprite-sheet authoring).
        var (offsetX, offsetY) = ResolveBackgroundPosition(layerPosition, boxW, boxH, renderW, renderH);

        // The rendered image rectangle in box coordinates.
        var imgX = offsetX;
        var imgY = offsetY;

        // Clip the rendered image to the box — the visible part is the
        // intersection. Anything outside is discarded.
        var destX = Math.Max(0, imgX);
        var destY = Math.Max(0, imgY);
        var destRight = Math.Min(boxW, imgX + renderW);
        var destBottom = Math.Min(boxH, imgY + renderH);
        var destW = destRight - destX;
        var destH = destBottom - destY;
        if (destW <= 0 || destH <= 0) return;

        // Map the clipped destination rect back to source pixel coords.
        var scaleX = decoded.Width / renderW;
        var scaleY = decoded.Height / renderH;
        var srcX = (destX - imgX) * scaleX;
        var srcY = (destY - imgY) * scaleY;
        var srcW = destW * scaleX;
        var srcH = destH * scaleY;

        var dest = new Rect(frameX + destX, frameY + destY, destW, destH);
        Emit(list, new DrawImage(dest, decoded, new Rect(srcX, srcY, srcW, srcH)), dest, current, cull);
    }

    private static (double Width, double Height) ResolveBackgroundSize(CssValue? value, double boxW, double boxH, double nativeW, double nativeH)
    {
        if (value is CssKeyword { Name: "contain" })
        {
            var scale = Math.Min(boxW / nativeW, boxH / nativeH);
            return (nativeW * scale, nativeH * scale);
        }
        if (value is CssKeyword { Name: "cover" })
        {
            var scale = Math.Max(boxW / nativeW, boxH / nativeH);
            return (nativeW * scale, nativeH * scale);
        }

        double? w = null, h = null;
        if (value is CssValueList list)
        {
            if (list.Values.Count > 0) w = ResolveBgSizeDim(list.Values[0], boxW);
            if (list.Values.Count > 1) h = ResolveBgSizeDim(list.Values[^1], boxH);
        }
        else
        {
            w = ResolveBgSizeDim(value, boxW);
        }

        // `auto` on one axis preserves aspect ratio against the other.
        if (w is null && h is null) return (nativeW, nativeH);
        if (w is null) return (nativeW * (h!.Value / nativeH), h.Value);
        if (h is null) return (w.Value, nativeH * (w.Value / nativeW));
        return (w.Value, h.Value);
    }

    private static double? ResolveBgSizeDim(CssValue? value, double basis)
        => value switch
        {
            CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
            CssPercentage pct => basis * pct.Value / 100d,
            CssNumber n => n.Value,
            _ => null,
        };

    private static (double X, double Y) ResolveBackgroundPosition(CssValue? value, double boxW, double boxH, double imgW, double imgH)
    {
        CssValue? xv = null, yv = null;
        if (value is CssValueList list)
        {
            if (list.Values.Count > 0) xv = list.Values[0];
            if (list.Values.Count > 1) yv = list.Values[^1];
        }
        else
        {
            xv = value;
        }
        var x = ResolveBgPositionAxis(xv, boxW, imgW, axisX: true);
        var y = ResolveBgPositionAxis(yv, boxH, imgH, axisX: false);
        return (x, y);
    }

    private static double ResolveBgPositionAxis(CssValue? value, double boxSize, double imgSize, bool axisX)
        => value switch
        {
            CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
            CssPercentage pct => (boxSize - imgSize) * pct.Value / 100d,
            CssNumber n => n.Value,
            CssKeyword { Name: "left" } => 0,
            CssKeyword { Name: "top" } => 0,
            CssKeyword { Name: "right" } => boxSize - imgSize,
            CssKeyword { Name: "bottom" } => boxSize - imgSize,
            CssKeyword { Name: "center" } => (boxSize - imgSize) / 2d,
            // Default missing-axis behaviour: x defaults to 0, y to 0 — the
            // sprite-sheet use case is the dominant consumer and authors
            // typically pass a single horizontal offset.
            _ => 0,
        };

    /// <summary>
    /// Holds the resolved mask source and geometry for a whole-element mask group.
    /// Created by <see cref="TryResolveMaskGeometry"/> and consumed by
    /// <see cref="FlushMaskBracket"/>.
    /// </summary>
    private sealed record MaskGeometry(
        Rect Bounds,
        CornerRadii Radii,
        DecodedImage? Mask,
        CssGradient? MaskGradient,
        double RenderW,
        double RenderH,
        double OffsetX,
        double OffsetY,
        MaskRepeatMode Repeat,
        MaskModeKind Mode);

    /// <summary>
    /// Resolves the mask source and geometry for a box that has a paintable
    /// <c>mask-image</c>. Returns null when the box has no resolvable mask so
    /// the caller can bail out cheaply. The returned <see cref="MaskGeometry"/>
    /// is passed to <see cref="FlushMaskBracket"/> after the inner content is
    /// collected.
    /// </summary>
    private static MaskGeometry? TryResolveMaskGeometry(Box box, double frameX, double frameY, ComputedStyle style, IImageResolver? images)
    {
        if (box.Frame.Width <= 0 || box.Frame.Height <= 0) return null;

        var maskValue = style.Get(PropertyId.MaskImage);

        DecodedImage? maskImage = null;
        CssGradient? maskGradient = null;

        if (maskValue is CssUrl maskUrl)
        {
            if (images is null) return null;
            if (!images.TryResolveUrl(maskUrl.Value, out maskImage) || maskImage is null) return null;
            if (maskImage.Width <= 0 || maskImage.Height <= 0) return null;
        }
        else if (maskValue is CssFunctionValue maskGradientFn
                 && CssGradientParser.TryParseFunction(maskGradientFn, out var mg)
                 && mg.IsPaintable)
        {
            maskGradient = mg;
        }
        else
        {
            return null; // No mask source we can handle.
        }

        var boxW = box.Frame.Width;
        var boxH = box.Frame.Height;
        var nativeMaskW = maskImage?.Width ?? boxW;
        var nativeMaskH = maskImage?.Height ?? boxH;
        var (renderW, renderH) = ResolveBackgroundSize(style.Get(PropertyId.MaskSize), boxW, boxH, nativeMaskW, nativeMaskH);
        if (renderW <= 0 || renderH <= 0) return null;

        var (offsetX, offsetY) = ResolveBackgroundPosition(style.Get(PropertyId.MaskPosition), boxW, boxH, renderW, renderH);
        var repeat = ResolveMaskRepeat(style.Get(PropertyId.MaskRepeat));
        var mode = ResolveMaskMode(style.Get(PropertyId.MaskMode));
        var radii = ReadCornerRadii(style, boxW, boxH);
        var bounds = new Rect(frameX, frameY, boxW, boxH);

        return new MaskGeometry(bounds, radii, maskImage, maskGradient, renderW, renderH, offsetX, offsetY, repeat, mode);
    }

    /// <summary>
    /// Wraps the accumulated <paramref name="innerList"/> items in a
    /// <see cref="PushMask"/> / <see cref="PopMask"/> bracket and appends them
    /// to <paramref name="list"/>. Called after all box-interior and child items
    /// have been collected.
    /// </summary>
    private static void FlushMaskBracket(DisplayList list, DisplayList innerList, MaskGeometry? geo)
    {
        if (geo is null) return;
        list.Add(new PushMask(
            geo.Bounds, geo.Radii,
            geo.Mask, geo.MaskGradient,
            geo.RenderW, geo.RenderH,
            geo.OffsetX, geo.OffsetY,
            geo.Repeat, geo.Mode));
        foreach (var item in innerList.Items)
            list.Add(item);
        list.Add(PopMask.Instance);
    }

    /// <summary>
    /// Resolves <c>mask-mode</c> (CSS Masking 1 §6.1). Defaults to
    /// <see cref="MaskModeKind.MatchSource"/> when unset or unrecognised.
    /// </summary>
    private static MaskModeKind ResolveMaskMode(CssValue? value)
        => value switch
        {
            CssKeyword { Name: "luminance" } => MaskModeKind.Luminance,
            CssKeyword { Name: "alpha" } => MaskModeKind.Alpha,
            _ => MaskModeKind.MatchSource,
        };

    /// <summary>
    /// Resolves <c>mask-repeat</c> (CSS Masking 1 §6.5). Handles the single-keyword
    /// and two-keyword forms; the two-keyword form uses the X-axis keyword to set
    /// the dominant mode (space/round on X side wins).
    /// </summary>
    private static MaskRepeatMode ResolveMaskRepeat(CssValue? value)
    {
        // Extract the first (x-axis) keyword from a two-value list.
        CssKeyword? xKey = value switch
        {
            CssValueList { Values: var vs } => vs.Count > 0 ? vs[0] as CssKeyword : null,
            CssKeyword k => k,
            _ => null,
        };
        // Also check if the single keyword or x-keyword is "no-repeat" – but a
        // list value can have a no-repeat on the y-axis only; honour x-axis first.
        CssKeyword? yKey = value switch
        {
            CssValueList { Values: var vs } => vs.Count > 1 ? vs[1] as CssKeyword : null,
            _ => null,
        };
        var xName = xKey?.Name ?? "";
        var yName = yKey?.Name ?? "";

        return xName switch
        {
            "no-repeat" => MaskRepeatMode.NoRepeat,
            "space" => MaskRepeatMode.Space,
            "round" => MaskRepeatMode.Round,
            "repeat-x" => MaskRepeatMode.RepeatX,
            "repeat-y" => MaskRepeatMode.RepeatY,
            "repeat" when yName is "no-repeat" => MaskRepeatMode.RepeatX,
            _ when yName is "no-repeat" => MaskRepeatMode.RepeatY,
            _ => MaskRepeatMode.Repeat,
        };
    }

    /// <summary>
    /// Reads <c>PropertyId.Transform</c> off the box's effective style and
    /// composes it with the (centre-of-box) transform-origin into a single
    /// document-space matrix. Returns <c>null</c> when no transform applies
    /// (<c>none</c>, identity, or unparseable). The hardcoded centre origin
    /// is a known limitation. Full <c>transform-origin</c> value parsing is
    /// tracked as separate work.
    /// </summary>
    internal static Matrix2D? TryGetTransformMatrix(Box box, Func<Box, ComputedStyle?>? styleOverride, double frameX, double frameY)
    {
        if (box.Frame.Width <= 0 && box.Frame.Height <= 0) return null;
        var style = EffectiveStyle(box, styleOverride);
        if (style is null) return null;
        var raw = style.Get(PropertyId.Transform);
        if (raw is null or CssKeyword { Name: "none" }) return null;

        var transform = CssTransformParser.Parse(raw);
        if (transform.IsNone) return null;

        var local = transform.ToMatrix(box.Frame.Width, box.Frame.Height);
        if (local.IsIdentity) return null;

        var originX = frameX + box.Frame.Width * 0.5;
        var originY = frameY + box.Frame.Height * 0.5;

        // final = T(+origin) × local × T(-origin)
        var preOrigin = Matrix2D.Translate(-originX, -originY);
        var postOrigin = Matrix2D.Translate(originX, originY);
        return postOrigin.Multiply(local).Multiply(preOrigin);
    }

    /// <summary>
    /// Reads the four <c>border-*-radius</c> longhands off the style, resolves
    /// each to CSS px against the box dimensions, and clamps them so adjacent
    /// radii never overlap (CSS Backgrounds 3 §5.1: if the sum of two radii on a
    /// side exceeds that side's length, all radii are scaled down by the same
    /// factor). Returns <see cref="CornerRadii.None"/> when every corner is
    /// square.
    /// </summary>
    private static CornerRadii ReadCornerRadii(ComputedStyle style, double width, double height)
    {
        var tl = ResolveRadius(style.Get(PropertyId.BorderTopLeftRadius), width, height);
        var tr = ResolveRadius(style.Get(PropertyId.BorderTopRightRadius), width, height);
        var br = ResolveRadius(style.Get(PropertyId.BorderBottomRightRadius), width, height);
        var bl = ResolveRadius(style.Get(PropertyId.BorderBottomLeftRadius), width, height);
        if (tl <= 0 && tr <= 0 && br <= 0 && bl <= 0) return CornerRadii.None;

        // §5.1 overlap clamp — scale every corner by the smallest side ratio.
        var scale = 1d;
        if (tl + tr > 0) scale = Math.Min(scale, SafeRatio(width, tl + tr));
        if (bl + br > 0) scale = Math.Min(scale, SafeRatio(width, bl + br));
        if (tl + bl > 0) scale = Math.Min(scale, SafeRatio(height, tl + bl));
        if (tr + br > 0) scale = Math.Min(scale, SafeRatio(height, tr + br));
        if (scale < 1d)
        {
            tl *= scale; tr *= scale; br *= scale; bl *= scale;
        }

        return CornerRadii.Uniform(tl, tr, br, bl);
    }

    private static double SafeRatio(double available, double requested)
        => requested <= 0 ? 1d : Math.Min(1d, available / requested);

    private static double ResolveRadius(CssValue? value, double width, double height)
        => value switch
        {
            CssLength len => Math.Max(0, Starling.Layout.Block.BlockLayout.ToPx(len)),
            // §5: a percentage radius is relative to the box's width for the
            // horizontal component; we use the smaller dimension as a single
            // circular approximation (rx == ry) which matches the common case.
            CssPercentage pct => Math.Max(0, Math.Min(width, height) * pct.Value / 100d),
            CssNumber n => Math.Max(0, n.Value),
            CssValueList list when list.Values.Count > 0 => ResolveRadius(list.Values[0], width, height),
            _ => 0,
        };

    private static CornerRadii ShrinkRadii(CornerRadii r, double by)
    {
        static double S(double v, double by) => Math.Max(0, v - by);
        return new CornerRadii(
            S(r.TopLeftX, by), S(r.TopLeftY, by),
            S(r.TopRightX, by), S(r.TopRightY, by),
            S(r.BottomRightX, by), S(r.BottomRightY, by),
            S(r.BottomLeftX, by), S(r.BottomLeftY, by));
    }

    /// <summary>
    /// Emits the outer <c>box-shadow</c> drop shadows behind the box. Per CSS
    /// Backgrounds 3 §6 the first listed layer is on top, so the layers are
    /// emitted back-to-front (last → first). Inset layers are recognised but
    /// only their parse is honoured here; inner-shadow painting is a documented
    /// follow-up.
    /// </summary>
    private static void EmitBoxShadows(Box box, Rect bounds, CornerRadii radii, DisplayList list, Matrix2D current, Rect? cull, ComputedStyle style)
    {
        var raw = style.Get(PropertyId.BoxShadow);
        if (raw is null or CssKeyword { Name: "none" }) return;
        var shadow = CssBoxShadowParser.Parse(raw);
        if (shadow.IsNone) return;

        var textColor = style.GetColor(PropertyId.Color);

        for (var i = shadow.Layers.Count - 1; i >= 0; i--)
        {
            var layer = shadow.Layers[i];
            if (layer.Inset) continue; // outer shadows only (inset deferred)

            var color = layer.Color ?? textColor;
            if (color.A == 0) continue;

            var offsetX = Starling.Layout.Block.BlockLayout.ToPx(layer.OffsetX);
            var offsetY = Starling.Layout.Block.BlockLayout.ToPx(layer.OffsetY);
            var blur = Math.Max(0, Starling.Layout.Block.BlockLayout.ToPx(layer.Blur));
            var spread = Starling.Layout.Block.BlockLayout.ToPx(layer.Spread);

            // Shadow AABB for culling: box grown by spread + blur, offset.
            var pad = spread + blur;
            var shadowAabb = new Rect(
                bounds.X + offsetX - pad,
                bounds.Y + offsetY - pad,
                bounds.Width + 2 * pad,
                bounds.Height + 2 * pad);

            Emit(list, new DrawBoxShadow(bounds, radii, offsetX, offsetY, blur, spread, color, layer.Inset), shadowAabb, current, cull);
        }
    }

    private static void EmitBorders(Box box, double x, double y, DisplayList list, Matrix2D current, Rect? cull, ComputedStyle style, CornerRadii radii)
    {
        var top = box.Border.Top;
        var right = box.Border.Right;
        var bottom = box.Border.Bottom;
        var left = box.Border.Left;
        if (top + right + bottom + left == 0) return;

        var topColor = style.GetColor(PropertyId.BorderTopColor);
        var rightColor = style.GetColor(PropertyId.BorderRightColor);
        var bottomColor = style.GetColor(PropertyId.BorderBottomColor);
        var leftColor = style.GetColor(PropertyId.BorderLeftColor);

        // Rounded uniform border: when every side shares the same width and
        // color (the overwhelmingly common authoring case) and the box has
        // rounded corners, paint the border as a single rounded ring — fill the
        // outer rounded border-box, then knock out the inner (padding-box)
        // rounded rect by filling it back to the background-less hole. We can't
        // composite a hole here, so instead paint the ring as the difference of
        // two rounded fills layered with the background already drawn beneath:
        // fill the outer border-box rounded rect with the border color BEFORE
        // the background would be insufficient. Simpler + correct for the solid
        // case: stroke the centre-line rounded rect with a pen of the border
        // width. Per-side mixed (different widths/colors) rounded borders fall
        // back to the square strokes below — a documented first-cut gap.
        if (!radii.IsZero
            && top > 0 && top == right && right == bottom && bottom == left
            && topColor.A > 0
            && topColor == rightColor && rightColor == bottomColor && bottomColor == leftColor)
        {
            // Centre-line rounded rect: inset by half the border width so a
            // pen of `top` width straddles the border-box edge symmetrically.
            var half = top / 2d;
            var inner = new Rect(x + half, y + half, box.Frame.Width - top, box.Frame.Height - top);
            var innerRadii = ShrinkRadii(radii, half);
            var ringBounds = new Rect(x, y, box.Frame.Width, box.Frame.Height);
            Emit(list, new StrokeRoundedRect(inner, innerRadii, topColor, top), ringBounds, current, cull);
            return;
        }

        {
            var b = new Rect(x, y, box.Frame.Width, top);
            Emit(list, new FillRect(b, topColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
        if (right > 0 && rightColor.A > 0)
        {
            var b = new Rect(x + box.Frame.Width - right, y, right, box.Frame.Height);
            Emit(list, new FillRect(b, rightColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
        if (bottom > 0 && bottomColor.A > 0)
        {
            var b = new Rect(x, y + box.Frame.Height - bottom, box.Frame.Width, bottom);
            Emit(list, new FillRect(b, bottomColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
        if (left > 0 && leftColor.A > 0)
        {
            var b = new Rect(x, y, left, box.Frame.Height);
            Emit(list, new FillRect(b, leftColor, FillRectPixelAlignment.Preserve), b, current, cull);
        }
    }

    private static void EmitTextFragments(TextBox text, double x, double y, DisplayList list, Matrix2D current, Rect? cull, Func<Box, ComputedStyle?>? styleOverride)
    {
        if (text.Fragments.Count == 0) return;
        var style = EffectiveStyle(text, styleOverride);
        var color = style?.GetColor(PropertyId.Color) ?? CssColor.Black;
        var fontSize = style?.Get(PropertyId.FontSize) switch
        {
            CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
            _ => 16d,
        };
        var spec = FontSpec.FromStyle(style);

        // CSS Text Decoration 3 — resolve the decoration + shadow style once per
        // text box; both apply uniformly to every fragment in the run.
        var lines = ResolveDecorationLines(style);
        var decorationStyle = ResolveDecorationStyle(style);
        var decorationColor = ResolveDecorationColor(style, color);
        var thickness = ResolveDecorationThickness(style, fontSize);
        var underlineOffset = ResolveUnderlineOffset(style, fontSize);
        var shadow = ResolveTextShadow(style);

        foreach (var frag in text.Fragments)
        {
            if (frag.Text.Length == 0 || string.IsNullOrWhiteSpace(frag.Text)) continue;
            var baselineY = y + frag.Y + frag.Baseline;
            // Cull bounds: the glyph run sits on the baseline. Cover ascent
            // (≈ font size above the baseline) and a small descent below so the
            // AABB safely encloses the rasterized glyphs.
            var glyphBounds = new Rect(
                x + frag.X,
                baselineY - fontSize,
                frag.Width,
                fontSize * 1.3d);

            // text-shadow (CSS Text Decoration 3 §5): paint each layer beneath
            // the foreground glyphs, back-to-front. Per spec the first listed
            // layer renders on top, so emit the list in reverse.
            for (var i = shadow.Layers.Count - 1; i >= 0; i--)
            {
                var layer = shadow.Layers[i];
                var layerColor = layer.Color ?? color;
                if (layerColor.A == 0) continue;
                var shadowBounds = new Rect(
                    glyphBounds.X + layer.OffsetX - layer.Blur,
                    glyphBounds.Y + layer.OffsetY - layer.Blur,
                    glyphBounds.Width + 2 * layer.Blur,
                    glyphBounds.Height + 2 * layer.Blur);
                Emit(list, new DrawTextShadow(
                    frag.Text,
                    x + frag.X,
                    y + frag.Y,
                    fontSize,
                    layerColor,
                    layer.OffsetX,
                    layer.OffsetY,
                    layer.Blur,
                    spec.Families,
                    spec.Bold,
                    spec.Italic,
                    frag.Shaped), shadowBounds, current, cull);
            }

            Emit(list, new DrawText(
                frag.Text,
                x + frag.X,
                y + frag.Y,
                fontSize,
                color,
                spec.Families,
                spec.Bold,
                spec.Italic,
                frag.Shaped), glyphBounds, current, cull);

            if (lines != TextDecorationLines.None && frag.Width > 0)
            {
                // Decoration position/thickness depend on real font metrics, so
                // emit a typed primitive carrying the run geometry + style and
                // let the backend resolve exact y-offsets from the resolved
                // face (CSS Text Decoration 3 §2). Cull bounds cover the full
                // glyph box, which encloses all three line positions.
                Emit(list, new DrawTextDecoration(
                    x + frag.X,
                    frag.Width,
                    baselineY,
                    fontSize,
                    decorationColor,
                    lines,
                    decorationStyle,
                    thickness,
                    underlineOffset,
                    spec.Families,
                    spec.Bold,
                    spec.Italic), glyphBounds, current, cull);
            }
        }
    }

    // ---- CSS Text Decoration 3 helpers (wp:M5-css-15) ----

    private static TextDecorationLines ResolveDecorationLines(ComputedStyle? style)
    {
        if (style is null) return TextDecorationLines.None;
        // The `text-decoration` shorthand expands to text-decoration-line; keep
        // the legacy `text-decoration` carrier as a fallback for authors that
        // set the longhand-less shorthand value directly.
        var lines = LinesFromValue(style.Get(PropertyId.TextDecorationLine));
        if (lines == TextDecorationLines.None)
            lines = LinesFromValue(style.Get(PropertyId.TextDecoration));
        return lines;
    }

    private static TextDecorationLines LinesFromValue(CssValue? value)
    {
        var result = TextDecorationLines.None;
        switch (value)
        {
            case CssKeyword k:
                result |= LineFromKeyword(k.Name);
                break;
            case CssValueList list:
                foreach (var item in list.Values.OfType<CssKeyword>())
                    result |= LineFromKeyword(item.Name);
                break;
        }
        return result;
    }

    private static TextDecorationLines LineFromKeyword(string name) => name.ToLowerInvariant() switch
    {
        "underline" => TextDecorationLines.Underline,
        "overline" => TextDecorationLines.Overline,
        "line-through" => TextDecorationLines.LineThrough,
        _ => TextDecorationLines.None,
    };

    private static TextDecorationStyleKind ResolveDecorationStyle(ComputedStyle? style)
        => (style?.Get(PropertyId.TextDecorationStyle) as CssKeyword)?.Name.ToLowerInvariant() switch
        {
            "double" => TextDecorationStyleKind.Double,
            "dotted" => TextDecorationStyleKind.Dotted,
            "dashed" => TextDecorationStyleKind.Dashed,
            "wavy" => TextDecorationStyleKind.Wavy,
            _ => TextDecorationStyleKind.Solid,
        };

    private static CssColor ResolveDecorationColor(ComputedStyle? style, CssColor currentColor)
    {
        // text-decoration-color defaults to currentColor; only an explicit typed
        // color overrides it (the keyword `currentColor` stays unresolved).
        if (style?.Get(PropertyId.TextDecorationColor) is CssColor c) return c;
        return currentColor;
    }

    private static double ResolveDecorationThickness(ComputedStyle? style, double fontSize)
    {
        switch (style?.Get(PropertyId.TextDecorationThickness))
        {
            case CssLength { Unit: CssLengthUnit.Px } px:
                return Math.Max(0.5, px.Value);
            case CssLength len:
                return Math.Max(0.5, Starling.Layout.Block.BlockLayout.ToPx(len));
            case CssPercentage pct:
                return Math.Max(0.5, fontSize * pct.Value / 100d);
            default:
                // `auto` / `from-font`: ~1px at 16px, scaling with font size; the
                // backend may refine this from the font's underline thickness.
                return 0d; // sentinel: backend derives from font metrics.
        }
    }

    private static double ResolveUnderlineOffset(ComputedStyle? style, double fontSize)
        => style?.Get(PropertyId.TextUnderlineOffset) switch
        {
            CssLength { Unit: CssLengthUnit.Px } px => px.Value,
            CssLength len => Starling.Layout.Block.BlockLayout.ToPx(len),
            CssPercentage pct => fontSize * pct.Value / 100d,
            _ => 0d, // auto
        };

    private static CssTextShadow ResolveTextShadow(ComputedStyle? style)
        => style?.Get(PropertyId.TextShadow) as CssTextShadow ?? CssTextShadow.None;
}
