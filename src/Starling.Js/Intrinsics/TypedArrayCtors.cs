using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-262 §25.2 TypedArray constructors and shared prototype.</summary>
public static class TypedArrayCtors
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var shared = realm.TypedArrayPrototype;
        InstallSharedPrototype(realm, shared);

        // §23.2.1 — the ABSTRACT %TypedArray% constructor. Not a global, but
        // reachable via Object.getPrototypeOf(Int8Array); throws when invoked
        // directly. It owns `from`/`of` (generic, this-aware — subclasses and
        // every concrete ctor inherit them) and @@species; its `prototype` is
        // the shared method prototype the concrete prototypes inherit from.
        JsNativeFunction? abstractCtor = null;
        abstractCtor = new JsNativeFunction("TypedArray", (_, _) =>
            throw new JsThrow(realm.NewTypeError("Abstract class TypedArray not directly constructable")),
            isConstructor: true);
        abstractCtor.SetPrototypeOf(realm.FunctionPrototype);
        ArrayBufferCtor.DefineData(abstractCtor, "prototype", JsValue.Object(shared), false, false, false);
        ArrayBufferCtor.DefineData(abstractCtor, "name", JsValue.String("TypedArray"), false, false, true);
        ArrayBufferCtor.DefineData(abstractCtor, "length", JsValue.Number(0), false, false, true);
        ArrayBufferCtor.DefineMethod(abstractCtor, "from", (thisV, args) => GenericFrom(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(abstractCtor, "of", (thisV, args) => GenericOf(realm, thisV, args), 0);
        abstractCtor.DefineOwnProperty(SymbolCtor.Species,
            PropertyDescriptor.Accessor(new JsNativeFunction("get [Symbol.species]", (thisV, _) => thisV), null));
        ArrayBufferCtor.DefineData(shared, "constructor", JsValue.Object(abstractCtor), true, false, true);
        realm.TypedArrayAbstract = abstractCtor;

        InstallType(realm, "Int8Array", JsTypedArrayKind.Int8);
        InstallType(realm, "Uint8Array", JsTypedArrayKind.Uint8);
        InstallType(realm, "Uint8ClampedArray", JsTypedArrayKind.Uint8Clamped);
        InstallType(realm, "Int16Array", JsTypedArrayKind.Int16);
        InstallType(realm, "Uint16Array", JsTypedArrayKind.Uint16);
        InstallType(realm, "Int32Array", JsTypedArrayKind.Int32);
        InstallType(realm, "Uint32Array", JsTypedArrayKind.Uint32);
        InstallType(realm, "Float32Array", JsTypedArrayKind.Float32);
        InstallType(realm, "Float64Array", JsTypedArrayKind.Float64);
        InstallType(realm, "BigInt64Array", JsTypedArrayKind.BigInt64);
        InstallType(realm, "BigUint64Array", JsTypedArrayKind.BigUint64);
    }

    private static void InstallType(JsRealm realm, string name, JsTypedArrayKind kind)
    {
        var proto = new JsObject(realm.TypedArrayPrototype);

        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction(name, (newTarget, args) =>
        {
            // §23.2.5.1 step 1: a TypedArray constructor requires `new` (or a
            // derived super()). Resolve the instance prototype from new.target
            // so `class X extends Uint8Array {}` yields an X-prototyped view.
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError(name + " constructor requires 'new'"));
            }

            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(Construct(realm, instProto, kind, args));
        }, isConstructor: true);
        // §23.2.5 — every concrete TypedArray constructor inherits from the
        // abstract %TypedArray% (so `from`/`of`/@@species resolve through it).
        ctor.SetPrototypeOf(realm.TypedArrayAbstract ?? realm.FunctionPrototype);
        ArrayBufferCtor.DefineData(ctor, "prototype", JsValue.Object(proto), false, false, false);
        ArrayBufferCtor.DefineData(ctor, "name", JsValue.String(name), false, false, true);
        ArrayBufferCtor.DefineData(ctor, "length", JsValue.Number(3), false, false, true);
        ArrayBufferCtor.DefineData(ctor, "BYTES_PER_ELEMENT", JsValue.Number(JsTypedArray.BytesPerElementOf(kind)), false, false, false);
        ArrayBufferCtor.DefineData(proto, "constructor", JsValue.Object(ctor), true, false, true);
        ArrayBufferCtor.DefineData(proto, "BYTES_PER_ELEMENT", JsValue.Number(JsTypedArray.BytesPerElementOf(kind)), false, false, false);
        // §23.2.3.34: @@toStringTag lives on %TypedArray%.prototype as an
        // accessor, not on each concrete prototype — installed once in
        // InstallSharedPrototype below.
        realm.GlobalObject.DefineOwnProperty(name, PropertyDescriptor.Data(JsValue.Object(ctor), true, false, true));
    }

    private static JsTypedArray Construct(JsRealm realm, JsObject proto, JsTypedArrayKind kind, JsValue[] args)
    {
        var bpe = JsTypedArray.BytesPerElementOf(kind);
        var first = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (first.IsObject && first.AsObject is JsArrayBuffer buffer)
        {
            var offset = args.Length > 1 ? ArrayBufferCtor.ToIndex(realm, args[1]) : 0;
            if (offset % bpe != 0)
            {
                throw new JsThrow(realm.NewRangeError("TypedArray byteOffset must align to element size"));
            }

            if (offset > buffer.ByteLength)
            {
                throw new JsThrow(realm.NewRangeError("TypedArray byteOffset out of range"));
            }

            var remaining = buffer.ByteLength - offset;
            int viewLength;
            if (args.Length > 2 && !args[2].IsUndefined)
            {
                viewLength = ArrayBufferCtor.ToIndex(realm, args[2]);
                if (viewLength > remaining / bpe)
                {
                    throw new JsThrow(realm.NewRangeError("TypedArray length out of range"));
                }
            }
            else
            {
                // ES2024 — an auto-length view over a RESIZABLE buffer is
                // length-tracking: its length follows the buffer on resize.
                if (buffer.IsResizable)
                {
                    return new JsTypedArray(proto, kind, buffer, offset, null);
                }

                if (remaining % bpe != 0)
                {
                    throw new JsThrow(realm.NewRangeError("TypedArray byteLength must align to element size"));
                }

                viewLength = remaining / bpe;
            }
            return new JsTypedArray(proto, kind, buffer, offset, viewLength);
        }

        if (first.IsObject && first.AsObject is JsTypedArray source)
        {
            var target = Allocate(realm, proto, kind, source.Length);
            for (var i = 0; i < source.Length; i++)
            {
                target.SetElement(i, source.GetElement(i), realm);
            }

            return target;
        }

        if (first.IsObject)
        {
            // §23.2.5.1 step 6.b.iii — any other object initializes FROM its
            // contents: the iterator protocol when @@iterator is present,
            // otherwise array-like (ToLength of "length", absent → 0). A plain
            // object never reaches the numeric-length coercion below.
            var src = first.AsObject;
            var vm = realm.ActiveVm;
            var iterMethod = AbstractOperations.GetMethod(vm, first, SymbolCtor.Iterator);
            if (!iterMethod.IsUndefined && !iterMethod.IsNull)
            {
                var values = new List<JsValue>();
                var record = AbstractOperations.GetIterator(realm, vm, first);
                while (true)
                {
                    var step = AbstractOperations.IteratorNext(realm, vm, record);
                    if (AbstractOperations.IteratorComplete(vm, step))
                    {
                        break;
                    }

                    values.Add(AbstractOperations.IteratorValue(vm, step));
                }

                var iterTarget = Allocate(realm, proto, kind, values.Count);
                for (var i = 0; i < values.Count; i++)
                {
                    iterTarget.SetElement(i, values[i], realm);
                }

                return iterTarget;
            }

            var lenV = AbstractOperations.Get(vm, src, "length");
            var len = (int)Math.Clamp(
                lenV.IsUndefined ? 0 : Math.Truncate(JsValue.ToNumber(AbstractOperations.ToPrimitive(vm, lenV, "number"))),
                0, int.MaxValue);
            var target = Allocate(realm, proto, kind, len);
            for (var i = 0; i < len; i++)
            {
                target.SetElement(i, AbstractOperations.Get(vm, src, ArrayBufferCtor.IndexKey(i)), realm);
            }

            return target;
        }

        var length = ArrayBufferCtor.ToIndex(realm, first);
        return Allocate(realm, proto, kind, length);
    }

    private static JsTypedArray Allocate(JsRealm realm, JsObject proto, JsTypedArrayKind kind, int length)
    {
        var bpe = JsTypedArray.BytesPerElementOf(kind);
        if (length > int.MaxValue / bpe)
        {
            throw new JsThrow(realm.NewRangeError("TypedArray length out of range"));
        }

        return new JsTypedArray(proto, kind, new JsArrayBuffer(realm.ArrayBufferPrototype, length * bpe), 0, length);
    }

    /// <summary>§23.2.2.1 %TypedArray%.from — generic over `this` (the target
    /// constructor), honoring iterables, array-likes, and a map function.</summary>
    private static JsValue GenericFrom(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var vm = realm.ActiveVm;
        if (!AbstractOperations.IsConstructor(thisV))
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.from called on non-constructor"));
        }

        var source = args.Length > 0 ? args[0] : JsValue.Undefined;
        var mapFn = args.Length > 1 ? args[1] : JsValue.Undefined;
        var thisArg = args.Length > 2 ? args[2] : JsValue.Undefined;
        if (!mapFn.IsUndefined && !AbstractOperations.IsCallable(mapFn))
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.from mapFn must be callable"));
        }

        var usingIterator = AbstractOperations.GetMethod(vm, source, SymbolCtor.Iterator);
        if (!usingIterator.IsUndefined && !usingIterator.IsNull)
        {
            var values = new List<JsValue>();
            var record = AbstractOperations.GetIterator(realm, vm, source);
            while (true)
            {
                var step = AbstractOperations.IteratorNext(realm, vm, record);
                if (AbstractOperations.IteratorComplete(vm, step))
                {
                    break;
                }

                values.Add(AbstractOperations.IteratorValue(vm, step));
            }

            var target = AbstractOperations.Construct(vm, thisV, new[] { JsValue.Number(values.Count) });
            var targetObj = target.AsObject;
            for (var i = 0; i < values.Count; i++)
            {
                var v = values[i];
                if (!mapFn.IsUndefined)
                {
                    v = AbstractOperations.Call(vm, mapFn, thisArg, new[] { v, JsValue.Number(i) });
                }

                AbstractOperations.Set(vm, targetObj, ArrayBufferCtor.IndexKey(i), v);
            }

            return target;
        }

        // Array-like path.
        var srcObj = AbstractOperations.ToObject(realm, source);
        var len = ArrayBufferCtor.ToIndex(realm, AbstractOperations.Get(vm, srcObj, "length"));
        var target2 = AbstractOperations.Construct(vm, thisV, new[] { JsValue.Number(len) });
        var target2Obj = target2.AsObject;
        for (var i = 0; i < len; i++)
        {
            var v = AbstractOperations.Get(vm, srcObj, ArrayBufferCtor.IndexKey(i));
            if (!mapFn.IsUndefined)
            {
                v = AbstractOperations.Call(vm, mapFn, thisArg, new[] { v, JsValue.Number(i) });
            }

            AbstractOperations.Set(vm, target2Obj, ArrayBufferCtor.IndexKey(i), v);
        }

        return target2;
    }

    /// <summary>§23.2.2.2 %TypedArray%.of — generic over `this`.</summary>
    private static JsValue GenericOf(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var vm = realm.ActiveVm;
        if (!AbstractOperations.IsConstructor(thisV))
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.of called on non-constructor"));
        }

        var target = AbstractOperations.Construct(vm, thisV, new[] { JsValue.Number(args.Length) });
        var targetObj = target.AsObject;
        for (var i = 0; i < args.Length; i++)
        {
            AbstractOperations.Set(vm, targetObj, ArrayBufferCtor.IndexKey(i), args[i]);
        }

        return target;
    }



    private static void InstallSharedPrototype(JsRealm realm, JsObject proto)
    {
        // §23.2.3 — buffer/byteOffset/byteLength/length are ACCESSORS on
        // %TypedArray%.prototype (not instance data), so length-tracking views
        // over resizable buffers report their current size on every read.
        void Getter(string name, Func<JsTypedArray, JsValue> read) =>
            proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get " + name, 0, (thisV, _) =>
                    thisV.IsObject && thisV.AsObject is JsTypedArray ta
                        ? read(ta)
                        : throw new JsThrow(realm.NewTypeError("get %TypedArray%.prototype." + name + " called on incompatible receiver"))),
                null));
        Getter("buffer", ta => JsValue.Object(ta.Buffer));
        Getter("byteOffset", ta => JsValue.Number(ta.IsOutOfBounds ? 0 : ta.ByteOffset));
        Getter("byteLength", ta => JsValue.Number(ta.ByteLength));
        Getter("length", ta => JsValue.Number(ta.Length));

        ArrayBufferCtor.DefineMethod(proto, "at", (thisV, args) => At(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "copyWithin", (thisV, args) => CopyWithin(realm, thisV, args), 2);
        ArrayBufferCtor.DefineMethod(proto, "entries", (thisV, args) => Entries(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(proto, "every", (thisV, args) => Every(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "fill", (thisV, args) => Fill(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "filter", (thisV, args) => Filter(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "find", (thisV, args) => Find(realm, thisV, args, last: false, indexOnly: false), 1);
        ArrayBufferCtor.DefineMethod(proto, "findIndex", (thisV, args) => Find(realm, thisV, args, last: false, indexOnly: true), 1);
        ArrayBufferCtor.DefineMethod(proto, "findLast", (thisV, args) => Find(realm, thisV, args, last: true, indexOnly: false), 1);
        ArrayBufferCtor.DefineMethod(proto, "findLastIndex", (thisV, args) => Find(realm, thisV, args, last: true, indexOnly: true), 1);
        ArrayBufferCtor.DefineMethod(proto, "forEach", (thisV, args) => ForEach(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "includes", (thisV, args) => Includes(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "indexOf", (thisV, args) => IndexOf(realm, thisV, args, last: false), 1);
        ArrayBufferCtor.DefineMethod(proto, "join", (thisV, args) => Join(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "keys", (thisV, args) => Keys(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(proto, "lastIndexOf", (thisV, args) => IndexOf(realm, thisV, args, last: true), 1);
        ArrayBufferCtor.DefineMethod(proto, "map", (thisV, args) => Map(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "reduce", (thisV, args) => Reduce(realm, thisV, args, right: false), 1);
        ArrayBufferCtor.DefineMethod(proto, "reduceRight", (thisV, args) => Reduce(realm, thisV, args, right: true), 1);
        ArrayBufferCtor.DefineMethod(proto, "reverse", (thisV, args) => Reverse(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(proto, "set", (thisV, args) => Set(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "slice", (thisV, args) => Slice(realm, thisV, args), 2);
        ArrayBufferCtor.DefineMethod(proto, "some", (thisV, args) => Some(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "sort", (thisV, args) => Sort(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "subarray", (thisV, args) => Subarray(realm, thisV, args), 2);
        ArrayBufferCtor.DefineMethod(proto, "toLocaleString", (thisV, args) => Join(realm, thisV, args), 0);
        ArrayBufferCtor.DefineMethod(proto, "toReversed", (thisV, args) => ToReversed(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(proto, "toSorted", (thisV, args) => ToSorted(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(proto, "toString", (thisV, args) => Join(realm, thisV, args), 0);
        var valuesFn = IntrinsicHelpers.DefineMethod(realm, proto, "values", 0,
            (thisV, args) => Values(realm, thisV));
        ArrayBufferCtor.DefineMethod(proto, "with", (thisV, args) => With(realm, thisV, args), 2);

        // §23.2.3.36 %TypedArray%.prototype[@@iterator] is the same function
        // object as %TypedArray%.prototype.values per spec.
        proto.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.BuiltinMethod(JsValue.Object(valuesFn)));

        // §23.2.3.34 get %TypedArray%.prototype[@@toStringTag] — accessor
        // returning the receiver's [[TypedArrayName]] (Kind-derived name) or
        // undefined when the receiver isn't a TypedArray.
        var tagGetter = new JsNativeFunction(realm, "get [Symbol.toStringTag]", 0, (thisV, _) =>
        {
            if (thisV.IsObject && thisV.AsObject is JsTypedArray ta)
            {
                return JsValue.String(ta.ConstructorName);
            }

            return JsValue.Undefined;
        }, isConstructor: false);
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Accessor(tagGetter, setter: null, enumerable: false, configurable: true));
    }

    /// <summary>§23.2.4.1 TypedArraySpeciesCreate — reads the receiver's
    /// `constructor` and its @@species (both observable), constructs through
    /// it, and validates the result; falls back to the receiver's default
    /// constructor when unset.</summary>
    private static JsTypedArray SpeciesAllocate(JsRealm realm, JsTypedArray ta, int len)
    {
        var vm = realm.ActiveVm;
        var ctorV = AbstractOperations.Get(vm, ta, "constructor");
        JsValue species = JsValue.Undefined;
        if (!ctorV.IsUndefined)
        {
            if (!ctorV.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("TypedArray constructor property is not an object"));
            }

            species = AbstractOperations.Get(vm, ctorV.AsObject, JsPropertyKey.Symbol(SymbolCtor.Species));
        }

        if (species.IsUndefined || species.IsNull)
        {
            return Allocate(realm, TypePrototype(realm, ta), ta.Kind, len);
        }

        if (!AbstractOperations.IsConstructor(species))
        {
            throw new JsThrow(realm.NewTypeError("@@species is not a constructor"));
        }

        var created = AbstractOperations.Construct(vm, species, new[] { JsValue.Number(len) });
        if (!created.IsObject || created.AsObject is not JsTypedArray result)
        {
            throw new JsThrow(realm.NewTypeError("@@species did not construct a TypedArray"));
        }

        if (result.IsOutOfBounds)
        {
            throw new JsThrow(realm.NewTypeError("@@species constructed a detached or out-of-bounds TypedArray"));
        }

        if (result.Length < len)
        {
            throw new JsThrow(realm.NewTypeError("@@species constructed a TypedArray that is too small"));
        }

        var wantBig = ta.Kind is JsTypedArrayKind.BigInt64 or JsTypedArrayKind.BigUint64;
        var gotBig = result.Kind is JsTypedArrayKind.BigInt64 or JsTypedArrayKind.BigUint64;
        if (wantBig != gotBig)
        {
            throw new JsThrow(realm.NewTypeError("@@species constructed a TypedArray with a different content type"));
        }

        return result;
    }

    private static JsTypedArray ThisTA(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsTypedArray ta)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray method called on incompatible receiver"));
        }

        // §23.2.4.4 ValidateTypedArray — a detached buffer OR a fixed-length
        // view left out of bounds by a resizable-buffer shrink is a TypeError
        // at method entry.
        if (ta.IsOutOfBounds)
        {
            throw new JsThrow(realm.NewTypeError(ta.Buffer.IsDetached
                ? "Cannot perform operation on a detached ArrayBuffer"
                : "TypedArray is out of bounds on its resizable ArrayBuffer"));
        }

        return ta;
    }

    private static JsObject TypePrototype(JsRealm realm, JsTypedArray ta)
        => ta.Prototype ?? realm.TypedArrayPrototype;

    private static int RelativeIndex(JsRealm realm, JsValue v, int len, bool defaultEnd = false)
    {
        if (v.IsUndefined)
        {
            return defaultEnd ? len : 0;
        }

        var n = ArrayBufferCtor.ToIntegerOrInfinity(realm, v);
        if (n < 0)
        {
            return (int)Math.Max(len + n, 0);
        }

        return (int)Math.Min(n, len);
    }

    private static JsValue At(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var k = (int)ArrayBufferCtor.ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        if (k < 0)
        {
            k += ta.Length;
        }

        return k < 0 || k >= ta.Length ? JsValue.Undefined : ta.GetElement(k);
    }

    private static JsValue Fill(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var value = args.Length > 0 ? args[0] : JsValue.Undefined;
        var start = RelativeIndex(realm, args.Length > 1 ? args[1] : JsValue.Undefined, ta.Length);
        var end = RelativeIndex(realm, args.Length > 2 ? args[2] : JsValue.Undefined, ta.Length, defaultEnd: true);
        for (var i = start; i < end; i++)
        {
            ta.SetElement(i, value, realm);
        }

        return thisV;
    }

    private static JsValue CopyWithin(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var to = RelativeIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined, ta.Length);
        var from = RelativeIndex(realm, args.Length > 1 ? args[1] : JsValue.Undefined, ta.Length);
        var end = RelativeIndex(realm, args.Length > 2 ? args[2] : JsValue.Undefined, ta.Length, defaultEnd: true);
        var count = Math.Min(end - from, ta.Length - to);
        if (count <= 0)
        {
            return thisV;
        }

        var tmp = new JsValue[count];
        for (var i = 0; i < count; i++)
        {
            tmp[i] = ta.GetElement(from + i);
        }

        for (var i = 0; i < count; i++)
        {
            ta.SetElement(to + i, tmp[i], realm);
        }

        return thisV;
    }

    private static JsValue Set(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        if (args.Length == 0 || !args[0].IsObject)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.prototype.set source must be array-like"));
        }

        var offset = args.Length > 1 ? ArrayBufferCtor.ToIndex(realm, args[1]) : 0;
        var values = ToList(realm, args[0].AsObject);
        if (values.Count > ta.Length - offset)
        {
            throw new JsThrow(realm.NewRangeError("TypedArray.prototype.set out of range"));
        }

        for (var i = 0; i < values.Count; i++)
        {
            ta.SetElement(offset + i, values[i], realm);
        }

        return JsValue.Undefined;
    }

    private static List<JsValue> ToList(JsRealm realm, JsObject src)
    {
        if (src is JsTypedArray sta)
        {
            var values = new List<JsValue>(sta.Length);
            for (var i = 0; i < sta.Length; i++)
            {
                values.Add(sta.GetElement(i));
            }

            return values;
        }
        var len = ArrayBufferCtor.ToIndex(realm, src.Get("length"));
        var list = new List<JsValue>(len);
        for (var i = 0; i < len; i++)
        {
            list.Add(src.Get(ArrayBufferCtor.IndexKey(i)));
        }

        return list;
    }

    private static JsValue Slice(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var start = RelativeIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined, ta.Length);
        var end = RelativeIndex(realm, args.Length > 1 ? args[1] : JsValue.Undefined, ta.Length, defaultEnd: true);
        var len = Math.Max(end - start, 0);
        var result = SpeciesAllocate(realm, ta, len);
        for (var i = 0; i < len; i++)
        {
            result.SetElement(i, ta.GetElement(start + i), realm);
        }

        return JsValue.Object(result);
    }

    private static JsValue Subarray(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var start = RelativeIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined, ta.Length);
        var end = RelativeIndex(realm, args.Length > 1 ? args[1] : JsValue.Undefined, ta.Length, defaultEnd: true);
        var len = Math.Max(end - start, 0);
        return JsValue.Object(new JsTypedArray(TypePrototype(realm, ta), ta.Kind, ta.Buffer, ta.ByteOffset + start * ta.BytesPerElement, len));
    }

    private static JsValue Reverse(JsRealm realm, JsValue thisV)
    {
        var ta = ThisTA(realm, thisV);
        for (var i = 0; i < ta.Length / 2; i++)
        {
            var j = ta.Length - 1 - i;
            var a = ta.GetElement(i);
            ta.SetElement(i, ta.GetElement(j), realm);
            ta.SetElement(j, a, realm);
        }
        return thisV;
    }

    private static JsValue ToReversed(JsRealm realm, JsValue thisV)
    {
        var ta = ThisTA(realm, thisV);
        var result = SpeciesAllocate(realm, ta, ta.Length);
        // Length snapshots at method entry (§23.2.3) — a detach or
        // resize inside the callback yields undefined elements, it does
        // not truncate the visit count.
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            result.SetElement(i, ta.GetElement(ta.Length - 1 - i), realm);
        }

        return JsValue.Object(result);
    }

    private static JsValue ToSorted(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var copy = Slice(realm, thisV, Array.Empty<JsValue>()).AsObject;
        return Sort(realm, JsValue.Object(copy), args);
    }

    private static JsValue Sort(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        // §23.2.3.29 — a non-callable, non-undefined comparator is a TypeError
        // before any element is read.
        var compare = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!compare.IsUndefined && !AbstractOperations.IsCallable(compare))
        {
            throw new JsThrow(realm.NewTypeError("The comparison function must be either a function or undefined"));
        }

        var values = ToList(realm, ta);
        try
        {
            values.Sort((a, b) =>
            {
                if (AbstractOperations.IsCallable(compare))
                {
                    var r = ArrayBufferCtor.Number(realm, AbstractOperations.Call(realm.ActiveVm, compare, JsValue.Undefined, new[] { a, b }));
                    return double.IsNaN(r) ? 0 : Math.Sign(r);
                }

                // Default sort: BigInt elements compare as BigIntegers (a
                // Number conversion would be a host crash), numbers per
                // §23.2.4.7 (NaN sorts last; -0 before +0).
                if (a.IsBigInt || b.IsBigInt)
                {
                    return a.AsBigInt.CompareTo(b.AsBigInt);
                }

                var x = JsValue.ToNumber(a);
                var y = JsValue.ToNumber(b);
                if (double.IsNaN(x))
                {
                    return double.IsNaN(y) ? 0 : 1;
                }

                if (double.IsNaN(y))
                {
                    return -1;
                }

                if (x == 0 && y == 0)
                {
                    return (double.IsNegative(x) ? 0 : 1) - (double.IsNegative(y) ? 0 : 1);
                }

                return x.CompareTo(y);
            });
        }
        catch (InvalidOperationException ex) when (ex.InnerException is JsThrow inner)
        {
            // List.Sort wraps comparator exceptions; surface the JS throw.
            throw inner;
        }
        for (var i = 0; i < values.Count; i++)
        {
            ta.SetElement(i, values[i], realm);
        }

        return thisV;
    }

    private static JsValue Map(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var cb = RequireCallback(realm, args);
        var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        var result = Allocate(realm, TypePrototype(realm, ta), ta.Kind, ta.Length);
        // Length snapshots at method entry (§23.2.3) — a detach or
        // resize inside the callback yields undefined elements, it does
        // not truncate the visit count.
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            result.SetElement(i, AbstractOperations.Call(realm.ActiveVm, cb, thisArg, new[] { ta.GetElement(i), JsValue.Number(i), thisV }), realm);
        }

        return JsValue.Object(result);
    }

    private static JsValue Filter(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var cb = RequireCallback(realm, args);
        var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        var kept = new List<JsValue>();
        // Length snapshots at method entry (§23.2.3) — a detach or
        // resize inside the callback yields undefined elements, it does
        // not truncate the visit count.
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            var v = ta.GetElement(i);
            if (JsValue.ToBoolean(AbstractOperations.Call(realm.ActiveVm, cb, thisArg, new[] { v, JsValue.Number(i), thisV })))
            {
                kept.Add(v);
            }
        }
        var result = SpeciesAllocate(realm, ta, kept.Count);
        for (var i = 0; i < kept.Count; i++)
        {
            result.SetElement(i, kept[i], realm);
        }

        return JsValue.Object(result);
    }

    private static JsValue ForEach(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var cb = RequireCallback(realm, args);
        var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        // Length snapshots at method entry (§23.2.3) — a detach or
        // resize inside the callback yields undefined elements, it does
        // not truncate the visit count.
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            AbstractOperations.Call(realm.ActiveVm, cb, thisArg, new[] { ta.GetElement(i), JsValue.Number(i), thisV });
        }

        return JsValue.Undefined;
    }

    private static JsValue Every(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var cb = RequireCallback(realm, args);
        var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        // Length snapshots at method entry (§23.2.3) — a detach or
        // resize inside the callback yields undefined elements, it does
        // not truncate the visit count.
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            if (!JsValue.ToBoolean(AbstractOperations.Call(realm.ActiveVm, cb, thisArg, new[] { ta.GetElement(i), JsValue.Number(i), thisV })))
            {
                return JsValue.False;
            }
        }

        return JsValue.True;
    }

    private static JsValue Some(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var cb = RequireCallback(realm, args);
        var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        // Length snapshots at method entry (§23.2.3) — a detach or
        // resize inside the callback yields undefined elements, it does
        // not truncate the visit count.
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            if (JsValue.ToBoolean(AbstractOperations.Call(realm.ActiveVm, cb, thisArg, new[] { ta.GetElement(i), JsValue.Number(i), thisV })))
            {
                return JsValue.True;
            }
        }

        return JsValue.False;
    }

    private static JsValue Find(JsRealm realm, JsValue thisV, JsValue[] args, bool last, bool indexOnly)
    {
        var ta = ThisTA(realm, thisV);
        var cb = RequireCallback(realm, args);
        var thisArg = args.Length > 1 ? args[1] : JsValue.Undefined;
        for (var step = 0; step < ta.Length; step++)
        {
            var i = last ? ta.Length - 1 - step : step;
            var v = ta.GetElement(i);
            if (JsValue.ToBoolean(AbstractOperations.Call(realm.ActiveVm, cb, thisArg, new[] { v, JsValue.Number(i), thisV })))
            {
                return indexOnly ? JsValue.Number(i) : v;
            }
        }
        return indexOnly ? JsValue.Number(-1) : JsValue.Undefined;
    }

    private static JsValue Reduce(JsRealm realm, JsValue thisV, JsValue[] args, bool right)
    {
        var ta = ThisTA(realm, thisV);
        var cb = RequireCallback(realm, args);
        if (ta.Length == 0 && args.Length < 2)
        {
            throw new JsThrow(realm.NewTypeError("Reduce of empty TypedArray with no initial value"));
        }

        var i = right ? ta.Length - 1 : 0;
        var acc = args.Length > 1 ? args[1] : ta.GetElement(i);
        if (args.Length <= 1)
        {
            i += right ? -1 : 1;
        }

        var reduceLen = ta.Length;
        for (; right ? i >= 0 : i < reduceLen; i += right ? -1 : 1)
        {
            acc = AbstractOperations.Call(realm.ActiveVm, cb, JsValue.Undefined, new[] { acc, ta.GetElement(i), JsValue.Number(i), thisV });
        }

        return acc;
    }

    private static JsValue Includes(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var search = args.Length > 0 ? args[0] : JsValue.Undefined;
        var start = RelativeIndex(realm, args.Length > 1 ? args[1] : JsValue.Undefined, ta.Length);
        var scanLen = ta.Length;
        for (var i = start; i < scanLen; i++)
        {
            if (AbstractOperations.SameValueZero(ta.GetElement(i), search))
            {
                return JsValue.True;
            }
        }

        return JsValue.False;
    }

    private static JsValue IndexOf(JsRealm realm, JsValue thisV, JsValue[] args, bool last)
    {
        var ta = ThisTA(realm, thisV);
        var search = args.Length > 0 ? args[0] : JsValue.Undefined;
        var start = args.Length > 1 ? RelativeIndex(realm, args[1], ta.Length, defaultEnd: last) : (last ? ta.Length - 1 : 0);
        if (last)
        {
            for (var i = Math.Min(start, ta.Length - 1); i >= 0; i--)
            {
                if (JsValue.StrictEquals(ta.GetElement(i), search))
                {
                    return JsValue.Number(i);
                }
            }
        }
        else
        {
            var scanLen = ta.Length;
        for (var i = start; i < scanLen; i++)
            {
                if (JsValue.StrictEquals(ta.GetElement(i), search))
                {
                    return JsValue.Number(i);
                }
            }
        }
        return JsValue.Number(-1);
    }

    private static JsValue Join(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var sep = args.Length > 0 && !args[0].IsUndefined ? JsValue.ToStringValue(args[0]) : ",";
        var parts = new string[ta.Length];
        // Length snapshots at method entry (§23.2.3) — a detach or
        // resize inside the callback yields undefined elements, it does
        // not truncate the visit count.
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            parts[i] = JsValue.ToStringValue(ta.GetElement(i));
        }

        return JsValue.String(string.Join(sep, parts));
    }

    private static JsValue With(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var index = (int)ArrayBufferCtor.ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        if (index < 0)
        {
            index += ta.Length;
        }

        if (index < 0 || index >= ta.Length)
        {
            throw new JsThrow(realm.NewRangeError("TypedArray.prototype.with index out of range"));
        }

        var copy = Slice(realm, thisV, Array.Empty<JsValue>()).AsObject as JsTypedArray ?? throw new InvalidOperationException();
        copy.SetElement(index, args.Length > 1 ? args[1] : JsValue.Undefined, realm);
        return JsValue.Object(copy);
    }

    // §23.2.3.{19,36,7} keys / values / entries return a real Array Iterator
    // (%ArrayIteratorPrototype%) over the typed array, so the result has a
    // working `next` and is itself iterable — required for `for…of`, spread,
    // and destructuring to consume a typed array (the @@iterator alias of
    // `values`). ThisTA validates the receiver is a TypedArray first.
    private static JsValue Keys(JsRealm realm, JsValue thisV)
    {
        var ta = ThisTA(realm, thisV);
        return IteratorIntrinsics.CreateArrayIterator(realm, JsValue.Object(ta), ArrayIteratorKind.Key);
    }

    private static JsValue Values(JsRealm realm, JsValue thisV)
    {
        var ta = ThisTA(realm, thisV);
        return IteratorIntrinsics.CreateArrayIterator(realm, JsValue.Object(ta), ArrayIteratorKind.Value);
    }

    private static JsValue Entries(JsRealm realm, JsValue thisV)
    {
        var ta = ThisTA(realm, thisV);
        return IteratorIntrinsics.CreateArrayIterator(realm, JsValue.Object(ta), ArrayIteratorKind.KeyAndValue);
    }

    private static JsValue RequireCallback(JsRealm realm, JsValue[] args)
    {
        var cb = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!AbstractOperations.IsCallable(cb))
        {
            throw new JsThrow(realm.NewTypeError("callback must be callable"));
        }

        return cb;
    }
}
