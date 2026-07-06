using System.Globalization;

namespace Starling.Js.Runtime;

/// <summary>
/// ES2024 §10.4.2 Array exotic object. Dense backing storage with a magic
/// <c>length</c> property that stays in lockstep with the indexed slots, plus
/// a fallback to the ordinary property bag for non-index/non-length keys.
/// </summary>
/// <remarks>
/// <para>The exotic dispatch lives in our overridden
/// GetOwnPropertyDescriptor / DefineOwnProperty / HasOwn / Delete / Keys /
/// OwnPropertyKeys / EnumerableKeys. We also override the virtual
/// <see cref="JsObject.Get(string)"/> / <see cref="JsObject.Set(string, JsValue)"/>
/// pair so intrinsic helpers reading <c>arr.length</c> or <c>arr[3]</c>
/// don't allocate a descriptor struct.</para>
/// </remarks>
public sealed class JsArray : JsObject
{
    private readonly List<JsValue> _items = new();
    private readonly JsRealm _realm;

    /// <summary>Indices whose descriptor deviates from the dense default
    /// (writable+enumerable+configurable data): their AUTHORITATIVE property
    /// lives in the base bag; the dense slot keeps a placeholder so length
    /// math stays untouched. Null on the (overwhelmingly common) all-default
    /// array — every hot path pays a single null check.</summary>
    private HashSet<int>? _exoticIndices;

    /// <summary>§10.4.2.1 — `length` can be made non-writable via
    /// defineProperty; a non-writable length rejects value changes and pins
    /// SetIndex growth.</summary>
    private bool _lengthWritable = true;

    public JsArray(JsRealm realm) : base(realm.ArrayPrototype) { _realm = realm; DisableInlineCache(); }

    /// <summary>Create an empty array whose dense backing is pre-sized to
    /// <paramref name="capacity"/> slots, so a known number of subsequent
    /// <see cref="Push(JsValue)"/> calls grow the list without reallocating.
    /// Length stays 0 until items are added.</summary>
    public JsArray(JsRealm realm, int capacity) : base(realm.ArrayPrototype)
    {
        _realm = realm;
        DisableInlineCache();
        if (capacity > 0)
        {
            _items.Capacity = capacity;
        }
    }

    public JsArray(JsRealm realm, IReadOnlyList<JsValue> items) : base(realm.ArrayPrototype)
    {
        _realm = realm;
        DisableInlineCache();
        _items.AddRange(items);
    }

    /// <summary>Live element count. Driven by <see cref="_items"/>.Count; the
    /// "length" data slot is synthesized on the fly so we never store it twice.</summary>
    public int Length => _items.Count;

    /// <summary>Direct access to the dense slot (for native-side fast paths).
    /// Out-of-range reads return <see cref="JsValue.Undefined"/>.</summary>
    public JsValue this[int index]
    {
        get => (uint)index < (uint)_items.Count ? _items[index] : JsValue.Undefined;
        set => SetIndex(index, value);
    }

    /// <summary>Append without going through descriptor machinery.</summary>
    public void Push(JsValue value) => _items.Add(value);

    /// <summary>§7.2.2 IsArray. Proxies around arrays unwrap recursively and
    /// revoked proxies throw.</summary>
    public static bool IsArray(JsValue v, JsRealm? realm = null)
    {
        if (!v.IsObject)
        {
            return false;
        }

        var obj = v.AsObject;
        while (obj is JsProxy proxy)
        {
            if (proxy.Target is null)
            {
                throw new JsThrow(realm is not null
                    ? realm.NewTypeError("Cannot perform IsArray on a revoked proxy")
                    : JsValue.String("Cannot perform IsArray on a revoked proxy"));
            }

            obj = proxy.Target;
        }
        return obj is JsArray;
    }

