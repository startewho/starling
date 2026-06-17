using Starling.Dom;

namespace Starling.Html.TreeBuilder;

/// <summary>
/// The list of active formatting elements per
/// [HTML §13.2.4.3](https://html.spec.whatwg.org/multipage/parsing.html#the-list-of-active-formatting-elements).
/// Entries are either elements or "markers". The list drives the reconstruction
/// of formatting elements and the adoption agency algorithm.
/// </summary>
internal sealed class ActiveFormattingElements
{
    /// <summary>One entry: a real element, or a marker when <see cref="Element"/> is null.</summary>
    internal readonly struct Entry
    {
        public readonly Element? Element;
        private Entry(Element? element) => Element = element;
        public bool IsMarker => Element is null;
        public static Entry Marker => new(null);
        public static Entry For(Element element) => new(element);
    }

    private readonly List<Entry> _items = [];

    public int Count => _items.Count;
    public Entry this[int i] => _items[i];

    public void AddMarker() => _items.Add(Entry.Marker);

    /// <summary>§13.2.4.3 "push onto the list of active formatting elements",
    /// including the Noah's Ark clause that caps identical entries at three.</summary>
    public void Add(Element element)
    {
        // Noah's Ark: count entries equal to this one back to the last marker (or
        // the start of the list). If there are already 3, drop the earliest.
        var matchCount = 0;
        var earliestMatch = -1;
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].IsMarker) break;
            if (Equal(_items[i].Element!, element))
            {
                matchCount++;
                earliestMatch = i;
            }
        }
        if (matchCount >= 3 && earliestMatch >= 0)
            _items.RemoveAt(earliestMatch);

        _items.Add(Entry.For(element));
    }

    public void AddElementOnly(Element element) => _items.Add(Entry.For(element));

    public void Insert(int index, Element element) => _items.Insert(index, Entry.For(element));

    public void ReplaceAt(int index, Element element) => _items[index] = Entry.For(element);

    public void RemoveAt(int index) => _items.RemoveAt(index);

    public bool Remove(Element element)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Element == element)
            {
                _items.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    public bool Contains(Element element)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].Element == element) return true;
        return false;
    }

    public int IndexOf(Element element)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
            if (_items[i].Element == element) return i;
        return -1;
    }

    /// <summary>§13.2.4.3 "clear the list of active formatting elements up to the
    /// last marker" — pop entries (including the marker) off the end.</summary>
    public void ClearToLastMarker()
    {
        while (_items.Count > 0)
        {
            var last = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            if (last.IsMarker) return;
        }
    }

    /// <summary>The last element with the given local name, scanning back no
    /// further than the most recent marker. Used by formatting end tags and the
    /// adoption agency.</summary>
    public Element? LastBeforeMarker(string localName)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].IsMarker) return null;
            var el = _items[i].Element!;
            if (string.Equals(el.LocalName, localName, StringComparison.Ordinal))
                return el;
        }
        return null;
    }

    private static bool Equal(Element a, Element b)
    {
        if (!string.Equals(a.LocalName, b.LocalName, StringComparison.Ordinal)) return false;
        if (!string.Equals(a.Namespace, b.Namespace, StringComparison.Ordinal)) return false;
        if (a.Attributes.Count != b.Attributes.Count) return false;
        foreach (var attr in a.Attributes)
        {
            var other = b.Attributes.GetNamedItemNS(attr.Namespace, attr.LocalName);
            if (other is null || !string.Equals(other.Value, attr.Value, StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
