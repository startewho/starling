namespace Starling.Layout.Incremental;

/// <summary>
/// The parent-supplied inputs that determine a box's layout — the
/// constraint space a box is laid out against. Together with the box's own
/// (immutable) inputs it forms the reuse key: when a box is not dirty and the
/// incoming <see cref="ConstraintSpace"/> equals the one it was last laid out
/// under, its entire laid-out subtree can be reused unchanged.
/// </summary>
/// <remarks>
/// <para>This is the cache-key half of incremental layout (see the
/// incremental-layout plan §3). It captures exactly the parent-derived values
/// the block formatting pass threads into a child:</para>
/// <list type="bullet">
///   <item><see cref="AvailableInlineSize"/> — the container's content width,
///   which drives width resolution, auto-margin centering, and percentage
///   widths.</item>
///   <item><see cref="AvailableBlockSize"/> — the container's definite content
///   height, the percentage-height basis (CSS 2.1 §10.5). Null when the
///   containing block height is indefinite, in which case percentage heights
///   resolve to auto.</item>
///   <item><see cref="ViewportWidth"/> / <see cref="ViewportHeight"/> — the
///   bases for <c>vw</c>/<c>vh</c> and the initial containing block, constant
///   within a layout but part of the key so a resize invalidates everything.</item>
/// </list>
/// <para>It is a <c>readonly record struct</c>: value equality is the whole
/// point, and the doubles compare exactly because two layouts of the same tree
/// thread identical values down.</para>
/// </remarks>
public readonly record struct ConstraintSpace(
    double AvailableInlineSize,
    double? AvailableBlockSize,
    double ViewportWidth,
    double ViewportHeight);
