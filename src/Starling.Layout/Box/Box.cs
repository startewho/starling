using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Layout.Compositor;

namespace Starling.Layout.Box;

public enum BoxKind : byte
{
    BlockContainer,
    Inline,
    Text,
    Replaced,
    AnonymousBlock,
}

/// <summary>
/// A box in the layout tree. Frame is in the parent's content-edge coordinate
/// space; the painter walks the tree and applies parent translations.
/// </summary>
public abstract class Box
{
    protected Box(BoxKind kind, ComputedStyle? style, Element? element)
    {
        Kind = kind;
        Style = style;
        Element = element;
    }

    public BoxKind Kind { get; }
    public ComputedStyle? Style { get; }
    public Element? Element { get; }
    public Box? Parent { get; internal set; }
    public List<Box> Children { get; } = [];

    public Rect Frame { get; internal set; }

    /// <summary>
    /// Why this box is a compositor layer candidate, populated during the
    /// box-tree build pass by <see cref="StackingContextResolver"/>. Pure
    /// metadata: it does not affect layout or paint in this work package.
    /// </summary>
    public LayerHint Hints { get; internal set; }

    public Edges Margin { get; internal set; }
    public Edges Padding { get; internal set; }
    public Edges Border { get; internal set; }

    // ---- Incremental-layout cache (see Starling.Layout.Incremental) ----------
    //
    // These back constraint-space-keyed subtree reuse. They are written only on
    // the incremental path; the full-rebuild path never reads them, so a box
    // laid out the old way is byte-for-byte identical to before.

    /// <summary>The constraint space this box was last laid out against, or
    /// null if it has never been laid out under the incremental engine. Reuse
    /// requires this to equal the incoming constraint space.</summary>
    internal Starling.Layout.Incremental.ConstraintSpace? LaidConstraint;

    /// <summary>True when this box or any descendant was touched by a mutation
    /// since its last layout — set during reconciliation along the root-to-change
    /// path. A subtree-dirty box cannot be reused; a clean one (with a matching
    /// constraint space) is repositioned in O(1) without descending, because
    /// every child <see cref="Frame"/> is parent-relative.</summary>
    internal bool SubtreeDirty;

    public void AppendChild(Box child)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent = this;
        Children.Add(child);
    }
}

public sealed class BlockBox : Box
{
    public BlockBox(ComputedStyle? style, Element? element)
        : base(BoxKind.BlockContainer, style, element) { }
}

public sealed class AnonymousBlockBox : Box
{
    public AnonymousBlockBox(ComputedStyle? parentStyle) : base(BoxKind.AnonymousBlock, parentStyle, element: null) { }
}

public sealed class InlineBox : Box
{
    public InlineBox(ComputedStyle? style, Element? element) : base(BoxKind.Inline, style, element) { }
}

public sealed class TextBox : Box
{
    public TextBox(string text, ComputedStyle? style) : base(BoxKind.Text, style, element: null)
    {
        Text = text;
    }

    /// <summary>The run's text. Settable so incremental layout can refresh a
    /// changed text node in place (the box structure is unchanged by a text
    /// edit); the inline pass re-shapes from the new value.</summary>
    public string Text { get; internal set; }

    /// <summary>
    /// Populated by the inline formatting context: one entry per line fragment
    /// drawn from this text run. Painter consumes this list.
    /// </summary>
    public List<TextFragment> Fragments { get; } = [];
}

/// <summary>
/// A replaced inline element with intrinsic dimensions (currently &lt;img&gt;).
/// The <see cref="Source"/> is the backend-neutral decoded pixel buffer; the
/// paint backend blits from it. <see cref="Box.Frame"/> is set by the inline
/// formatting context (for inline images) or block layout (for block images)
/// to the box's position within its parent's content-box.
/// </summary>
public sealed class ImageBox : Box
{
    public ImageBox(
        ComputedStyle? style,
        Element? element,
        double intrinsicWidth,
        double intrinsicHeight,
        DecodedImage source,
        bool intrinsicSizeIsRatioOnly = false)
        : base(BoxKind.Replaced, style, element)
    {
        IntrinsicWidth = intrinsicWidth;
        IntrinsicHeight = intrinsicHeight;
        Source = source;
        IntrinsicSizeIsRatioOnly = intrinsicSizeIsRatioOnly;
    }

    public double IntrinsicWidth { get; }
    public double IntrinsicHeight { get; }
    public DecodedImage Source { get; }

    /// <summary>
    /// True when the box has an intrinsic aspect ratio but no definite intrinsic
    /// size — e.g. an inline <c>&lt;svg&gt;</c> with a <c>viewBox</c> but no
    /// <c>width</c>/<c>height</c> attribute. Per CSS Images §5.3.1 such a box,
    /// when both <c>width</c> and <c>height</c> compute to <c>auto</c>, fills the
    /// available inline size (and derives the other axis from the ratio) instead
    /// of rendering at the <c>viewBox</c> pixel size. <see cref="IntrinsicWidth"/>
    /// / <see cref="IntrinsicHeight"/> then carry the ratio only.
    /// </summary>
    public bool IntrinsicSizeIsRatioOnly { get; }
}

/// <summary>
/// A single line-aligned fragment of text emitted by the inline formatting
/// context. <see cref="X"/> / <see cref="Y"/> are in the enclosing block's
/// content-area coordinate space. <see cref="Shaped"/>, when non-null, carries
/// the pre-shaped glyph run produced by <see cref="Text.ITextMeasurer.Shape"/>
/// at layout time so the paint backend can draw it without re-shaping.
/// </summary>
public readonly record struct TextFragment(
    string Text,
    double X,
    double Y,
    double Width,
    double Height,
    double Baseline,
    Text.ShapedRun? Shaped = null);
