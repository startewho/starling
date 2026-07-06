using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §23.1 The Array constructor and §23.1.3 Array.prototype. Backed by the
/// dense <see cref="JsArray"/> exotic object.
/// </summary>
/// <remarks>
/// <para>All prototype methods are generic: lengths are §7.1.20 LengthOfArrayLike
/// values (up to 2^53-1, held in <c>long</c>), element traffic goes through
/// HasProperty-gated reads / throwing writes and deletes, and fresh results
/// come from §7.3.22 ArraySpeciesCreate. The <see cref="JsArray"/> dense fast
/// paths (<c>TryGetDense</c>/<c>TrySetDense</c>) keep the common case
/// allocation-free.</para>
/// </remarks>
public static class ArrayCtor
{
    private const long MaxSafeLength = 9007199254740991; // 2^53 - 1
    private const long MaxArrayLength = 4294967295;      // 2^32 - 1

    [ThreadStatic] private static HashSet<JsObject>? s_joinStack;

    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.ArrayPrototype;

        // -------- Constructor (callable + constructible). §23.1.1.1. Array may
        // be called as a function or constructed; when derived via
        // `class X extends Array {}`, super() threads X as new.target so the
        // result is X-prototyped (a real exotic Array with the right chain).
        var ctor = new JsNativeFunction(realm, "Array", length: 1,
            (newTarget, args) =>
            {
                var arr = ConstructArray(realm, args);
                var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
                if (!ReferenceEquals(instProto, proto))
                {
                    arr.SetPrototypeOf(instProto);
                }

                return JsValue.Object(arr);
            },
            isConstructor: true);
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));

        // -------- Statics
        IntrinsicHelpers.DefineMethod(realm, ctor, "isArray", 1, (_, args) =>
            JsValue.Boolean(args.Length > 0 && JsArray.IsArray(args[0], realm)));
        IntrinsicHelpers.DefineMethod(realm, ctor, "of", 0, (thisV, args) => Of(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, ctor, "from", 1, (thisV, args) => From(realm, thisV, args));

        // Bulk-install constructor + every string-keyed prototype method by
        // adopting one precomputed shape. Order (and thus getOwnPropertyNames
        // order) is unchanged: constructor, mutating methods, non-mutating
        // methods, ES2023 immutable methods, then the iterator trio
        // (values/keys/entries). All are string-keyed builtin data properties, so
        // the result is byte-identical to the prior sequential DefineMethod chain.
        var speciesGetter = new JsNativeFunction(realm, "get [Symbol.species]", 0,
            (thisV, _) => thisV, isConstructor: false);
        ctor.DefineOwnProperty(SymbolCtor.Species,
            PropertyDescriptor.Accessor(speciesGetter, null, enumerable: false, configurable: true));

        IntrinsicHelpers.BulkInstallBuiltins(realm, proto, new[]
        {
            new IntrinsicHelpers.BulkMember("constructor", 0, null, JsValue.Object(ctor)),
            new IntrinsicHelpers.BulkMember("push", 1, (thisV, args) => Push(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("pop", 0, (thisV, _) => Pop(realm, thisV)),
            new IntrinsicHelpers.BulkMember("shift", 0, (thisV, _) => Shift(realm, thisV)),
            new IntrinsicHelpers.BulkMember("unshift", 1, (thisV, args) => Unshift(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("splice", 2, (thisV, args) => Splice(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("reverse", 0, (thisV, _) => Reverse(realm, thisV)),
            new IntrinsicHelpers.BulkMember("sort", 1, (thisV, args) => Sort(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("fill", 1, (thisV, args) => Fill(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("copyWithin", 2, (thisV, args) => CopyWithin(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("concat", 1, (thisV, args) => Concat(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("slice", 2, (thisV, args) => Slice(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("join", 1, (thisV, args) => Join(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("toString", 0, (thisV, _) => ToString(realm, thisV)),
            new IntrinsicHelpers.BulkMember("toLocaleString", 0, (thisV, args) => ToLocaleString(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("indexOf", 1, (thisV, args) => IndexOf(realm, thisV, args, fromEnd: false)),
            new IntrinsicHelpers.BulkMember("lastIndexOf", 1, (thisV, args) => IndexOf(realm, thisV, args, fromEnd: true)),
            new IntrinsicHelpers.BulkMember("includes", 1, (thisV, args) => Includes(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("at", 1, (thisV, args) => At(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("forEach", 1, (thisV, args) => ForEach(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("map", 1, (thisV, args) => Map(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("filter", 1, (thisV, args) => Filter(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("reduce", 1, (thisV, args) => Reduce(realm, thisV, args, fromRight: false)),
            new IntrinsicHelpers.BulkMember("reduceRight", 1, (thisV, args) => Reduce(realm, thisV, args, fromRight: true)),
            new IntrinsicHelpers.BulkMember("every", 1, (thisV, args) => Every(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("some", 1, (thisV, args) => Some(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("find", 1, (thisV, args) => Find(realm, thisV, args, fromEnd: false, indexOnly: false)),
            new IntrinsicHelpers.BulkMember("findIndex", 1, (thisV, args) => Find(realm, thisV, args, fromEnd: false, indexOnly: true)),
            new IntrinsicHelpers.BulkMember("findLast", 1, (thisV, args) => Find(realm, thisV, args, fromEnd: true, indexOnly: false)),
            new IntrinsicHelpers.BulkMember("findLastIndex", 1, (thisV, args) => Find(realm, thisV, args, fromEnd: true, indexOnly: true)),
            new IntrinsicHelpers.BulkMember("flat", 0, (thisV, args) => Flat(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("flatMap", 1, (thisV, args) => FlatMap(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("toReversed", 0, (thisV, _) => ToReversed(realm, thisV)),
            new IntrinsicHelpers.BulkMember("toSorted", 1, (thisV, args) => ToSorted(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("toSpliced", 2, (thisV, args) => ToSpliced(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("with", 2, (thisV, args) => With(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("values", 0, (thisV, _) => IteratorIntrinsics.CreateArrayIterator(realm, thisV, ArrayIteratorKind.Value)),
            new IntrinsicHelpers.BulkMember("keys", 0, (thisV, _) => IteratorIntrinsics.CreateArrayIterator(realm, thisV, ArrayIteratorKind.Key)),
            new IntrinsicHelpers.BulkMember("entries", 0, (thisV, _) => IteratorIntrinsics.CreateArrayIterator(realm, thisV, ArrayIteratorKind.KeyAndValue)),
        });
        // §23.1.3.36 Array.prototype[@@iterator] is the SAME function object as
        // Array.prototype.values per spec. Symbol-keyed — install via the
        // dictionary path AFTER the string-method shape is adopted.
        var values = proto.Get("values");
        proto.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.BuiltinMethod(values));

        // §23.1.3.38 Array.prototype[@@unscopables] — an ordinary null-proto
        // object marking the post-ES6 additions invisible to `with` blocks.
        var unscopables = new JsObject((JsObject?)null);
        foreach (var name in new[]
        {
            "at", "copyWithin", "entries", "fill", "find", "findIndex",
            "findLast", "findLastIndex", "flat", "flatMap", "includes",
            "keys", "toReversed", "toSorted", "toSpliced", "values",
        })
        {
            unscopables.DefineOwnProperty(name,
                PropertyDescriptor.Data(JsValue.True, writable: true, enumerable: true, configurable: true));
        }

        proto.DefineOwnProperty(SymbolCtor.Unscopables,
            PropertyDescriptor.Data(JsValue.Object(unscopables), writable: false, enumerable: false, configurable: true));

        realm.ArrayConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Array",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // ============================================================
    //                   Constructor helper
    // ============================================================
    private static JsArray ConstructArray(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 1 && args[0].IsNumber)
        {
            var n = args[0].AsNumber;
            var len = (uint)n;
            if (n != len || double.IsNaN(n) || double.IsInfinity(n))
            {
                throw new JsThrow(realm.NewRangeError("Invalid array length"));
            }

            var arr = new JsArray(realm);
            var lengthDesc = PropertyDescriptor.Data(JsValue.Number(len), writable: true, enumerable: false, configurable: false);
            arr.DefineOwnProperty("length", lengthDesc);
            return arr;
        }
        return new JsArray(realm, args);
    }

    // ============================================================
    //                       Helpers
    // ============================================================

    /// <summary>Coerce <c>this</c> into an array-like host object. Falls back
    /// to ToObject so generic prototype methods can be called on plain objects
    /// (the spec convention — every Array.prototype method works on any
    /// <c>{ length, 0, 1, ... }</c> shape).</summary>
    private static JsObject ThisObject(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("Array.prototype method called on null or undefined"));
        }

        return thisV.IsObject ? thisV.AsObject : AbstractOperations.ToObject(realm, thisV);
    }

    /// <summary>§7.1.20 LengthOfArrayLike — 0 … 2^53-1.</summary>
    private static long ToLength(JsRealm realm, JsObject obj)
    {
        var vm = realm.ActiveVm;
        var v = AbstractOperations.Get(vm, obj, "length");
        var prim = AbstractOperations.ToPrimitive(vm, v, "number");
        var n = prim.IsBigInt
            ? throw new JsThrow(realm.NewTypeError("Cannot convert a BigInt to array length"))
            : JsValue.ToNumber(prim);
        if (double.IsNaN(n) || n <= 0)
        {
            return 0;
        }

        var t = Math.Truncate(n);
        return t >= MaxSafeLength ? MaxSafeLength : (long)t;
    }

    /// <summary>§7.1.5 ToIntegerOrInfinity — keeps ±∞, so relative-index
    /// clamping can distinguish them.</summary>
    private static double ToIntegerOrInfinity(JsRealm realm, JsValue v)
    {
        var prim = AbstractOperations.ToPrimitive(realm.ActiveVm, v, "number");
        if (prim.IsSymbol)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a Symbol value to a number"));
        }

        if (prim.IsBigInt)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert a BigInt value to a number"));
        }

        var n = JsValue.ToNumber(prim);
        if (double.IsNaN(n))
        {
            return 0;
        }

        return double.IsInfinity(n) ? n : Math.Truncate(n);
    }

    /// <summary>The shared start/end clamp: negative counts back from
    /// <paramref name="len"/>, both directions saturate to [0, len].</summary>
    private static long ClampRelative(double relative, long len)
    {
        if (relative < 0)
        {
            var r = len + relative;
            return r < 0 ? 0 : (long)r;
        }

        return relative > len ? len : (long)relative;
    }

    private static string IndexKey(long i) => i.ToString(CultureInfo.InvariantCulture);

    /// <summary>VM-aware [[Get]] for an index — getters on array-likes fire.</summary>
    private static JsValue GetElement(JsRealm realm, JsObject obj, long i)
    {
        if (obj is JsArray ja && ja.TryGetDense(i, out var fast))
        {
            return fast;
        }

        return AbstractOperations.Get(realm.ActiveVm, obj, IndexKey(i));
    }

    /// <summary>The §23.1.3 iteration protocol read: HasProperty(O, k) first
    /// (a HOLE that the prototype chain fills IS visited; a true hole is
    /// skipped), then a VM-aware [[Get]] so accessors fire. Returns false for
    /// a skipped hole.</summary>
    private static bool TryGetElement(JsRealm realm, JsObject obj, long i, out JsValue v)
    {
        if (obj is JsArray ja && ja.TryGetDense(i, out v))
        {
            return true;
        }

        var key = IndexKey(i);
        if (!obj.Has(key))
        {
            v = JsValue.Undefined;
            return false;
        }

        v = AbstractOperations.Get(realm.ActiveVm, obj, key);
        return true;
    }

    /// <summary>§7.3.4 Set(O, P, V, true) — a rejected write is a TypeError.</summary>
    private static void SetElementThrow(JsRealm realm, JsObject obj, long i, JsValue v)
    {
        if (obj is JsArray ja && ja.TrySetDense(i, v))
        {
            return;
        }

        if (!AbstractOperations.Set(realm.ActiveVm, obj, IndexKey(i), v))
        {
            throw new JsThrow(realm.NewTypeError($"Cannot assign to read only property '{i}'"));
        }
    }

    private static void SetLengthThrow(JsRealm realm, JsObject obj, long len)
    {
        if (!AbstractOperations.Set(realm.ActiveVm, obj, "length", JsValue.Number(len)))
        {
            throw new JsThrow(realm.NewTypeError("Cannot assign to read only property 'length'"));
        }
    }

    /// <summary>§7.3.10 DeletePropertyOrThrow.</summary>
    private static void DeleteThrow(JsRealm realm, JsObject obj, long i)
    {
        if (!obj.Delete(IndexKey(i)))
        {
            throw new JsThrow(realm.NewTypeError($"Cannot delete property '{i}'"));
        }
    }

    /// <summary>§7.3.7 CreateDataPropertyOrThrow for an index key.</summary>
    private static void CreateIndexPropertyThrow(JsRealm realm, JsObject target, long index, JsValue value)
    {
        if (target is JsArray fast && fast.TrySetDense(index, value))
        {
            return;
        }

        if (!target.DefineOwnProperty(IndexKey(index),
                PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true)))
        {
            throw new JsThrow(realm.NewTypeError($"Cannot define property '{index}'"));
        }
    }

    /// <summary>§10.4.2.2 ArrayCreate — RangeError above 2^32-1; the length is
    /// virtual (no slot materialization).</summary>
    private static JsArray ArrayCreate(JsRealm realm, long length)
    {
        if (length > MaxArrayLength)
        {
            throw new JsThrow(realm.NewRangeError("Invalid array length"));
        }

        var arr = new JsArray(realm);
        if (length > 0)
        {
            arr.DefineOwnProperty("length",
                PropertyDescriptor.Data(JsValue.Number(length), writable: true, enumerable: false, configurable: false));
        }

        return arr;
    }

    /// <summary>§7.3.5 GetMethod on a value (GetV semantics — primitives
    /// resolve through their wrapper prototype).</summary>
    private static JsValue GetMethod(JsRealm realm, JsValue value, JsPropertyKey key)
    {
        var obj = AbstractOperations.ToObject(realm, value);
        var fn = AbstractOperations.Get(realm.ActiveVm, obj, key);
        if (fn.IsNullish)
        {
            return JsValue.Undefined;
        }

        if (!AbstractOperations.IsCallable(fn))
        {
            throw new JsThrow(realm.NewTypeError("Property is not a function"));
        }

        return fn;
    }

    // ============================================================
    //                       Statics: from / of
    // ============================================================

    private static JsValue From(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var vm = realm.ActiveVm;
        var items = args.Length > 0 ? args[0] : JsValue.Undefined;
        var mapFn = args.Length > 1 ? args[1] : JsValue.Undefined;
        var thisArg = args.Length > 2 ? args[2] : JsValue.Undefined;
        var mapping = !mapFn.IsUndefined;
        if (mapping && !AbstractOperations.IsCallable(mapFn))
        {
            throw new JsThrow(realm.NewTypeError("Array.from: map fn must be callable"));
        }

        var usingIterator = GetMethod(realm, items, JsPropertyKey.Symbol(SymbolCtor.Iterator));
        if (!usingIterator.IsUndefined)
        {
            var target = AbstractOperations.IsConstructor(thisV)
                ? ConstructTarget(realm, thisV, Array.Empty<JsValue>())
                : ArrayCreate(realm, 0);
            var iterator = AbstractOperations.Call(vm, usingIterator, items, Array.Empty<JsValue>());
            if (!iterator.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("Result of the Symbol.iterator method is not an object"));
            }

            var next = AbstractOperations.Get(vm, iterator.AsObject, "next");
            var record = new IteratorRecord(iterator, next, false);
            long k = 0;
            while (true)
            {
                var step = AbstractOperations.IteratorStep(realm, vm, ref record);
                if (step is null)
                {
                    SetLengthThrow(realm, target, k);
                    return JsValue.Object(target);
                }

                var value = AbstractOperations.IteratorValue(vm, step.Value);
                JsValue elem;
                try
                {
                    elem = mapping
                        ? AbstractOperations.Call(vm, mapFn, thisArg, new[] { value, JsValue.Number(k) })
                        : value;
                    CreateIndexPropertyThrow(realm, target, k, elem);
                }
                catch (JsThrow)
                {
                    AbstractOperations.IteratorClose(vm, record, isThrowing: true);
                    throw;
                }
                k++;
            }
        }

        // Array-like fallback (length + indexed access). NOT a TypeError for
        // non-iterable primitives — ToObject wraps them (len then reads 0).
        var arrayLike = AbstractOperations.ToObject(realm, items);
        var len = ToLength(realm, arrayLike);
        var arr = AbstractOperations.IsConstructor(thisV)
            ? ConstructTarget(realm, thisV, new[] { JsValue.Number(len) })
            : ArrayCreate(realm, len);
        for (long i = 0; i < len; i++)
        {
            var elem = GetElement(realm, arrayLike, i);
            if (mapping)
            {
                elem = AbstractOperations.Call(vm, mapFn, thisArg, new[] { elem, JsValue.Number(i) });
            }

            CreateIndexPropertyThrow(realm, arr, i, elem);
        }

        SetLengthThrow(realm, arr, len);
        return JsValue.Object(arr);
    }

    private static JsValue Of(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var len = args.Length;
        var target = AbstractOperations.IsConstructor(thisV)
            ? ConstructTarget(realm, thisV, new[] { JsValue.Number(len) })
            : ArrayCreate(realm, len);
        for (var i = 0; i < len; i++)
        {
            CreateIndexPropertyThrow(realm, target, i, args[i]);
        }

        SetLengthThrow(realm, target, len);
        return JsValue.Object(target);
    }

    private static JsObject ConstructTarget(JsRealm realm, JsValue ctor, JsValue[] args)
    {
        var created = AbstractOperations.Construct(realm.ActiveVm, ctor, args);
        if (!created.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Constructor did not produce an object"));
        }

        return created.AsObject;
    }

    /// <summary>Lightweight check whether <paramref name="value"/> has an
    /// <c>@@iterator</c> method without invoking it.</summary>
    internal static bool HasIteratorMethod(JsRealm realm, JsValue value)
    {
        if (value.IsString)
        {
            return true;
        }

        if (!value.IsObject)
        {
            return false;
        }

        var v = AbstractOperations.Get((JsVm?)null, value.AsObject, JsPropertyKey.Symbol(SymbolCtor.Iterator));
        return AbstractOperations.IsCallable(v);
    }

    // ============================================================
    //                       Mutators
    // ============================================================

    private static JsValue Push(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        if (len + args.Length > MaxSafeLength)
        {
            throw new JsThrow(realm.NewTypeError("Pushing would exceed the maximum array-like length"));
        }

        foreach (var v in args)
        {
            SetElementThrow(realm, obj, len, v);
            len++;
        }
        SetLengthThrow(realm, obj, len);
        return JsValue.Number(len);
    }

    private static JsValue Pop(JsRealm realm, JsValue thisV)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        if (len == 0)
        {
            SetLengthThrow(realm, obj, 0);
            return JsValue.Undefined;
        }
        var idx = len - 1;
        var v = GetElement(realm, obj, idx);
        DeleteThrow(realm, obj, idx);
        SetLengthThrow(realm, obj, idx);
        return v;
    }

    private static JsValue Shift(JsRealm realm, JsValue thisV)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        if (len == 0)
        {
            SetLengthThrow(realm, obj, 0);
            return JsValue.Undefined;
        }
        var first = GetElement(realm, obj, 0);
        for (long i = 1; i < len; i++)
        {
            if (TryGetElement(realm, obj, i, out var v))
            {
                SetElementThrow(realm, obj, i - 1, v);
            }
            else
            {
                DeleteThrow(realm, obj, i - 1);
            }
        }

        DeleteThrow(realm, obj, len - 1);
        SetLengthThrow(realm, obj, len - 1);
        return first;
    }

    private static JsValue Unshift(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        long count = args.Length;
        if (count > 0)
        {
            if (len + count > MaxSafeLength)
            {
                throw new JsThrow(realm.NewTypeError("Unshifting would exceed the maximum array-like length"));
            }

            for (var i = len - 1; i >= 0; i--)
            {
                if (TryGetElement(realm, obj, i, out var v))
                {
                    SetElementThrow(realm, obj, i + count, v);
                }
                else
                {
                    DeleteThrow(realm, obj, i + count);
                }
            }

            for (long i = 0; i < count; i++)
            {
                SetElementThrow(realm, obj, i, args[i]);
            }
        }
        var newLen = len + count;
        SetLengthThrow(realm, obj, newLen);
        return JsValue.Number(newLen);
    }

    private static JsValue Splice(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var start = args.Length > 0 ? ClampRelative(ToIntegerOrInfinity(realm, args[0]), len) : 0;
        long deleteCount;
        if (args.Length < 1)
        {
            deleteCount = 0;
        }
        else if (args.Length < 2)
        {
            deleteCount = len - start;
        }
        else
        {
            var dc = ToIntegerOrInfinity(realm, args[1]);
            deleteCount = dc <= 0 ? 0 : (dc >= len - start ? len - start : (long)dc);
        }

        long insertCount = Math.Max(0, args.Length - 2);
        if (len + insertCount - deleteCount > MaxSafeLength)
        {
            throw new JsThrow(realm.NewTypeError("Splicing would exceed the maximum array-like length"));
        }

        var removed = SpeciesCreateArray(realm, obj, deleteCount);
        for (long i = 0; i < deleteCount; i++)
        {
            if (TryGetElement(realm, obj, start + i, out var rv))
            {
                CreateIndexPropertyThrow(realm, removed, i, rv);
            }
        }

        SetLengthThrow(realm, removed, deleteCount);

        var newLen = len - deleteCount + insertCount;
        if (insertCount < deleteCount)
        {
            for (var i = start; i < len - deleteCount; i++)
            {
                if (TryGetElement(realm, obj, i + deleteCount, out var v))
                {
                    SetElementThrow(realm, obj, i + insertCount, v);
                }
                else
                {
                    DeleteThrow(realm, obj, i + insertCount);
                }
            }

            for (var i = len; i > newLen; i--)
            {
                DeleteThrow(realm, obj, i - 1);
            }
        }
        else if (insertCount > deleteCount)
        {
            for (var i = len - deleteCount - 1; i >= start; i--)
            {
                if (TryGetElement(realm, obj, i + deleteCount, out var v))
                {
                    SetElementThrow(realm, obj, i + insertCount, v);
                }
                else
                {
                    DeleteThrow(realm, obj, i + insertCount);
                }
            }
        }
        for (long i = 0; i < insertCount; i++)
        {
            SetElementThrow(realm, obj, start + i, args[2 + i]);
        }

        SetLengthThrow(realm, obj, newLen);
        return JsValue.Object(removed);
    }

    private static JsValue Reverse(JsRealm realm, JsValue thisV)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var middle = len / 2;
        for (long lower = 0; lower < middle; lower++)
        {
            var upper = len - lower - 1;
            var lowerPresent = TryGetElement(realm, obj, lower, out var lowerV);
            var upperPresent = TryGetElement(realm, obj, upper, out var upperV);
            if (lowerPresent && upperPresent)
            {
                SetElementThrow(realm, obj, lower, upperV);
                SetElementThrow(realm, obj, upper, lowerV);
            }
            else if (upperPresent)
            {
                SetElementThrow(realm, obj, lower, upperV);
                DeleteThrow(realm, obj, upper);
            }
            else if (lowerPresent)
            {
                DeleteThrow(realm, obj, lower);
                SetElementThrow(realm, obj, upper, lowerV);
            }
        }
        return JsValue.Object(obj);
    }

    private static JsValue Sort(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var cmpV = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!cmpV.IsUndefined && !AbstractOperations.IsCallable(cmpV))
        {
            throw new JsThrow(realm.NewTypeError("Array.prototype.sort: comparator must be a function"));
        }

        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);

        // §23.1.3.30.1 SortIndexedProperties (skip-holes): only PRESENT
        // elements are read, sorted, and written back; the hole tail is
        // deleted afterwards.
        var items = new List<JsValue>(len < 64 ? (int)len : 64);
        for (long i = 0; i < len; i++)
        {
            if (TryGetElement(realm, obj, i, out var v))
            {
                items.Add(v);
            }
        }

        var arr = items.ToArray();
        StableSort(realm, arr, cmpV);
        for (var i = 0; i < arr.Length; i++)
        {
            SetElementThrow(realm, obj, i, arr[i]);
        }

        for (long i = arr.Length; i < len; i++)
        {
            DeleteThrow(realm, obj, i);
        }

        return JsValue.Object(obj);
    }

    /// <summary>§23.1.3.30.2 SortCompare. Undefineds sort last regardless of
    /// the comparator; a NaN comparator result counts as equal.</summary>
    private static double SortCompare(JsRealm realm, JsValue a, JsValue b, JsValue cmpFn)
    {
        var aIsU = a.IsUndefined;
        var bIsU = b.IsUndefined;
        if (aIsU && bIsU)
        {
            return 0;
        }

        if (aIsU)
        {
            return 1;
        }

        if (bIsU)
        {
            return -1;
        }

        if (!cmpFn.IsUndefined)
        {
            var r = AbstractOperations.Call(realm.ActiveVm, cmpFn, JsValue.Undefined, new[] { a, b });
            var prim = AbstractOperations.ToPrimitive(realm.ActiveVm, r, "number");
            var n = JsValue.ToNumber(prim);
            return double.IsNaN(n) ? 0 : n;
        }
        return string.CompareOrdinal(
            AbstractOperations.ToStringJs(realm.ActiveVm, a),
            AbstractOperations.ToStringJs(realm.ActiveVm, b));
    }

    /// <summary>Bottom-up merge sort: stable, and — unlike the BCL introsort —
    /// tolerant of inconsistent user comparators (the spec leaves the order
    /// implementation-defined then, but it must not crash).</summary>
    private static void StableSort(JsRealm realm, JsValue[] items, JsValue cmpFn)
    {
        var n = items.Length;
        if (n < 2)
        {
            return;
        }

        var src = items;
        var dst = new JsValue[n];
        for (var width = 1; width < n; width *= 2)
        {
            for (var lo = 0; lo < n; lo += 2 * width)
            {
                var mid = Math.Min(lo + width, n);
                var hi = Math.Min(lo + 2 * width, n);
                int i = lo, j = mid, k = lo;
                while (i < mid && j < hi)
                {
                    dst[k++] = SortCompare(realm, src[i], src[j], cmpFn) <= 0 ? src[i++] : src[j++];
                }

                while (i < mid)
                {
                    dst[k++] = src[i++];
                }

                while (j < hi)
                {
                    dst[k++] = src[j++];
                }
            }
            (src, dst) = (dst, src);
        }
        if (!ReferenceEquals(src, items))
        {
            Array.Copy(src, items, n);
        }
    }

    private static JsValue Fill(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var value = args.Length > 0 ? args[0] : JsValue.Undefined;
        var start = args.Length > 1 ? ClampRelative(ToIntegerOrInfinity(realm, args[1]), len) : 0;
        var end = args.Length > 2 && !args[2].IsUndefined ? ClampRelative(ToIntegerOrInfinity(realm, args[2]), len) : len;
        for (var i = start; i < end; i++)
        {
            SetElementThrow(realm, obj, i, value);
        }

        return JsValue.Object(obj);
    }

    private static JsValue CopyWithin(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var to = args.Length > 0 ? ClampRelative(ToIntegerOrInfinity(realm, args[0]), len) : 0;
        var from = args.Length > 1 ? ClampRelative(ToIntegerOrInfinity(realm, args[1]), len) : 0;
        var final = args.Length > 2 && !args[2].IsUndefined ? ClampRelative(ToIntegerOrInfinity(realm, args[2]), len) : len;
        var count = Math.Min(final - from, len - to);
        long direction = 1;
        if (count > 0 && from < to && to < from + count)
        {
            direction = -1;
            from += count - 1;
            to += count - 1;
        }
        while (count > 0)
        {
            if (TryGetElement(realm, obj, from, out var v))
            {
                SetElementThrow(realm, obj, to, v);
            }
            else
            {
                DeleteThrow(realm, obj, to);
            }

            from += direction;
            to += direction;
            count--;
        }
        return JsValue.Object(obj);
    }

    /// <summary>§7.3.22 ArraySpeciesCreate — reads the receiver's
    /// `constructor` and its @@species (both observable) and constructs the
    /// result through it; ArrayCreate otherwise. Used by the methods that
    /// return fresh arrays (map/filter/slice/splice/concat).</summary>
    private static JsObject SpeciesCreateArray(JsRealm realm, JsObject original, long length)
    {
        var vm = realm.ActiveVm;
        if (!JsArray.IsArray(JsValue.Object(original), realm))
        {
            return ArrayCreate(realm, length);
        }

        var ctorV = AbstractOperations.Get(vm, original, "constructor");
        JsValue species = ctorV;
        if (ctorV.IsObject)
        {
            species = AbstractOperations.Get(vm, ctorV.AsObject, JsPropertyKey.Symbol(SymbolCtor.Species));
            if (species.IsNull)
            {
                species = JsValue.Undefined;
            }
        }

        if (species.IsUndefined
            || (species.IsObject && ReferenceEquals(species.AsObject, realm.ArrayConstructor)))
        {
            return ArrayCreate(realm, length);
        }

        if (!AbstractOperations.IsConstructor(species))
        {
            throw new JsThrow(realm.NewTypeError("@@species is not a constructor"));
        }

        var created = AbstractOperations.Construct(vm, species, new[] { JsValue.Number(length) });
        if (!created.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("@@species did not construct an object"));
        }

        return created.AsObject;
    }

    // ============================================================
    //                       Non-mutators
    // ============================================================

    private static JsValue Concat(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var result = SpeciesCreateArray(realm, obj, 0);
        long n = 0;
        AppendConcat(realm, result, JsValue.Object(obj), ref n);
        foreach (var a in args)
        {
            AppendConcat(realm, result, a, ref n);
        }

        // §23.1.3.1 step 6 — the final length write is part of the contract
        // (holes at the tail must still extend length).
        SetLengthThrow(realm, result, n);
        return JsValue.Object(result);
    }

    private static void AppendConcat(JsRealm realm, JsObject target, JsValue v, ref long n)
    {
        if (IsConcatSpreadable(realm, v))
        {
            var source = v.AsObject;
            var len = ToLength(realm, source);
            if (n + len > MaxSafeLength)
            {
                throw new JsThrow(realm.NewTypeError("Concat result would exceed the maximum array-like length"));
            }

            for (long i = 0; i < len; i++)
            {
                if (TryGetElement(realm, source, i, out var el))
                {
                    CreateIndexPropertyThrow(realm, target, n, el);
                }

                n++; // holes advance the write cursor without materializing
            }
        }
        else
        {
            if (n >= MaxSafeLength)
            {
                throw new JsThrow(realm.NewTypeError("Concat result would exceed the maximum array-like length"));
            }

            CreateIndexPropertyThrow(realm, target, n++, v);
        }
    }

    private static bool IsConcatSpreadable(JsRealm realm, JsValue value)
    {
        if (!value.IsObject)
        {
            return false;
        }

        var spreadable = AbstractOperations.Get(realm.ActiveVm, value.AsObject,
            JsPropertyKey.Symbol(SymbolCtor.IsConcatSpreadable));
        return spreadable.IsUndefined ? JsArray.IsArray(value, realm) : JsValue.ToBoolean(spreadable);
    }

    private static JsValue Slice(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var start = args.Length > 0 ? ClampRelative(ToIntegerOrInfinity(realm, args[0]), len) : 0;
        var end = args.Length > 1 && !args[1].IsUndefined ? ClampRelative(ToIntegerOrInfinity(realm, args[1]), len) : len;
        var result = SpeciesCreateArray(realm, obj, Math.Max(0, end - start));
        long to = 0;
        for (var i = start; i < end; i++)
        {
            if (TryGetElement(realm, obj, i, out var v))
            {
                CreateIndexPropertyThrow(realm, result, to, v);
            }

            to++;
        }

        SetLengthThrow(realm, result, to);
        return JsValue.Object(result);
    }

    private static JsValue Join(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var sep = args.Length > 0 && !args[0].IsUndefined
            ? AbstractOperations.ToStringJs(realm.ActiveVm, args[0])
            : ",";
        var sb = new System.Text.StringBuilder();
        var stack = s_joinStack ??= new HashSet<JsObject>();
        if (!stack.Add(obj))
        {
            return JsValue.String(string.Empty);
        }

        try
        {
            for (long i = 0; i < len; i++)
            {
                if (i > 0)
                {
                    sb.Append(sep);
                }

                var v = GetElement(realm, obj, i);
                if (v.IsNullish)
                {
                    continue;
                }

                if (v.IsObject && stack.Contains(v.AsObject))
                {
                    continue;
                }

                sb.Append(AbstractOperations.ToStringJs(realm.ActiveVm, v));
            }
        }
        finally
        {
            stack.Remove(obj);
        }
        return JsValue.String(sb.ToString());
    }

    private static JsValue ToString(JsRealm realm, JsValue thisV)
    {
        var obj = ThisObject(realm, thisV);
        var join = AbstractOperations.Get(realm.ActiveVm, obj, "join");
        if (AbstractOperations.IsCallable(join))
        {
            return AbstractOperations.Call(realm.ActiveVm, join, JsValue.Object(obj), Array.Empty<JsValue>());
        }

        var objectToString = AbstractOperations.Get(realm.ActiveVm, realm.ObjectPrototype, "toString");
        return AbstractOperations.Call(realm.ActiveVm, objectToString, JsValue.Object(obj), Array.Empty<JsValue>());
    }

    /// <summary>§23.1.3.32 (+ ECMA-402 §13.4.1) — every non-nullish element is
    /// asked for ITS toLocaleString (locales/options forwarded), comma-joined.</summary>
    private static JsValue ToLocaleString(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var vm = realm.ActiveVm;
        var sb = new System.Text.StringBuilder();
        var stack = s_joinStack ??= new HashSet<JsObject>();
        if (!stack.Add(obj))
        {
            return JsValue.String(string.Empty);
        }

        try
        {
            for (long i = 0; i < len; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                var v = GetElement(realm, obj, i);
                if (v.IsNullish)
                {
                    continue;
                }

                if (v.IsObject && stack.Contains(v.AsObject))
                {
                    continue;
                }

                var target = AbstractOperations.ToObject(realm, v);
                var method = AbstractOperations.Get(vm, target, "toLocaleString");
                if (!AbstractOperations.IsCallable(method))
                {
                    throw new JsThrow(realm.NewTypeError("toLocaleString is not callable"));
                }

                var s = AbstractOperations.Call(vm, method, v, args);
                sb.Append(AbstractOperations.ToStringJs(vm, s));
            }
        }
        finally
        {
            stack.Remove(obj);
        }
        return JsValue.String(sb.ToString());
    }

    private static JsValue IndexOf(JsRealm realm, JsValue thisV, JsValue[] args, bool fromEnd)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        if (len == 0)
        {
            return JsValue.Number(-1);
        }

        var target = args.Length > 0 ? args[0] : JsValue.Undefined;
        long from;
        if (args.Length > 1)
        {
            var n = ToIntegerOrInfinity(realm, args[1]);
            if (fromEnd)
            {
                if (double.IsNegativeInfinity(n))
                {
                    return JsValue.Number(-1);
                }

                from = n >= 0 ? (long)Math.Min(n, len - 1) : len + (long)n;
            }
            else
            {
                if (double.IsPositiveInfinity(n))
                {
                    return JsValue.Number(-1);
                }

                from = n >= 0 ? (long)n : Math.Max(0, len + (long)n);
            }
        }
        else
        {
            from = fromEnd ? len - 1 : 0;
        }
        if (fromEnd)
        {
            for (var i = from; i >= 0; i--)
            {
                if (TryGetElement(realm, obj, i, out var el) && JsValue.StrictEquals(el, target))
                {
                    return JsValue.Number(i);
                }
            }
        }
        else
        {
            for (var i = from; i < len; i++)
            {
                if (TryGetElement(realm, obj, i, out var el) && JsValue.StrictEquals(el, target))
                {
                    return JsValue.Number(i);
                }
            }
        }
        return JsValue.Number(-1);
    }

    private static JsValue Includes(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        if (len == 0)
        {
            return JsValue.False;
        }

        var target = args.Length > 0 ? args[0] : JsValue.Undefined;
        var n = args.Length > 1 ? ToIntegerOrInfinity(realm, args[1]) : 0;
        if (double.IsPositiveInfinity(n))
        {
            return JsValue.False;
        }

        var from = n >= 0 ? (long)n : Math.Max(0, len + (long)Math.Max(n, -9007199254740991d));
        for (var i = from; i < len; i++)
        {
            if (AbstractOperations.SameValueZero(GetElement(realm, obj, i), target))
            {
                return JsValue.True;
            }
        }

        return JsValue.False;
    }

    private static JsValue At(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var rel = args.Length > 0 ? ToIntegerOrInfinity(realm, args[0]) : 0;
        var k = rel >= 0 ? rel : len + rel;
        if (k < 0 || k >= len)
        {
            return JsValue.Undefined;
        }

        return GetElement(realm, obj, (long)k);
    }

    private static (JsValue fn, JsValue thisArg) ParseCallback(JsRealm realm, JsValue[] args, string methodName)
    {
        if (args.Length == 0 || !AbstractOperations.IsCallable(args[0]))
        {
            throw new JsThrow(realm.NewTypeError($"Array.prototype.{methodName}: callback must be a function"));
        }

        return (args[0], args.Length > 1 ? args[1] : JsValue.Undefined);
    }

    private static JsValue ForEach(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var (fn, thisArg) = ParseCallback(realm, args, "forEach");
        for (long i = 0; i < len; i++)
        {
            if (!TryGetElement(realm, obj, i, out var v))
            {
                continue; // true hole — skipped per §23.1.3.15 step 6.b
            }

            AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { v, JsValue.Number(i), JsValue.Object(obj) });
        }
        return JsValue.Undefined;
    }

    private static JsValue Map(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var (fn, thisArg) = ParseCallback(realm, args, "map");
        var result = SpeciesCreateArray(realm, obj, len);
        for (long i = 0; i < len; i++)
        {
            if (!TryGetElement(realm, obj, i, out var v))
            {
                continue; // hole stays a hole in the result
            }

            var mapped = AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { v, JsValue.Number(i), JsValue.Object(obj) });
            CreateIndexPropertyThrow(realm, result, i, mapped);
        }

        return JsValue.Object(result);
    }

    private static JsValue Filter(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var (fn, thisArg) = ParseCallback(realm, args, "filter");
        var result = SpeciesCreateArray(realm, obj, 0);
        long to = 0;
        for (long i = 0; i < len; i++)
        {
            if (!TryGetElement(realm, obj, i, out var v))
            {
                continue;
            }

            var keep = AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { v, JsValue.Number(i), JsValue.Object(obj) });
            if (JsValue.ToBoolean(keep))
            {
                CreateIndexPropertyThrow(realm, result, to++, v);
            }
        }
        return JsValue.Object(result);
    }

    private static JsValue Reduce(JsRealm realm, JsValue thisV, JsValue[] args, bool fromRight)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        if (args.Length == 0 || !AbstractOperations.IsCallable(args[0]))
        {
            throw new JsThrow(realm.NewTypeError("Array.prototype.reduce: callback must be a function"));
        }

        var fn = args[0];
        var hasInitial = args.Length > 1;
        JsValue acc;
        long i;
        if (hasInitial)
        {
            acc = args[1];
            i = fromRight ? len - 1 : 0;
        }
        else
        {
            if (len == 0)
            {
                throw new JsThrow(realm.NewTypeError("Reduce of empty array with no initial value"));
            }

            // §23.1.3.24 step 8 — scan for the FIRST present element (holes
            // don't count; all-holes throws).
            i = fromRight ? len - 1 : 0;
            var found = false;
            acc = JsValue.Undefined;
            while (fromRight ? i >= 0 : i < len)
            {
                if (TryGetElement(realm, obj, i, out acc))
                {
                    found = true;
                    i += fromRight ? -1 : 1;
                    break;
                }

                i += fromRight ? -1 : 1;
            }

            if (!found)
            {
                throw new JsThrow(realm.NewTypeError("Reduce of empty array with no initial value"));
            }
        }
        if (fromRight)
        {
            for (; i >= 0; i--)
            {
                if (!TryGetElement(realm, obj, i, out var v))
                {
                    continue; // hole — not visited (§23.1.3.25 step 9.b)
                }

                acc = AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Undefined,
                    new[] { acc, v, JsValue.Number(i), JsValue.Object(obj) });
            }
        }
        else
        {
            for (; i < len; i++)
            {
                if (!TryGetElement(realm, obj, i, out var v))
                {
                    continue;
                }

                acc = AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Undefined,
                    new[] { acc, v, JsValue.Number(i), JsValue.Object(obj) });
            }
        }
        return acc;
    }

    private static JsValue Every(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var (fn, thisArg) = ParseCallback(realm, args, "every");
        for (long i = 0; i < len; i++)
        {
            if (!TryGetElement(realm, obj, i, out var v))
            {
                continue;
            }

            var r = AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { v, JsValue.Number(i), JsValue.Object(obj) });
            if (!JsValue.ToBoolean(r))
            {
                return JsValue.False;
            }
        }
        return JsValue.True;
    }

    private static JsValue Some(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var (fn, thisArg) = ParseCallback(realm, args, "some");
        for (long i = 0; i < len; i++)
        {
            if (!TryGetElement(realm, obj, i, out var v))
            {
                continue;
            }

            var r = AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { v, JsValue.Number(i), JsValue.Object(obj) });
            if (JsValue.ToBoolean(r))
            {
                return JsValue.True;
            }
        }
        return JsValue.False;
    }

    private static JsValue Find(JsRealm realm, JsValue thisV, JsValue[] args, bool fromEnd, bool indexOnly)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var name = fromEnd ? (indexOnly ? "findLastIndex" : "findLast") : (indexOnly ? "findIndex" : "find");
        var (fn, thisArg) = ParseCallback(realm, args, name);
        if (fromEnd)
        {
            for (var i = len - 1; i >= 0; i--)
            {
                var v = GetElement(realm, obj, i);
                var r = AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { v, JsValue.Number(i), JsValue.Object(obj) });
                if (JsValue.ToBoolean(r))
                {
                    return indexOnly ? JsValue.Number(i) : v;
                }
            }
        }
        else
        {
            for (long i = 0; i < len; i++)
            {
                var v = GetElement(realm, obj, i);
                var r = AbstractOperations.Call(realm.ActiveVm, fn, thisArg, new[] { v, JsValue.Number(i), JsValue.Object(obj) });
                if (JsValue.ToBoolean(r))
                {
                    return indexOnly ? JsValue.Number(i) : v;
                }
            }
        }
        return indexOnly ? JsValue.Number(-1) : JsValue.Undefined;
    }

    private static JsValue Flat(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var depth = 1d;
        if (args.Length > 0 && !args[0].IsUndefined)
        {
            depth = ToIntegerOrInfinity(realm, args[0]);
        }

        var result = SpeciesCreateArray(realm, obj, 0);
        FlattenIntoArray(realm, result, obj, len, 0, depth, JsValue.Undefined, JsValue.Undefined);
        return JsValue.Object(result);
    }

    private static JsValue FlatMap(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var (fn, thisArg) = ParseCallback(realm, args, "flatMap");
        var result = SpeciesCreateArray(realm, obj, 0);
        FlattenIntoArray(realm, result, obj, len, 0, 1, fn, thisArg);
        return JsValue.Object(result);
    }

    /// <summary>§23.1.3.13.1 FlattenIntoArray — HasProperty-gated, proxy-aware
    /// IsArray recursion, CreateDataPropertyOrThrow writes.</summary>
    private static long FlattenIntoArray(JsRealm realm, JsObject target, JsObject source,
        long sourceLen, long start, double depth, JsValue mapper, JsValue thisArg)
    {
        var targetIndex = start;
        for (long sourceIndex = 0; sourceIndex < sourceLen; sourceIndex++)
        {
            if (!TryGetElement(realm, source, sourceIndex, out var element))
            {
                continue;
            }

            if (!mapper.IsUndefined)
            {
                element = AbstractOperations.Call(realm.ActiveVm, mapper, thisArg,
                    new[] { element, JsValue.Number(sourceIndex), JsValue.Object(source) });
            }

            if (depth > 0 && JsArray.IsArray(element, realm))
            {
                var elementLen = ToLength(realm, element.AsObject);
                targetIndex = FlattenIntoArray(realm, target, element.AsObject, elementLen,
                    targetIndex, depth - 1, JsValue.Undefined, JsValue.Undefined);
            }
            else
            {
                if (targetIndex >= MaxSafeLength)
                {
                    throw new JsThrow(realm.NewTypeError("Flattening would exceed the maximum array-like length"));
                }

                CreateIndexPropertyThrow(realm, target, targetIndex, element);
                targetIndex++;
            }
        }
        return targetIndex;
    }

    // ============================================================
    //                       Immutable variants (ES2023)
    // ============================================================

    private static JsValue ToReversed(JsRealm realm, JsValue thisV)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var result = ArrayCreate(realm, len);
        for (long i = 0; i < len; i++)
        {
            CreateIndexPropertyThrow(realm, result, i, GetElement(realm, obj, len - i - 1));
        }

        return JsValue.Object(result);
    }

    private static JsValue ToSorted(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var cmpV = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!cmpV.IsUndefined && !AbstractOperations.IsCallable(cmpV))
        {
            throw new JsThrow(realm.NewTypeError("Array.prototype.toSorted: comparator must be a function"));
        }

        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        if (len > MaxArrayLength)
        {
            throw new JsThrow(realm.NewRangeError("Invalid array length"));
        }

        // §23.1.3.34 uses SortIndexedProperties in read-through-holes mode:
        // every index is read (holes become undefined and sort to the end).
        var items = new JsValue[len];
        for (long i = 0; i < len; i++)
        {
            items[i] = GetElement(realm, obj, i);
        }

        StableSort(realm, items, cmpV);
        var result = ArrayCreate(realm, len);
        for (long i = 0; i < len; i++)
        {
            CreateIndexPropertyThrow(realm, result, i, items[i]);
        }

        return JsValue.Object(result);
    }

    private static JsValue ToSpliced(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var start = args.Length > 0 ? ClampRelative(ToIntegerOrInfinity(realm, args[0]), len) : 0;
        long skipCount;
        if (args.Length < 1)
        {
            skipCount = 0;
        }
        else if (args.Length < 2)
        {
            skipCount = len - start;
        }
        else
        {
            var dc = ToIntegerOrInfinity(realm, args[1]);
            skipCount = dc <= 0 ? 0 : (dc >= len - start ? len - start : (long)dc);
        }

        long insertCount = Math.Max(0, args.Length - 2);
        var newLen = len + insertCount - skipCount;
        if (newLen > MaxArrayLength)
        {
            throw new JsThrow(realm.NewRangeError("Invalid array length"));
        }

        var result = ArrayCreate(realm, newLen);
        long i = 0;
        var r = start + skipCount;
        for (; i < start; i++)
        {
            CreateIndexPropertyThrow(realm, result, i, GetElement(realm, obj, i));
        }

        for (long j = 2; j < args.Length; j++)
        {
            CreateIndexPropertyThrow(realm, result, i++, args[j]);
        }

        for (; i < newLen; i++, r++)
        {
            CreateIndexPropertyThrow(realm, result, i, GetElement(realm, obj, r));
        }

        return JsValue.Object(result);
    }

    private static JsValue With(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var obj = ThisObject(realm, thisV);
        var len = ToLength(realm, obj);
        var rel = args.Length > 0 ? ToIntegerOrInfinity(realm, args[0]) : 0;
        var idx = rel >= 0 ? rel : len + rel;
        if (idx < 0 || idx >= len)
        {
            throw new JsThrow(realm.NewRangeError("Invalid index"));
        }

        if (len > MaxArrayLength)
        {
            throw new JsThrow(realm.NewRangeError("Invalid array length"));
        }

        var value = args.Length > 1 ? args[1] : JsValue.Undefined;
        var actual = (long)idx;
        var result = ArrayCreate(realm, len);
        for (long i = 0; i < len; i++)
        {
            CreateIndexPropertyThrow(realm, result, i, i == actual ? value : GetElement(realm, obj, i));
        }

        return JsValue.Object(result);
    }
}