    /// <summary>Canonical array index per §6.1.7: a String <c>P</c> such that
    /// <c>ToString(ToUint32(P)) === P</c> and value &lt; 2^32 - 1.</summary>
    public static bool IsArrayIndex(string key, out uint index)
    {
        index = 0;
        if (key.Length == 0)
        {
            return false;
        }
        // No leading zeros (except literal "0").
        if (key[0] == '0' && key.Length > 1)
        {
            return false;
        }
        // Digits-only check.
        for (var i = 0; i < key.Length; i++)
        {
            if (key[i] < '0' || key[i] > '9')
            {
                return false;
            }
        }
        if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        return index < uint.MaxValue;
    }

    public static string IndexToString(uint i) => i.ToString(CultureInfo.InvariantCulture);

    // ---------------- Overridden hot-path accessors ----------------

    public override JsValue Get(string name)
    {
        if (name == "length")
        {
            return JsValue.Number(_items.Count);
        }

        if (IsArrayIndex(name, out var idx) && idx < _items.Count)
        {
            if (_exoticIndices is { } ex && ex.Contains((int)idx))
            {
                return base.Get(name);
            }

            return _items[(int)idx];
        }

        return base.Get(name);
    }

    public override void Set(string name, JsValue value)
    {
        if (name == "length")
        {
            if (!_lengthWritable)
            {
                return; // sloppy write to a non-writable length is ignored
            }

            SetLength(value);
            return;
        }
        if (IsArrayIndex(name, out var idx))
        {
            if (_exoticIndices is { } ex && ex.Contains((int)idx))
            {
                base.Set(name, value);
                return;
            }

            SetIndex((int)idx, value);
            return;
        }
        base.Set(name, value);
    }

    public override bool HasOwn(string name)
    {
        if (name == "length")
        {
            return true;
        }

        if (IsArrayIndex(name, out var idx) && idx < _items.Count)
        {
            return true;
        }

        return base.HasOwn(name);
    }

    public override PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (name == "length")
        {
            return PropertyDescriptor.Data(JsValue.Number(_items.Count), writable: _lengthWritable, enumerable: false, configurable: false);
        }

        if (IsArrayIndex(name, out var idx) && idx < _items.Count)
        {
            if (_exoticIndices is { } ex && ex.Contains((int)idx))
            {
                return base.GetOwnPropertyDescriptor(name);
            }

            return PropertyDescriptor.Data(_items[(int)idx], writable: true, enumerable: true, configurable: true);
        }

