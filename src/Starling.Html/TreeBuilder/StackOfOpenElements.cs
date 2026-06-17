using Starling.Dom;

namespace Starling.Html.TreeBuilder;

/// <summary>
/// Stack of open elements per [HTML §13.2.4.2](https://html.spec.whatwg.org/multipage/parsing.html#the-stack-of-open-elements).
/// Scope queries follow the spec's namespace-aware rules: a target matches an
/// HTML-namespace element with the given local name, and the scope-terminator
/// sets include the MathML/SVG integration elements.
/// </summary>
internal sealed class StackOfOpenElements
{
    private const string Html = Element.HtmlNamespace;
    private const string MathMl = "http://www.w3.org/1998/Math/MathML";
    private const string Svg = "http://www.w3.org/2000/svg";

    private readonly List<Element> _items = [];

    public int Count => _items.Count;
    public Element this[int i] => _items[i];

    /// <summary>The "current node" is the bottom of the stack (the most recently
    /// opened element).</summary>
    public Element Current => _items[^1];

    public bool IsEmpty => _items.Count == 0;

    public void Push(Element e) => _items.Add(e);

    public Element Pop()
    {
        var top = _items[^1];
        _items.RemoveAt(_items.Count - 1);
        return top;
    }

    public bool Contains(Element e) => _items.Contains(e);

    public bool ContainsNamed(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (IsHtmlNamed(_items[i], localName)) return true;
        return false;
    }

    public int IndexOf(Element e) => _items.IndexOf(e);

    public void Remove(Element e) => _items.Remove(e);

    public void InsertAt(int index, Element e) => _items.Insert(index, e);

    public void ReplaceAt(int index, Element e) => _items[index] = e;

    /// <summary>Pop elements off until — and including — an HTML element with the
    /// given local name.</summary>
    public void PopUntilNamed(string localName)
    {
        while (_items.Count > 0)
        {
            var popped = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            if (IsHtmlNamed(popped, localName)) return;
        }
    }

    /// <summary>Pop until — and including — one of the given HTML local names.</summary>
    public void PopUntilOneOf(params string[] localNames)
    {
        while (_items.Count > 0)
        {
            var popped = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            foreach (var n in localNames)
                if (IsHtmlNamed(popped, n)) return;
        }
    }

    /// <summary>Pop until — and including — the given element instance.</summary>
    public void PopUntilElement(Element target)
    {
        while (_items.Count > 0)
        {
            var popped = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            if (popped == target) return;
        }
    }

    public Element? FindByName(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (IsHtmlNamed(_items[i], localName)) return _items[i];
        return null;
    }

    // ---- scope checks (§13.2.4.2) -----------------------------------------

    private static readonly string[] DefaultScope =
    [
        "applet", "caption", "html", "table", "td", "th", "marquee", "object", "template",
    ];

    public bool HasInScope(string localName) => HasInScope(localName, DefaultScope);

    public bool HasInScope(Element target) => HasInScope(target, DefaultScope);

    public bool HasInButtonScope(string localName)
        => HasInScope(localName, DefaultScope, extra: "button");

    public bool HasInListItemScope(string localName)
        => HasInScope(localName, DefaultScope, extra: "ol", extra2: "ul");

    public bool HasInTableScope(string localName)
        => HasInScope(localName, ["html", "table", "template"], includeForeign: false);

    /// <summary>§13.2.4.2 "has an element in select scope" — inverted: every
    /// element except optgroup/option is a terminator.</summary>
    public bool HasInSelectScope(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var node = _items[i];
            if (IsHtmlNamed(node, localName)) return true;
            if (node.Namespace == Html && node.LocalName is "optgroup" or "option") continue;
            return false;
        }
        return false;
    }

    private bool HasInScope(string localName, string[] list, string? extra = null, string? extra2 = null, bool includeForeign = true)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var node = _items[i];
            if (IsHtmlNamed(node, localName)) return true;
            if (IsScopeTerminator(node, list, extra, extra2, includeForeign)) return false;
        }
        return false;
    }

    private bool HasInScope(Element target, string[] list)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var node = _items[i];
            if (node == target) return true;
            if (IsScopeTerminator(node, list, null, null, includeForeign: true)) return false;
        }
        return false;
    }

    private static bool IsScopeTerminator(Element node, string[] list, string? extra, string? extra2, bool includeForeign)
    {
        if (node.Namespace == Html)
        {
            foreach (var n in list)
                if (node.LocalName == n) return true;
            if (extra is not null && node.LocalName == extra) return true;
            if (extra2 is not null && node.LocalName == extra2) return true;
            return false;
        }
        // The MathML/SVG integration elements terminate the default/button/list-item
        // scopes, but NOT table scope (html/table/template only).
        if (!includeForeign) return false;
        if (node.Namespace == MathMl)
            return node.LocalName is "mi" or "mo" or "mn" or "ms" or "mtext" or "annotation-xml";
        if (node.Namespace == Svg)
            return node.LocalName is "foreignObject" or "desc" or "title";
        return false;
    }

    private static bool IsHtmlNamed(Element e, string localName)
        => e.Namespace == Html && string.Equals(e.LocalName, localName, StringComparison.Ordinal);
}
