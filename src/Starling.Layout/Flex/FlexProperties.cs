namespace Starling.Layout.Flex;

/// <summary>
/// Strongly-typed flex container properties resolved from a
/// <see cref="Starling.Css.Cascade.ComputedStyle"/> in <see cref="FlexParser"/>.
/// </summary>
internal enum FlexDirection : byte
{
    Row,
    RowReverse,
    Column,
    ColumnReverse,
}

internal enum FlexWrap : byte
{
    NoWrap,
    Wrap,
    // Honoured as `wrap` — reversing the cross-axis line order is deferred.
    WrapReverse,
}

internal enum JustifyContent : byte
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

internal enum AlignItems : byte
{
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    // First-baseline alignment. Row containers align items on their first
    // text baseline (synthesizing one from the margin-box bottom edge when an
    // item has no text); column containers fall back to flex-start —
    // cross-axis (horizontal) baselines need a writing-mode model this engine
    // doesn't have.
    Baseline,
}

/// <summary>
/// <c>align-content</c> — distribution of flex lines along the cross axis in
/// a multi-line container (CSS Flexbox §8.4). The initial <c>normal</c>
/// behaves as <see cref="Stretch"/> in flex containers.
/// </summary>
internal enum AlignContent : byte
{
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
}

internal readonly record struct FlexContainerProps(
    FlexDirection Direction,
    FlexWrap Wrap,
    JustifyContent Justify,
    AlignItems Align,
    AlignContent ContentAlign,
    double RowGap,
    double ColumnGap)
{
    public bool IsRow => Direction is FlexDirection.Row or FlexDirection.RowReverse;
    public bool IsReverse => Direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;
    public bool IsWrap => Wrap is FlexWrap.Wrap or FlexWrap.WrapReverse;

    /// <summary>The gap between adjacent items along the main axis.</summary>
    public double MainGap => IsRow ? ColumnGap : RowGap;

    /// <summary>The gap between adjacent flex lines (the cross axis).</summary>
    public double CrossGap => IsRow ? RowGap : ColumnGap;
}

/// <summary>
/// Per-child flex item properties. <see cref="Basis"/> is the parsed
/// flex-basis: a non-negative length in px, or <c>null</c> when it resolves
/// to <c>auto</c>. The layout resolves <c>auto</c> against the child's width/
/// height property and falls back to its content size when those are also
/// <c>auto</c>. <see cref="AlignSelf"/> is <c>null</c> for <c>auto</c> (take
/// the container's <c>align-items</c>), else the per-item override
/// (CSS Flexbox §8.3).
/// </summary>
internal readonly record struct FlexItemProps(
    double Grow,
    double Shrink,
    double? Basis,
    int Order,
    AlignItems? AlignSelf);
