namespace Tessera.Css.Selectors;

public sealed record SelectorList(IReadOnlyList<ComplexSelector> Selectors)
{
    public static SelectorList Empty { get; } = new([]);
}

public sealed record ComplexSelector(IReadOnlyList<ComplexSelectorPart> Parts)
{
    public CompoundSelector RightmostCompound => Parts[^1].Compound;

    public Specificity Specificity => SpecificityCalculator.Calculate(this);
}

public sealed record ComplexSelectorPart(
    CompoundSelector Compound,
    SelectorCombinator CombinatorFromPrevious);

public sealed record CompoundSelector(IReadOnlyList<SimpleSelector> SimpleSelectors);

public enum SelectorCombinator
{
    None,
    Descendant,
    Child,
    NextSibling,
    SubsequentSibling,
}

public abstract record SimpleSelector;

public sealed record TypeSelector(string LocalName, string? Namespace = null) : SimpleSelector;

public sealed record UniversalSelector(string? Namespace = null) : SimpleSelector;

public sealed record IdSelector(string Id) : SimpleSelector;

public sealed record ClassSelector(string ClassName) : SimpleSelector;

public sealed record AttributeSelector(
    string Name,
    AttributeOperator Operator,
    string? Value,
    bool CaseInsensitive) : SimpleSelector;

public enum AttributeOperator
{
    Exists,
    Equals,
    Includes,
    DashMatch,
    Prefix,
    Suffix,
    Substring,
}

public sealed record PseudoClassSelector(string Name, object? Argument = null) : SimpleSelector;

public sealed record PseudoElementSelector(string Name) : SimpleSelector;

public readonly record struct Specificity(int Ids, int Classes, int Types) : IComparable<Specificity>
{
    public static Specificity Zero => new(0, 0, 0);

    public int CompareTo(Specificity other)
    {
        var ids = Ids.CompareTo(other.Ids);
        if (ids != 0) return ids;
        var classes = Classes.CompareTo(other.Classes);
        if (classes != 0) return classes;
        return Types.CompareTo(other.Types);
    }

    public static Specificity operator +(Specificity left, Specificity right)
        => new(left.Ids + right.Ids, left.Classes + right.Classes, left.Types + right.Types);
}

public sealed record NthPattern(int A, int B)
{
    public bool Matches(int oneBasedIndex)
    {
        if (A == 0)
            return oneBasedIndex == B;

        var difference = oneBasedIndex - B;
        if (difference % A != 0)
            return false;

        return difference / A >= 0;
    }
}
