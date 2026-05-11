using Tessera.Dom;

namespace Tessera.Css.Selectors;

public sealed class SelectorMatchContext
{
    public static SelectorMatchContext Default { get; } = new();

    public Element? HoveredElement { get; init; }
    public Element? ActiveElement { get; init; }
    public Element? FocusedElement { get; init; }
    public Element? TargetElement { get; init; }
}

public static class SelectorMatcher
{
    public static bool Matches(SelectorList selectorList, Element element, SelectorMatchContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(selectorList);
        ArgumentNullException.ThrowIfNull(element);
        context ??= SelectorMatchContext.Default;
        return selectorList.Selectors.Any(selector => Matches(selector, element, context));
    }

    public static bool Matches(ComplexSelector selector, Element element, SelectorMatchContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(element);
        context ??= SelectorMatchContext.Default;
        return selector.Parts.Count > 0 && MatchesFrom(selector, selector.Parts.Count - 1, element, context);
    }

    private static bool MatchesFrom(
        ComplexSelector selector,
        int partIndex,
        Element element,
        SelectorMatchContext context)
    {
        var part = selector.Parts[partIndex];
        if (!Matches(part.Compound, element, context))
            return false;
        if (partIndex == 0)
            return true;

        return part.CombinatorFromPrevious switch
        {
            SelectorCombinator.Child => ParentElement(element) is { } parent &&
                MatchesFrom(selector, partIndex - 1, parent, context),
            SelectorCombinator.Descendant => Ancestors(element)
                .Any(ancestor => MatchesFrom(selector, partIndex - 1, ancestor, context)),
            SelectorCombinator.NextSibling => PreviousElementSibling(element) is { } sibling &&
                MatchesFrom(selector, partIndex - 1, sibling, context),
            SelectorCombinator.SubsequentSibling => PreviousElementSiblings(element)
                .Any(sibling => MatchesFrom(selector, partIndex - 1, sibling, context)),
            _ => false,
        };
    }

    public static bool Matches(CompoundSelector selector, Element element, SelectorMatchContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(element);
        context ??= SelectorMatchContext.Default;
        return selector.SimpleSelectors.All(simple => Matches(simple, element, context));
    }

    private static bool Matches(SimpleSelector selector, Element element, SelectorMatchContext context)
        => selector switch
        {
            TypeSelector type => element.LocalName.Equals(type.LocalName, StringComparison.OrdinalIgnoreCase),
            UniversalSelector => true,
            IdSelector id => element.Id.Equals(id.Id, StringComparison.Ordinal),
            ClassSelector @class => element.ClassList.Contains(@class.ClassName),
            AttributeSelector attribute => MatchesAttribute(attribute, element),
            PseudoClassSelector pseudo => MatchesPseudoClass(pseudo, element, context),
            PseudoElementSelector => false,
            _ => false,
        };

