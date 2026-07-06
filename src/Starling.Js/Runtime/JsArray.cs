using System.Globalization;

namespace Starling.Js.Runtime;

/// <summary>
/// ES2024 §10.4.2 Array exotic object. Dense backing storage for the common
/// case, a virtual <c>length</c> that can exceed the materialized slots, real
/// holes, and a fallback to the ordinary property bag for attribute-deviating
/// or far-sparse elements.
/// </summary>
/// <remarks>
/// <para>The exotic dispatch lives in our overridden
/// GetOwnPropertyDescriptor / DefineOwnProperty / HasOwn / Delete / Keys /
/// OwnPropertyKeys / EnumerableKeys. We also override the virtual
/// <see cref="JsObject.Get(string)"/> / <see cref="JsObject.Set(string, JsValue)"/>
/// pair so intrinsic helpers reading <c>arr.length</c> or <c>arr[3]</c>
/// don't allocate a descriptor struct.</para>
/// <para><b>Storage model:</b> <see cref="_items"/> is the dense prefix.
/// <see cref="_length"/> is authoritative and may exceed the dense count
/// (a virtual all-holes tail — <c>new Array(1e9)</c> materializes nothing).
/// <see cref="_holes"/> marks dense-range indices that are ABSENT (delete,
/// elision). <see cref="_bagIndices"/> marks indices whose authoritative
/// property lives in the base bag: attribute-deviating (accessor,
/// non-writable, …) or far-sparse elements. <see cref="_slow"/> is true when
/// either set is non-empty, so the dense fast path stays one flag check.</para>
/// </remarks>
public sealed class JsArray : JsObject
{
    private readonly List<JsValue> _items = new();
    private readonly JsRealm _realm;
    private uint _length;

    /// <summary>Dense-range indices with NO property (holes). Disjoint from
    /// <see cref="_bagIndices"/>; always &lt; <c>_items.Count</c>.</summary>
    private HashSet<uint>? _holes;

    /// <summary>Indices whose authoritative property lives in the base bag.
    /// Entries &lt; <c>_items.Count</c> keep a dense placeholder slot.</summary>
    private HashSet<uint>? _bagIndices;

    /// <summary>True when <see cref="_holes"/> or <see cref="_bagIndices"/>
    /// is non-empty — the single flag the dense fast paths check.</summary>
    private bool _slow;

    /// <summary>§10.4.2.1 — `length` can be made non-writable via
    /// defineProperty; a non-writable length rejects value changes and pins
    /// index growth.</summary>
    private bool _lengthWritable = true;

