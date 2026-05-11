namespace Tessera.Css.Selectors;

public static class SpecificityCalculator
{
    public static Specificity Calculate(ComplexSelector selector)
    {
        var result = Specificity.Zero;
        foreach (var part in selector.Parts)
            result += Calculate(part.Compound);
        return result;
    }

    public static Specificity Calculate(CompoundSelector selector)
    {
        var result = Specificity.Zero;
        foreach (var simple in selector.SimpleSelectors)
        {
            result += simple switch
            {
                IdSelector => new Specificity(1, 0, 0),
                ClassSelector or AttributeSelector => new Specificity(0, 1, 0),
                PseudoClassSelector { Name: "where" } => Specificity.Zero,
                PseudoClassSelector { Argument: SelectorList argument }
                    when IsSpecificityReplacingPseudo(simple) => Max(argument),
                PseudoClassSelector => new Specificity(0, 1, 0),
                TypeSelector or PseudoElementSelector => new Specificity(0, 0, 1),
                _ => Specificity.Zero,
            };
        }

        return result;
    }

    private static bool IsSpecificityReplacingPseudo(SimpleSelector selector)
        => selector is PseudoClassSelector { Name: "is" or "not" or "has" };

    private static Specificity Max(SelectorList list)
        => list.Selectors.Count == 0
            ? Specificity.Zero
            : list.Selectors.Select(selector => selector.Specificity).Max();
}
