using System.Collections.Concurrent;
using Starling.Css.Selectors;
using Starling.Dom;

namespace Starling.Css.Cascade;

/// <summary>
/// Per-tree-pass memoization for <see cref="StyleEngine.Compute(Element)"/>.
/// The cascade entry point recurses up the ancestor chain to compute each
/// parent's <see cref="ComputedStyle"/>; for a tree of N elements at depth
/// D that's O(N*D) work and the ancestor styles get recomputed N times.
/// Threading one of these into the tree-traversal call sites (e.g. the
/// box-tree builder) collapses repeated ancestor cascades to a single
/// computation per element per pass.
/// </summary>
/// <remarks>
/// Reference equality on <see cref="Element"/> is fine: <see cref="Element"/>
/// is a class and the DOM keeps a single instance per node within a pass.
/// The cache is only valid while the DOM and stylesheets are stable, so
/// callers should construct a fresh cache for each layout pass.
///
/// The cache deliberately does not handle the interactive
/// <see cref="SelectorMatchContext"/> path — when a non-null context is
/// passed to <see cref="StyleEngine.Compute(Element, SelectorMatchContext?, CascadeCache?)"/>
/// the engine bypasses the cache so pseudo-class state (<c>:hover</c>,
/// <c>:focus</c>, <c>:active</c>) is always honoured.
/// </remarks>
public sealed class CascadeCache
{
    // ConcurrentDictionary so the cache survives the parallel
    // per-element BFS in StyleEngine.PrecomputeTree without locks.
    // Single-threaded callers (the box-tree-build walk) pay a tiny
    // amount of overhead per lookup but stay correct without any
    // separate code path.
    private readonly ConcurrentDictionary<Element, ComputedStyle> _styles
        = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<SharingKey, SharedStyleEntry> _shared = new();

    public bool TryGet(Element element, out ComputedStyle style)
        => _styles.TryGetValue(element, out style!);

    public void Set(Element element, ComputedStyle style)
        => _styles[element] = style;

    /// <summary>
    /// Style-sharing lookup. A public hit returns the stored style for callers
    /// that know their key is valid. <see cref="StyleEngine"/> uses the
    /// internal entry path below so it can recheck selector match results before
    /// reusing the style.
    /// </summary>
    public bool TryGetShared(in SharingKey key, out ComputedStyle style)
    {
        if (_shared.TryGetValue(key, out var entry))
        {
            style = entry.Style;
            return true;
        }

        style = null!;
        return false;
    }

    public void SetShared(in SharingKey key, ComputedStyle style)
        => _shared[key] = new SharedStyleEntry(style, Validation: null);

    internal bool TryGetSharedEntry(in SharingKey key, out SharedStyleEntry entry)
        => _shared.TryGetValue(key, out entry!);

    internal void SetSharedEntry(in SharingKey key, SharedStyleEntry entry)
        => _shared[key] = entry;

    /// <summary>
    /// Iterates the per-element entries. Provided for diagnostics — call
    /// while no parallel work is running (the snapshot is consistent but
    /// not stable across concurrent writers).
    /// </summary>
    public IEnumerable<KeyValuePair<Element, ComputedStyle>> ElementEntries => _styles;

    public int Count => _styles.Count;

    public int SharedCount => _shared.Count;

    public void Clear()
    {
        _styles.Clear();
        _shared.Clear();
    }
}

/// <summary>
/// Key for the style-sharing cache. Captures every input visible to selector
/// matching for an element: tag, every attribute (covers id, class, plus
/// attribute selectors like <c>input[type=text]</c>), the parent's computed
/// style (for inherited values), and the previous element sibling's tag
/// (handles <c>:first-child</c> via null, and adjacent-sibling selectors that
/// only depend on that tag).
/// <para>
/// The key narrows likely matches. <see cref="StyleEngine"/> also stores the
/// source element's selector match results and rechecks them on the target
/// element before it reuses a shared style.
/// </para>
/// </summary>
public readonly struct SharingKey : IEquatable<SharingKey>
{
    public string LocalName { get; }
    public string AttributeFingerprint { get; }
    public ComputedStyle? ParentStyle { get; }
    public string? PreviousElementSiblingTag { get; }
    private readonly int _hash;

    public SharingKey(
        string localName,
        string attributeFingerprint,
        ComputedStyle? parentStyle,
        string? previousElementSiblingTag)
    {
        LocalName = localName;
        AttributeFingerprint = attributeFingerprint;
        ParentStyle = parentStyle;
        PreviousElementSiblingTag = previousElementSiblingTag;

        var h = new HashCode();
        h.Add(localName, StringComparer.Ordinal);
        h.Add(attributeFingerprint, StringComparer.Ordinal);
        h.Add(parentStyle, ReferenceEqualityComparer.Instance);
        h.Add(previousElementSiblingTag, StringComparer.Ordinal);
        _hash = h.ToHashCode();
    }

    public bool Equals(SharingKey other)
        => string.Equals(LocalName, other.LocalName, StringComparison.Ordinal)
        && string.Equals(AttributeFingerprint, other.AttributeFingerprint, StringComparison.Ordinal)
        && ReferenceEquals(ParentStyle, other.ParentStyle)
        && string.Equals(PreviousElementSiblingTag, other.PreviousElementSiblingTag, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is SharingKey other && Equals(other);
    public override int GetHashCode() => _hash;

    public static bool operator ==(SharingKey left, SharingKey right) => left.Equals(right);
    public static bool operator !=(SharingKey left, SharingKey right) => !left.Equals(right);
}

internal sealed record SharedStyleEntry(
    ComputedStyle Style,
    IReadOnlyList<SelectorValidationResult>? Validation);

internal readonly record struct SelectorValidationResult(
    ComplexSelector Selector,
    IReadOnlyList<RuleCondition> Conditions,
    bool Matched);