    private static bool MatchesAttribute(AttributeSelector selector, Element element)
    {
        var actual = element.GetAttribute(selector.Name);
        if (actual is null)
            return false;
        if (selector.Operator == AttributeOperator.Exists)
            return true;

        var expected = selector.Value ?? string.Empty;
        var comparison = selector.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return selector.Operator switch
        {
            AttributeOperator.Equals => actual.Equals(expected, comparison),
            AttributeOperator.Includes => actual
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(expected, selector.CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            AttributeOperator.DashMatch => actual.Equals(expected, comparison) ||
                actual.StartsWith(expected + "-", comparison),
            AttributeOperator.Prefix => actual.StartsWith(expected, comparison),
            AttributeOperator.Suffix => actual.EndsWith(expected, comparison),
            AttributeOperator.Substring => actual.Contains(expected, comparison),
            _ => false,
        };
    }

    private static bool MatchesPseudoClass(
        PseudoClassSelector selector,
        Element element,
        SelectorMatchContext context)
        => selector.Name switch
        {
            "root" => element.OwnerDocument?.DocumentElement == element,
            "empty" => IsEmpty(element),
            "first-child" => PreviousElementSibling(element) is null,
            "last-child" => NextElementSibling(element) is null,
            "only-child" => PreviousElementSibling(element) is null && NextElementSibling(element) is null,
            "first-of-type" => PreviousElementSiblings(element).All(s => !SameType(s, element)),
            "last-of-type" => NextElementSiblings(element).All(s => !SameType(s, element)),
            "only-of-type" => PreviousElementSiblings(element).All(s => !SameType(s, element)) &&
                NextElementSiblings(element).All(s => !SameType(s, element)),
            "nth-child" => selector.Argument is NthPattern nth && nth.Matches(ElementIndex(element, ofType: false, fromEnd: false)),
            "nth-last-child" => selector.Argument is NthPattern nth && nth.Matches(ElementIndex(element, ofType: false, fromEnd: true)),
            "nth-of-type" => selector.Argument is NthPattern nth && nth.Matches(ElementIndex(element, ofType: true, fromEnd: false)),
            "nth-last-of-type" => selector.Argument is NthPattern nth && nth.Matches(ElementIndex(element, ofType: true, fromEnd: true)),
            "is" or "where" => selector.Argument is SelectorList list && Matches(list, element, context),
            "not" => selector.Argument is SelectorList list && !Matches(list, element, context),
            "has" => false,
            "lang" => selector.Argument is string lang && MatchesLanguage(element, lang),
            "hover" => context.HoveredElement == element,
            "active" => context.ActiveElement == element,
            "focus" or "focus-visible" => context.FocusedElement == element,
            "focus-within" => context.FocusedElement is not null &&
                (context.FocusedElement == element || Ancestors(context.FocusedElement).Contains(element)),
            "target" => context.TargetElement == element,
            "checked" => element.HasAttribute("checked") || element.HasAttribute("selected"),
            "disabled" => element.HasAttribute("disabled"),
            "enabled" => !element.HasAttribute("disabled"),
            "required" => element.HasAttribute("required"),
            "optional" => !element.HasAttribute("required"),
            "placeholder-shown" => element.HasAttribute("placeholder") &&
                string.IsNullOrEmpty(element.GetAttribute("value")),
            _ => false,
        };

    private static bool IsEmpty(Element element)
        => element.ChildNodes.All(child => child switch
        {
            Element => false,
            Text text => text.Data.Length == 0,
            CData cdata => cdata.Data.Length == 0,
            _ => true,
        });

    private static bool MatchesLanguage(Element element, string language)
    {
        for (Element? current = element; current is not null; current = ParentElement(current))
        {
            var actual = current.GetAttribute("lang");
            if (actual is null)
                continue;
            return actual.Equals(language, StringComparison.OrdinalIgnoreCase) ||
                actual.StartsWith(language + "-", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static int ElementIndex(Element element, bool ofType, bool fromEnd)
    {
        var siblings = fromEnd ? ElementSiblingsFromEnd(element) : ElementSiblingsFromStart(element);
        var index = 0;
        foreach (var sibling in siblings)
        {
            if (!ofType || SameType(sibling, element))
                index++;
            if (sibling == element)
                return index;
        }

        return -1;
    }

    private static IEnumerable<Element> ElementSiblingsFromStart(Element element)
        => element.ParentNode?.ChildNodes.OfType<Element>() ?? [];

    private static IEnumerable<Element> ElementSiblingsFromEnd(Element element)
        => ElementSiblingsFromStart(element).Reverse();

    private static bool SameType(Element left, Element right)
        => left.Namespace == right.Namespace &&
           left.LocalName.Equals(right.LocalName, StringComparison.OrdinalIgnoreCase);

    private static Element? ParentElement(Element element) => element.ParentNode as Element;

    private static IEnumerable<Element> Ancestors(Element element)
    {
        for (var current = ParentElement(element); current is not null; current = ParentElement(current))
            yield return current;
    }

    private static Element? PreviousElementSibling(Element element)
    {
        for (var sibling = element.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
            if (sibling is Element siblingElement)
                return siblingElement;
        return null;
    }

    private static Element? NextElementSibling(Element element)
    {
        for (var sibling = element.NextSibling; sibling is not null; sibling = sibling.NextSibling)
            if (sibling is Element siblingElement)
                return siblingElement;
        return null;
    }

    private static IEnumerable<Element> PreviousElementSiblings(Element element)
    {
        for (var sibling = PreviousElementSibling(element); sibling is not null; sibling = PreviousElementSibling(sibling))
            yield return sibling;
    }

    private static IEnumerable<Element> NextElementSiblings(Element element)
    {
        for (var sibling = NextElementSibling(element); sibling is not null; sibling = NextElementSibling(sibling))
            yield return sibling;
    }
}
