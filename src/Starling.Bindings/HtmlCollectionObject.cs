using System.Globalization;
using Starling.Dom;
using Starling.Js.Runtime;

namespace Starling.Bindings;

/// <summary>
/// A live HTMLCollection (DOM §4.2.10) as a legacy platform object: integer
/// indices resolve to items, and supported property names (element ids, plus
/// the name attribute of HTML-namespace elements) resolve to the matching
/// element. Backed by a snapshot function so it reflects the current tree.
/// </summary>
internal sealed class HtmlCollectionObject : JsObject
{
    private readonly JsRealm _realm;
    private readonly Func<IReadOnlyList<Element>> _source;

    public HtmlCollectionObject(JsRealm realm, JsObject? prototype, Func<IReadOnlyList<Element>> source)
        : base(prototype)
    {
        _realm = realm;
        _source = source;
    }

    private IReadOnlyList<Element> Items => _source();

    public int Count => Items.Count;

    public JsValue Item(int index)
    {
        var items = Items;
        return index >= 0 && index < items.Count
            ? JsValue.Object(DomWrappers.Wrap(_realm, items[index])) : JsValue.Null;
    }

    public JsValue NamedItemValue(string name)
        => NamedItem(name) is { } e ? JsValue.Object(DomWrappers.Wrap(_realm, e)) : JsValue.Null;

    public IEnumerable<JsValue> Values()
    {
        foreach (var e in Items) yield return JsValue.Object(DomWrappers.Wrap(_realm, e));
    }

    // An array index is a canonical non-negative integer string (no leading
    // zeros, within range).
    private static bool TryIndex(string name, out int index)
    {
        index = 0;
        if (name.Length == 0) return false;
        if (name.Length > 1 && name[0] == '0') return false;
        if (!ulong.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            return false;
        // WebIDL "array index": an integer in [0, 2^32-2]. 2^32-1 (4294967295)
        // and above are NOT array indices — they fall through to named-property
        // lookup. A value beyond int range is a valid index but always past the
        // end of any real collection, so clamp it to int.MaxValue (=> undefined).
        if (v > 4294967294UL) return false;
        index = v > int.MaxValue ? int.MaxValue : (int)v;
        return true;
    }

    private Element? NamedItem(string name)
    {
        if (name.Length == 0) return null;
        var items = Items;
        foreach (var e in items)
            if (e.GetAttribute("id") == name) return e;
        foreach (var e in items)
            if (e.Namespace == Element.HtmlNamespace && e.GetAttribute("name") == name) return e;
        return null;
    }

    // The supported property names: each element's id, then the name attribute
    // of HTML-namespace elements, in tree order with no duplicates.
    private List<string> SupportedNames()
    {
        // HTML §2.7.2.1 "supported property names": process each element in tree
        // order, appending its id then (for HTML elements) its name — so the two
        // keys of one element stay adjacent — skipping duplicates.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var e in Items)
        {
            var id = e.GetAttribute("id");
            if (!string.IsNullOrEmpty(id) && seen.Add(id)) names.Add(id);
            if (e.Namespace == Element.HtmlNamespace)
            {
                var n = e.GetAttribute("name");
                if (!string.IsNullOrEmpty(n) && seen.Add(n)) names.Add(n);
            }
        }
        return names;
    }

    public override JsValue Get(string name)
    {
        var items = Items;
        if (name == "length") return JsValue.Number(items.Count);
        if (TryIndex(name, out var index))
            return index < items.Count ? JsValue.Object(DomWrappers.Wrap(_realm, items[index])) : JsValue.Undefined;
        // An own/prototype property (item, namedItem, @@iterator) wins over a
        // named element only when no element has that id/name.
        if (NamedItem(name) is { } named && !HasOwnOrProto(name))
            return JsValue.Object(DomWrappers.Wrap(_realm, named));
        return base.Get(name);
    }

    private bool HasOwnOrProto(string name)
    {
        for (var o = (JsObject)this; o is not null; o = o.GetPrototypeOf())
            if (o.HasOwn(name)) return true;
        return false;
    }

    public override bool HasOwn(string name)
    {
        if (name == "length") return true;
        if (TryIndex(name, out var index)) return index < Items.Count;
        if (base.HasOwn(name)) return true;
        return NamedItem(name) is not null;
    }

    public override PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        var items = Items;
        if (name == "length")
            return PropertyDescriptor.Data(JsValue.Number(items.Count), writable: false, enumerable: false, configurable: true);
        if (TryIndex(name, out var index))
            return index < items.Count
                ? PropertyDescriptor.Data(JsValue.Object(DomWrappers.Wrap(_realm, items[index])), writable: true, enumerable: true, configurable: true)
                : null;
        if (base.GetOwnPropertyDescriptor(name) is { } own) return own;
        if (NamedItem(name) is { } named)
            return PropertyDescriptor.Data(JsValue.Object(DomWrappers.Wrap(_realm, named)), writable: true, enumerable: true, configurable: true);
        return null;
    }

    // The own string keys in spec order: array indices, then supported property
    // names, then any expando (ordinary) string keys. `Keys` drives
    // Object.getOwnPropertyNames; OwnPropertyKeys adds the expando symbols.
    public override IEnumerable<string> Keys
    {
        get
        {
            var count = Items.Count;
            for (var i = 0; i < count; i++)
                yield return i.ToString(CultureInfo.InvariantCulture);
            foreach (var n in SupportedNames())
                yield return n;
            foreach (var k in base.Keys)
                yield return k; // expando properties set directly on the collection
        }
    }

    public override IEnumerable<JsPropertyKey> OwnPropertyKeys
    {
        get
        {
            foreach (var k in Keys)
                yield return JsPropertyKey.String(k);
            foreach (var s in SymbolKeys)
                yield return JsPropertyKey.Symbol(s);
        }
    }

    // Legacy platform object: no indexed or named property setter, so a plain
    // assignment to an array index or a supported named property is ignored
    // (in loose mode; strict-mode throwing is handled by the VM's set path).
    // Any other key is an ordinary expando.
    public override void Set(string name, JsValue value)
    {
        if (TryIndex(name, out _)) return;
        if (!base.HasOwn(name) && NamedItem(name) is not null) return;
        base.Set(name, value);
    }

    // Legacy platform object [[Delete]] (WebIDL): an indexed property is never
    // deletable, and HTMLCollection (no [LegacyOverrideBuiltins]) also refuses to
    // delete a supported named property. Expando keys delete normally.
    public override bool Delete(string name)
    {
        if (TryIndex(name, out _)) return false;
        if (!base.HasOwn(name) && NamedItem(name) is not null) return false;
        return base.Delete(name);
    }

    // Legacy platform object [[DefineOwnProperty]] (WebIDL): HTMLCollection has no
    // indexed or named property setter, so defining over an array index, or over a
    // supported named property, fails; any other key is an ordinary expando.
    public override bool DefineOwnProperty(string name, PropertyDescriptor desc)
    {
        if (TryIndex(name, out _)) return false;
        if (!base.HasOwn(name) && NamedItem(name) is not null) return false;
        return base.DefineOwnProperty(name, desc);
    }
}