    /// <summary>Sparse writes beyond this gap go to the property bag instead
    /// of materializing dense hole placeholders.</summary>
    private const uint MaxDenseGap = 64;

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
        _length = (uint)_items.Count;
    }

    /// <summary>The array length clamped to int for native-side consumers.
    /// See <see cref="LengthLong"/> for the authoritative value.</summary>
    public int Length => _length <= int.MaxValue ? (int)_length : int.MaxValue;

    /// <summary>Authoritative §10.4.2 length (0 … 2^32-1).</summary>
    public long LengthLong => _length;

    /// <summary>Append without going through descriptor machinery. Only valid
    /// while building a dense array (native result builders).</summary>
    public void Push(JsValue value)
    {
        _items.Add(value);
        if (_length < (uint)_items.Count)
        {
            _length = (uint)_items.Count;
        }
    }

    /// <summary>Direct access to the element at <paramref name="index"/>. The
    /// getter falls back to the full [[Get]] (prototype chain included) when
    /// the array is not plainly dense.</summary>
    public JsValue this[int index]
    {
        get
        {
            if (!_slow && (uint)index < (uint)_items.Count)
            {
                return _items[index];
            }

            return index >= 0 ? Get(IndexToString((uint)index)) : JsValue.Undefined;
        }
        set
        {
            if (index >= 0)
            {
                SetIndexChecked((uint)index, value);
            }
        }
    }

    /// <summary>Dense fast-path read: true when <paramref name="index"/> hits
    /// a materialized slot on a hole-free, bag-free array.</summary>
    internal bool TryGetDense(long index, out JsValue value)
    {
        if (!_slow && (ulong)index < (ulong)_items.Count)
        {
            value = _items[(int)index];
            return true;
        }

        value = JsValue.Undefined;
        return false;
    }

    /// <summary>Dense fast-path write: in-range overwrite or exact append on
    /// a hole-free, bag-free array. Returns false when the caller must take
    /// the generic (spec-observable) path.</summary>
    internal bool TrySetDense(long index, JsValue value)
    {
        if (_slow || (ulong)index > (ulong)_items.Count)
        {
            return false;
        }

        var i = (int)index;
        if (i < _items.Count)
        {
            _items[i] = value;
            return true;
        }

        if (!Extensible || (!_lengthWritable && (uint)i >= _length))
        {
            return false;
        }

        _items.Add(value);
        if (_length < (uint)_items.Count)
        {
            _length = (uint)_items.Count;
        }

        return true;
    }

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

    private bool IsHole(uint idx) => _holes is { } h && h.Contains(idx);

    private bool IsBag(uint idx) => _bagIndices is { } b && b.Contains(idx);

    private void RecomputeSlow()
        => _slow = _holes is { Count: > 0 } || _bagIndices is { Count: > 0 };

    private void MakeHole(uint idx)
    {
        _items[(int)idx] = JsValue.Undefined;
        (_holes ??= new HashSet<uint>()).Add(idx);
        _slow = true;
    }

    // ---------------- Overridden hot-path accessors ----------------

    public override JsValue Get(string name)
    {
        if (name == "length")
        {
            return JsValue.Number(_length);
        }

        if (IsArrayIndex(name, out var idx))
        {
            if (idx < (uint)_items.Count)
            {
                if (!_slow)
                {
                    return _items[(int)idx];
                }

                if (IsBag(idx))
                {
                    return base.Get(name);
                }

                if (!IsHole(idx))
                {
                    return _items[(int)idx];
                }
            }
            // Hole or beyond the dense prefix — bag miss falls through to the
            // prototype chain via the base walk.
            return base.Get(name);
        }

        return base.Get(name);
    }

    public override void Set(string name, JsValue value)
    {
        if (name == "length")
        {
            SetLengthValue(value);
            return;
        }
        if (IsArrayIndex(name, out var idx))
        {
            if (IsBag(idx))
            {
                base.Set(name, value);
                return;
            }

            SetIndexChecked(idx, value);
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

        if (IsArrayIndex(name, out var idx))
        {
            if (idx < (uint)_items.Count)
            {
                if (!_slow)
                {
                    return true;
                }

                if (IsBag(idx))
                {
                    return base.HasOwn(name);
                }

                return !IsHole(idx);
            }
            return _slow && IsBag(idx) && base.HasOwn(name);
        }

        return base.HasOwn(name);
    }

    public override PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (name == "length")
        {
            return PropertyDescriptor.Data(JsValue.Number(_length), writable: _lengthWritable, enumerable: false, configurable: false);
        }

        if (IsArrayIndex(name, out var idx))
        {
            if (idx < (uint)_items.Count)
            {
                if (!_slow)
                {
                    return PropertyDescriptor.Data(_items[(int)idx], writable: true, enumerable: true, configurable: true);
                }

                if (IsBag(idx))
                {
                    return base.GetOwnPropertyDescriptor(name);
                }

                if (!IsHole(idx))
                {
                    return PropertyDescriptor.Data(_items[(int)idx], writable: true, enumerable: true, configurable: true);
                }

                return null;
            }
            return _slow && IsBag(idx) ? base.GetOwnPropertyDescriptor(name) : null;
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

            // §10.4.2.1 ArraySetLength steps 3-4 — BOTH coercions run (each
            // may hit valueOf) before any validation.
            var newLen = ToUint32Checked(desc.Value);
            if (!_lengthWritable && newLen != _length)
            {
                return false;
            }

            var ok = ApplyLength(newLen);
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
            if (!_lengthWritable && idx >= _length)
            {
                return false;
            }

            var inBag = IsBag(idx);
            if (!inBag && !desc.IsAccessor && desc.Writable && desc.Enumerable && desc.Configurable)
            {
                return SetIndexChecked(idx, desc.Value);
            }

            // Deviating attributes (or an accessor) migrate the element to the
            // base bag, which owns validation from here on.
            if (!inBag && idx < (uint)_items.Count && !IsHole(idx))
            {
                // Seed the bag with the current dense descriptor so redefine
                // validation sees the real prior state.
                ForceDefineOwnProperty(name, PropertyDescriptor.Data(_items[(int)idx], writable: true, enumerable: true, configurable: true));
            }

            if (!base.DefineOwnProperty(name, desc))
            {
                return false;
            }

            if (!inBag)
            {
                (_bagIndices ??= new HashSet<uint>()).Add(idx);
                _slow = true;
                _holes?.Remove(idx);
            }

            if (idx >= _length)
            {
                _length = idx + 1;
            }

            return true;
        }
        return base.DefineOwnProperty(name, desc);
    }

    /// <summary>Write an index slot honoring the exotic invariants: dense
    /// overwrite/append, small-gap growth with real holes, far-sparse bag
    /// storage. Returns false when the write is rejected (non-writable length
    /// growth, non-extensible add).</summary>
    private bool SetIndexChecked(uint idx, JsValue value)
    {
        var count = (uint)_items.Count;
        if (idx < count)
        {
            if (IsHole(idx))
            {
                if (!Extensible)
                {
                    return false;
                }

                _items[(int)idx] = value;
                _holes!.Remove(idx);
                RecomputeSlow();
                return true;
            }

            _items[(int)idx] = value;
            return true;
        }

        // New property — growth/extensibility checks apply.
        if (!Extensible)
        {
            return false;
        }

        if (!_lengthWritable && idx >= _length)
        {
            return false;
        }

        if (idx == count)
        {
            _items.Add(value);
        }
        else if (idx - count <= MaxDenseGap && idx < int.MaxValue)
        {
            var holes = _holes ??= new HashSet<uint>();
            for (var j = count; j < idx; j++)
            {
                _items.Add(JsValue.Undefined);
                holes.Add(j);
            }

            _items.Add(value);
            _slow = true;
        }
        else
        {
            ForceDefineOwnProperty(IndexToString(idx),
                PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true));
            (_bagIndices ??= new HashSet<uint>()).Add(idx);
            _slow = true;
        }

        if (idx >= _length)
        {
            _length = idx + 1;
        }

        return true;
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
            if (s == "length")
            {
                // An attribute-only redefine (no value field) must not re-run
                // the length coercion/validation with the synthesized current
                // value — apply the writable bit directly.
                if (!present.HasValue)
                {
                    if (desc.IsAccessor || (present.HasEnumerable && desc.Enumerable) || (present.HasConfigurable && desc.Configurable))
                    {
                        return false;
                    }

                    if (present.HasWritable)
                    {
                        if (!_lengthWritable && desc.Writable)
                        {
                            return false;
                        }

                        _lengthWritable = desc.Writable;
                    }

                    return true;
                }

                var mergedLen = MergeForExotic(GetOwnPropertyDescriptor(s), desc, present);
                return DefineOwnProperty(s, mergedLen);
            }
            if (IsArrayIndex(s, out _))
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

        if (IsArrayIndex(name, out var idx))
        {
            if (IsBag(idx))
            {
                if (!base.Delete(name))
                {
                    return false; // non-configurable element
                }

                _bagIndices!.Remove(idx);
                if (idx < (uint)_items.Count)
                {
                    MakeHole(idx);
                }

                RecomputeSlow();
                return true;
            }
            if (idx < (uint)_items.Count)
            {
                if (!IsHole(idx))
                {
                    MakeHole(idx);
                }

                return true;
            }
            return true; // absent — delete succeeds
        }
        return base.Delete(name);
    }

    // ---------------- Enumeration ----------------

    /// <summary>Bag indices at/above the dense prefix, ascending — they sort
    /// after every dense index so a simple append preserves spec order.</summary>
    private List<uint>? SortedBagTail()
    {
        if (_bagIndices is not { Count: > 0 } bag)
        {
            return null;
        }

        List<uint>? tail = null;
        var count = (uint)_items.Count;
        foreach (var i in bag)
        {
            if (i >= count)
            {
                (tail ??= new List<uint>()).Add(i);
            }
        }

        tail?.Sort();
        return tail;
    }

    public override IEnumerable<string> Keys
    {
        get
        {
            for (var i = 0u; i < (uint)_items.Count; i++)
            {
                if (_slow && IsHole(i))
                {
                    continue;
                }

                yield return IndexToString(i);
            }

            if (SortedBagTail() is { } tail)
            {
                foreach (var i in tail)
                {
                    yield return IndexToString(i);
                }
            }

            yield return "length";
            foreach (var key in base.Keys)
            {
                if (IsArrayIndex(key, out _))
                {
                    continue; // bag-resident indices already yielded in order
                }

                yield return key;
            }
        }
    }

    public override IEnumerable<JsPropertyKey> OwnPropertyKeys
    {
        get
        {
            foreach (var key in Keys)
            {
                yield return JsPropertyKey.String(key);
            }

            foreach (var key in base.OwnPropertyKeys)
            {
                if (!key.IsSymbol)
                {
                    continue; // string keys already yielded (spec order)
                }

                yield return key;
            }
        }
    }

    public override IEnumerable<string> EnumerableKeys()
    {
        for (var i = 0u; i < (uint)_items.Count; i++)
        {
            if (_slow)
            {
                if (IsHole(i))
                {
                    continue;
                }

                if (IsBag(i)
                    && base.GetOwnPropertyDescriptor(IndexToString(i)) is { Enumerable: false })
                {
                    continue;
                }
            }

            yield return IndexToString(i);
        }

        if (SortedBagTail() is { } tail)
        {
            foreach (var i in tail)
            {
                if (base.GetOwnPropertyDescriptor(IndexToString(i)) is { Enumerable: true })
                {
                    yield return IndexToString(i);
                }
            }
        }

        foreach (var key in base.EnumerableKeys())
        {
            if (IsArrayIndex(key, out _))
            {
                continue;
            }

            yield return key;
        }
    }

    // ---------------- Internals ----------------

    private void SetLengthValue(JsValue value)
    {
        // §10.4.2.1 — the coercion (and its RangeError) runs even when the
        // write will be rejected by a non-writable length.
        var newLen = ToUint32Checked(value);
        if (!_lengthWritable)
        {
            return; // sloppy write to a non-writable length is ignored
        }

        _ = ApplyLength(newLen);
    }

    /// <summary>ArraySetLength steps 3-5: ToUint32(V) and ToNumber(V) are BOTH
    /// evaluated (observable valueOf calls), then compared; a mismatch is a
    /// RangeError.</summary>
    private uint ToUint32Checked(JsValue value)
    {
        var vm = _realm.ActiveVm;
        var prim1 = AbstractOperations.ToPrimitive(vm, value, "number");
        var n1 = JsValue.ToNumber(prim1);
        var nu = ToUint32(n1);
        var prim2 = value.IsObject ? AbstractOperations.ToPrimitive(vm, value, "number") : prim1;
        var n2 = JsValue.ToNumber(prim2);
        if (double.IsNaN(n2) || nu != n2)
        {
            throw new JsThrow(_realm.NewRangeError("Invalid array length"));
        }

        return nu;
    }

    private static uint ToUint32(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n))
        {
            return 0;
        }

        var t = Math.Truncate(n);
        var m = t % 4294967296d;
        if (m < 0)
        {
            m += 4294967296d;
        }

        return (uint)m;
    }

    /// <summary>§10.4.2.1 ArraySetLength steps 14-16 — shrinking deletes
    /// elements from high to low and STOPS at the first non-configurable one:
    /// length ends at that index + 1 and the operation reports failure (the
    /// defineProperty caller turns that into a TypeError).</summary>
    private bool ApplyLength(uint newLen)
    {
        if (newLen < _length)
        {
            // A non-configurable bag element ≥ newLen clamps the shrink.
            if (_bagIndices is { Count: > 0 } bag)
            {
                var clamp = -1L;
                foreach (var i in bag)
                {
                    if (i >= newLen
                        && base.GetOwnPropertyDescriptor(IndexToString(i)) is { Configurable: false })
                    {
                        clamp = Math.Max(clamp, i);
                    }
                }

                if (clamp >= 0)
                {
                    ShrinkTo((uint)clamp + 1);
                    return false;
                }
            }

            ShrinkTo(newLen);
        }
        else
        {
            _length = newLen;
        }

        return true;
    }

    private void ShrinkTo(uint newLen)
    {
        if (newLen < (uint)_items.Count)
        {
            _items.RemoveRange((int)newLen, _items.Count - (int)newLen);
        }

        _holes?.RemoveWhere(i => i >= newLen);
        if (_bagIndices is { Count: > 0 } bag)
        {
            List<uint>? gone = null;
            foreach (var i in bag)
            {
                if (i >= newLen)
                {
                    (gone ??= new List<uint>()).Add(i);
                }
            }

            if (gone is not null)
            {
                foreach (var i in gone)
                {
                    base.Delete(IndexToString(i));
                    bag.Remove(i);
                }
            }
        }

        _length = newLen;
        RecomputeSlow();
    }

    public override string ToString() => "[object Array]";
}