        return base.GetOwnPropertyDescriptor(name);
    }

    public override bool DefineOwnProperty(string name, PropertyDescriptor desc)
    {
        if (name == "length")
        {
            if (desc.IsAccessor || desc.Configurable || desc.Enumerable)
            {
                return false;
            }

            // §10.4.2.1 ArraySetLength — a non-writable length rejects any
            // value change; writable:false may still be applied together with
            // (or after) the value.
            var newLenNum = JsValue.ToNumber(desc.Value);
            if (!_lengthWritable && newLenNum != _items.Count)
            {
                return false;
            }

            var ok = SetLengthChecked(desc.Value);
            if (!desc.Writable)
            {
                _lengthWritable = false;
            }

            return ok;
        }
        if (IsArrayIndex(name, out var idx))
        {
            // Defining an index at/above length is a length change — rejected
            // when length is non-writable.
            if (!_lengthWritable && idx >= _items.Count)
            {
                return false;
            }

            var i = (int)idx;
            var isExotic = _exoticIndices is { } ex0 && ex0.Contains(i);
            if (!isExotic && !desc.IsAccessor && desc.Writable && desc.Enumerable && desc.Configurable)
            {
                SetIndex(i, desc.Value);
                return true;
            }

            // Deviating attributes (or an accessor) migrate the element to the
            // base bag, which owns validation from here on.
            if (!isExotic && i < _items.Count)
            {
                // Seed the bag with the current dense descriptor so redefine
                // validation sees the real prior state.
                ForceDefineOwnProperty(name, PropertyDescriptor.Data(_items[i], writable: true, enumerable: true, configurable: true));
            }

            if (!base.DefineOwnProperty(name, desc))
            {
                return false;
            }

            (_exoticIndices ??= new HashSet<int>()).Add(i);
            // Keep the dense placeholder so length spans the index.
            if (i >= _items.Count)
            {
                SetIndex(i, JsValue.Undefined);
            }

            return true;
        }
        return base.DefineOwnProperty(name, desc);
    }

    /// <summary>For an array-index or <c>length</c> string key the partial
    /// define must reach the dense backing (or the magic <c>length</c> setter),
    /// not the base property-bag — so we route it through the array's own
    /// [[DefineOwnProperty]] with a descriptor that merges the caller's
    /// specified fields onto the current slot's attributes. Non-index string
    /// keys and symbol keys defer to the base ordinary-object partial path.</summary>
    internal override bool DefineOwnPropertyPartial(JsPropertyKey key, PropertyDescriptor desc, DescriptorFields present)
    {
        if (!key.IsSymbol)
        {
            var s = key.AsString;
            if (s == "length" || IsArrayIndex(s, out _))
            {
                var cur = GetOwnPropertyDescriptor(s);
                var merged = MergeForExotic(cur, desc, present);
                return DefineOwnProperty(s, merged);
            }
        }
        return base.DefineOwnPropertyPartial(key, desc, present);
    }

    /// <summary>Fold the <paramref name="present"/> fields of <paramref name="desc"/>
    /// onto <paramref name="cur"/> (or onto default false/undefined for a
    /// fresh slot) to produce the resolved descriptor an exotic
    /// [[DefineOwnProperty]] expects. Mirrors the inheritance the base
    /// <see cref="JsObject.DefineOwnPropertyPartial"/> performs but returns
    /// the merged value instead of writing it.</summary>
    private static PropertyDescriptor MergeForExotic(PropertyDescriptor? cur, PropertyDescriptor desc, DescriptorFields present)
    {
        if (cur is null)
        {
            return desc.IsAccessor
                ? PropertyDescriptor.Accessor(
                    present.HasGet ? desc.Getter : null,
                    present.HasSet ? desc.Setter : null,
                    present.HasEnumerable && desc.Enumerable,
                    present.HasConfigurable && desc.Configurable)
                : PropertyDescriptor.Data(
                    present.HasValue ? desc.Value : JsValue.Undefined,
                    present.HasWritable && desc.Writable,
                    present.HasEnumerable && desc.Enumerable,
                    present.HasConfigurable && desc.Configurable);
        }
        var c = cur.Value;
        var enumerable = present.HasEnumerable ? desc.Enumerable : c.Enumerable;
        var configurable = present.HasConfigurable ? desc.Configurable : c.Configurable;
        if (desc.IsAccessor)
        {
            return PropertyDescriptor.Accessor(
                present.HasGet ? desc.Getter : (c.IsAccessor ? c.Getter : null),
                present.HasSet ? desc.Setter : (c.IsAccessor ? c.Setter : null),
                enumerable, configurable);
        }
        var writable = present.HasWritable ? desc.Writable : (!c.IsAccessor && c.Writable);
        var value = present.HasValue ? desc.Value : (c.IsAccessor ? JsValue.Undefined : c.Value);
        return PropertyDescriptor.Data(value, writable, enumerable, configurable);
    }

    public override bool Delete(string name)
    {
        if (name == "length")
        {
            return false; // non-configurable
        }

        if (IsArrayIndex(name, out var idx) && idx < _items.Count)
        {
            if (_exoticIndices is { } ex && ex.Contains((int)idx))
            {
                if (!base.Delete(name))
                {
                    return false; // non-configurable element
                }

                ex.Remove((int)idx);
                _items[(int)idx] = JsValue.Undefined;
                return true;
            }

            // Make the slot a hole (spec: delete leaves the slot absent but
            // doesn't shrink length). We model with Undefined since we don't
            // track sparse holes separately.
            _items[(int)idx] = JsValue.Undefined;
            return true;
        }
        return base.Delete(name);
    }

    public override IEnumerable<string> Keys
    {
        get
        {
            for (var i = 0; i < _items.Count; i++)
            {
                yield return IndexToString((uint)i);
            }

            foreach (var key in base.Keys)
            {
                if (IsArrayIndex(key, out var bi) && bi < _items.Count)
                {
                    continue; // dense loop already yielded it
                }

                yield return key;
            }
        }
    }

    public override IEnumerable<JsPropertyKey> OwnPropertyKeys
    {
        get
        {
            for (var i = 0; i < _items.Count; i++)
            {
                yield return JsPropertyKey.String(IndexToString((uint)i));
            }

            foreach (var key in base.OwnPropertyKeys)
            {
                if (!key.IsSymbol && IsArrayIndex(key.AsString, out var bi) && bi < _items.Count)
                {
                    continue;
                }

                yield return key;
            }
        }
    }

    public override IEnumerable<string> EnumerableKeys()
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (_exoticIndices is { } ex && ex.Contains(i)
                && base.GetOwnPropertyDescriptor(IndexToString((uint)i)) is { Enumerable: false })
            {
                continue;
            }

            yield return IndexToString((uint)i);
        }

        foreach (var key in base.EnumerableKeys())
        {
            if (IsArrayIndex(key, out var bi) && bi < _items.Count)
            {
                continue;
            }

            yield return key;
        }
    }

    // ---------------- Internals ----------------

    private void SetIndex(int index, JsValue value)
    {
        if (index < 0)
        {
            return;
        }

        if (index >= _items.Count)
        {
            // Grow with explicit Undefined holes (we don't track sparseness).
            while (_items.Count < index)
            {
                _items.Add(JsValue.Undefined);
            }

            _items.Add(value);
        }
        else
        {
            _items[index] = value;
        }
    }

    private void SetLength(JsValue value) => _ = SetLengthChecked(value);

    /// <summary>§10.4.2.1 ArraySetLength steps 14-16 — shrinking deletes
    /// elements from high to low and STOPS at the first non-configurable one:
    /// length ends at that index + 1 and the operation reports failure (the
    /// defineProperty caller turns that into a TypeError).</summary>
    private bool SetLengthChecked(JsValue value)
    {
        var n = JsValue.ToNumber(value);
        var nu = (uint)n;
        if (n != nu || double.IsNaN(n) || double.IsInfinity(n))
        {
            throw new JsThrow(_realm.NewRangeError("Invalid array length"));
        }

        var newLen = (int)nu;
        if (newLen < _items.Count)
        {
            // A non-configurable exotic element ≥ newLen clamps the shrink.
            if (_exoticIndices is { } ex && ex.Count > 0)
            {
                var clamp = -1;
                foreach (var i in ex)
                {
                    if (i >= newLen
                        && base.GetOwnPropertyDescriptor(IndexToString((uint)i)) is { Configurable: false })
                    {
                        clamp = Math.Max(clamp, i);
                    }
                }

                if (clamp >= 0)
                {
                    ShrinkTo(clamp + 1);
                    return false;
                }
            }

            ShrinkTo(newLen);
        }
        else
        {
            while (_items.Count < newLen)
            {
                _items.Add(JsValue.Undefined);
            }
        }

        return true;
    }

    private void ShrinkTo(int newLen)
    {
        _items.RemoveRange(newLen, _items.Count - newLen);
        // Walk base own keys for stragglers (exotic elements and mixed-mode
        // fallbacks live in the bag).
        var stragglers = new List<string>();
        foreach (var k in base.Keys)
        {
            if (IsArrayIndex(k, out var i) && i >= newLen)
            {
                stragglers.Add(k);
            }
        }

        foreach (var k in stragglers)
        {
            base.Delete(k);
        }

        _exoticIndices?.RemoveWhere(i => i >= newLen);
    }

    public override string ToString() => "[object Array]";
}
