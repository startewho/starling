namespace Starling.Dom;

/// <summary>
/// What kind of layout-relevant change a <see cref="LayoutMutation"/> records.
/// These are exactly the cases the incremental layout engine reconciles:
/// text edits and layout-relevant attribute writes it handles in place, while
/// child insert/remove (a structural change) currently forces a full rebuild.
/// </summary>
public enum LayoutChangeKind : byte
{
    /// <summary>A text node's data changed. The target is the
    /// <see cref="Text"/> node.</summary>
    TextChanged,

    /// <summary>A value change to an attribute the cascade depends on
    /// (<see cref="Document.IsLayoutRelevantAttribute"/>). The target is the
    /// <see cref="Element"/>.</summary>
    LayoutRelevantAttr,

    /// <summary>A child was inserted. The target is the parent
    /// <see cref="Node"/> whose child list changed.</summary>
    ChildInserted,

    /// <summary>A child was removed. The target is the (former) parent
    /// <see cref="Node"/> whose child list changed.</summary>
    ChildRemoved,
}

/// <summary>
/// One entry in a <see cref="Document"/>'s per-frame layout-mutation batch:
/// the node that changed and how. The incremental layout engine drains the
/// batch each frame and reconciles only the affected subtrees.
/// </summary>
public readonly record struct LayoutMutation(Node Target, LayoutChangeKind Kind);
