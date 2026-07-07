using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-262 §25.2 TypedArray constructors and shared prototype.</summary>
public static class TypedArrayCtors
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var shared = realm.TypedArrayPrototype;
        var abstractCtor = InstallAbstractCtor(realm, shared);
        InstallSharedPrototype(realm, shared);

        InstallType(realm, abstractCtor, "Int8Array", JsTypedArrayKind.Int8);
        InstallType(realm, abstractCtor, "Uint8Array", JsTypedArrayKind.Uint8);
        InstallType(realm, abstractCtor, "Uint8ClampedArray", JsTypedArrayKind.Uint8Clamped);
        InstallType(realm, abstractCtor, "Int16Array", JsTypedArrayKind.Int16);
        InstallType(realm, abstractCtor, "Uint16Array", JsTypedArrayKind.Uint16);
        InstallType(realm, abstractCtor, "Int32Array", JsTypedArrayKind.Int32);
        InstallType(realm, abstractCtor, "Uint32Array", JsTypedArrayKind.Uint32);
        InstallType(realm, abstractCtor, "Float32Array", JsTypedArrayKind.Float32);
        InstallType(realm, abstractCtor, "Float64Array", JsTypedArrayKind.Float64);
        InstallType(realm, abstractCtor, "BigInt64Array", JsTypedArrayKind.BigInt64);
        InstallType(realm, abstractCtor, "BigUint64Array", JsTypedArrayKind.BigUint64);
    }

    /// <summary>§23.2.1 the %TypedArray% abstract constructor — never directly
    /// constructible; carries <c>from</c>/<c>of</c>/@@species that the eleven
    /// concrete constructors inherit.</summary>
    private static JsNativeFunction InstallAbstractCtor(JsRealm realm, JsObject shared)
    {
        var ctor = new JsNativeFunction("TypedArray", (_, _) =>
            throw new JsThrow(realm.NewTypeError("Abstract class TypedArray not directly constructable")), isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ArrayBufferCtor.DefineData(ctor, "prototype", JsValue.Object(shared), false, false, false);
        ArrayBufferCtor.DefineData(ctor, "name", JsValue.String("TypedArray"), false, false, true);
        ArrayBufferCtor.DefineData(ctor, "length", JsValue.Number(0), false, false, true);
        ArrayBufferCtor.DefineData(shared, "constructor", JsValue.Object(ctor), true, false, true);
        ArrayBufferCtor.DefineMethod(realm, ctor, "from", (thisV, args) => From(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, ctor, "of", (thisV, args) => Of(realm, thisV, args), 0);
        var speciesGetter = new JsNativeFunction(realm, "get [Symbol.species]", 0, (thisV, _) => thisV, isConstructor: false);
        ctor.DefineOwnProperty(SymbolCtor.Species,
            PropertyDescriptor.Accessor(speciesGetter, setter: null, enumerable: false, configurable: true));
        return ctor;
    }

    private static void InstallType(JsRealm realm, JsNativeFunction abstractCtor, string name, JsTypedArrayKind kind)
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

            // §23.2.5.1 step 6.b: for a non-object argument ToIndex runs
            // BEFORE AllocateTypedArray reads new.target.prototype.
            var firstArg = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (!firstArg.IsObject)
            {
                var elementLength = ArrayBufferCtor.ToIndex(realm, firstArg);
                var lenProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto,
                    r => FindRealmTypePrototype(r, name));
                return JsValue.Object(Allocate(realm, lenProto, kind, elementLength));
            }

            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto,
                r => FindRealmTypePrototype(r, name));
            return JsValue.Object(Construct(realm, instProto, kind, args));
        }, isConstructor: true);
        ctor.SetPrototypeOf(abstractCtor);
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

    /// <summary>§9.1.14 GetPrototypeFromConstructor fallback: the named concrete
    /// prototype from the constructor function's realm.</summary>
    private static JsObject FindRealmTypePrototype(JsRealm realm, string name)
    {
        var ctorV = realm.GlobalObject.Get(name);
        if (ctorV.IsObject)
        {
            var protoV = ctorV.AsObject.Get("prototype");
            if (protoV.IsObject)
            {
                return protoV.AsObject;
            }
        }

        return realm.ObjectPrototype;
    }

    private static JsTypedArray Construct(JsRealm realm, JsObject proto, JsTypedArrayKind kind, JsValue[] args)
    {
        var first = args[0];
        if (first.AsObject is JsArrayBuffer buffer)
        {
            return ConstructFromArrayBuffer(realm, proto, kind, buffer, args);
        }

        if (first.AsObject is JsTypedArray source)
        {
            return ConstructFromTypedArray(realm, proto, kind, source);
        }

        return ConstructFromObject(realm, proto, kind, first);
    }

    /// <summary>§23.2.5.1.b InitializeTypedArrayFromArrayBuffer.</summary>
    private static JsTypedArray ConstructFromArrayBuffer(JsRealm realm, JsObject proto, JsTypedArrayKind kind, JsArrayBuffer buffer, JsValue[] args)
    {
        var bpe = JsTypedArray.BytesPerElementOf(kind);
        var offset = args.Length > 1 ? ArrayBufferCtor.ToIndex(realm, args[1]) : 0;
        if (offset % bpe != 0)
        {
            throw new JsThrow(realm.NewRangeError("TypedArray byteOffset must align to element size"));
        }

        var lengthArg = args.Length > 2 ? args[2] : JsValue.Undefined;
        var explicitLength = -1;
        if (!lengthArg.IsUndefined)
        {
            explicitLength = ArrayBufferCtor.ToIndex(realm, lengthArg);
        }

        // The ToIndex coercions above can run user code; detachment is checked
        // only after them (§23.2.5.1.b step 6).
        if (buffer.IsDetached)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray viewed buffer is detached"));
        }

        var bufferByteLength = buffer.ByteLength;
        if (lengthArg.IsUndefined)
        {
            if (buffer.IsResizable)
            {
                if (offset > bufferByteLength)
                {
                    throw new JsThrow(realm.NewRangeError("TypedArray byteOffset out of range"));
                }

                return new JsTypedArray(proto, kind, buffer, offset, 0, lengthTracking: true, realm);
            }

            if (bufferByteLength % bpe != 0)
            {
                throw new JsThrow(realm.NewRangeError("TypedArray byteLength must align to element size"));
            }

            var newByteLength = bufferByteLength - offset;
            if (newByteLength < 0)
            {
                throw new JsThrow(realm.NewRangeError("TypedArray byteOffset out of range"));
            }

            return new JsTypedArray(proto, kind, buffer, offset, newByteLength / bpe, lengthTracking: false, realm);
        }

        var byteLength = (long)explicitLength * bpe;
        if (offset + byteLength > bufferByteLength)
        {
            throw new JsThrow(realm.NewRangeError("TypedArray length out of range"));
        }

        return new JsTypedArray(proto, kind, buffer, offset, explicitLength, lengthTracking: false, realm);
    }

    /// <summary>§23.2.5.1.c InitializeTypedArrayFromTypedArray.</summary>
    private static JsTypedArray ConstructFromTypedArray(JsRealm realm, JsObject proto, JsTypedArrayKind kind, JsTypedArray source)
    {
        if (source.IsOutOfBounds)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray source is detached or out of bounds"));
        }

        var targetIsBig = kind is JsTypedArrayKind.BigInt64 or JsTypedArrayKind.BigUint64;
        if (targetIsBig != source.IsBigIntKind)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray content type mismatch"));
        }

        var len = source.Length;
        var target = Allocate(realm, proto, kind, len);
        for (var i = 0; i < len; i++)
        {
            target.SetElement(i, source.GetElement(i), realm);
        }

        return target;
    }

    /// <summary>§23.2.5.1.e InitializeTypedArrayFromArrayLike (plus the
    /// @@iterator branch of step 8).</summary>
    private static JsTypedArray ConstructFromObject(JsRealm realm, JsObject proto, JsTypedArrayKind kind, JsValue first)
    {
        var vm = realm.ActiveVm;
        var usingIterator = AbstractOperations.GetMethod(vm, first, SymbolCtor.Iterator);
        if (!usingIterator.IsUndefined)
        {
            var values = IteratorToList(realm, first, usingIterator);
            var target = Allocate(realm, proto, kind, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                target.SetElement(i, values[i], realm);
            }

            return target;
        }

        var src = first.AsObject;
        var len = LengthOfArrayLike(realm, src);
        var arrayLike = Allocate(realm, proto, kind, len);
        for (var i = 0; i < len; i++)
        {
            arrayLike.SetElement(i, AbstractOperations.Get(vm, src, ArrayBufferCtor.IndexKey(i)), realm);
        }

        return arrayLike;
    }

    /// <summary>§7.4.3 GetIteratorFromMethod + §7.4.14 IteratorToList. The
    /// caller passes the already-fetched @@iterator method so the getter is not
    /// observably read twice; pass undefined to fetch it here (string sources).</summary>
    private static List<JsValue> IteratorToList(JsRealm realm, JsValue iterable, JsValue method)
    {
        var vm = realm.ActiveVm;
        IteratorRecord record;
        if (method.IsUndefined || !method.IsObject)
        {
            record = AbstractOperations.GetIterator(realm, vm, iterable);
        }
        else
        {
            var iterObj = AbstractOperations.Call(vm, method, iterable, Array.Empty<JsValue>());
            if (!iterObj.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("iterator method did not return an object"));
            }

            var nextMethod = AbstractOperations.Get(vm, iterObj.AsObject, "next");
            record = new IteratorRecord(iterObj, nextMethod, Done: false);
        }

        var values = new List<JsValue>();
        while (AbstractOperations.IteratorStep(realm, vm, ref record) is { } result)
        {
            values.Add(AbstractOperations.IteratorValue(vm, result));
        }

        return values;
    }

    /// <summary>§7.3.19 LengthOfArrayLike clamped to the allocatable range.</summary>
    private static int LengthOfArrayLike(JsRealm realm, JsObject src)
    {
        var lenV = AbstractOperations.Get(realm.ActiveVm, src, "length");
        var n = ArrayBufferCtor.ToIntegerOrInfinity(realm, lenV);
        if (n <= 0)
        {
            return 0;
        }

        if (n > int.MaxValue)
        {
            throw new JsThrow(realm.NewRangeError("TypedArray length out of range"));
        }

        return (int)n;
    }

    private static JsTypedArray Allocate(JsRealm realm, JsObject proto, JsTypedArrayKind kind, int length)
    {
        var bpe = JsTypedArray.BytesPerElementOf(kind);
        if (length > int.MaxValue / bpe)
        {
            throw new JsThrow(realm.NewRangeError("TypedArray length out of range"));
        }

        return new JsTypedArray(proto, kind, new JsArrayBuffer(realm.ArrayBufferPrototype, length * bpe), 0, length, lengthTracking: false, realm);
    }

    /// <summary>§23.2.4.2 TypedArrayCreate + §23.2.4.3 ValidateTypedArray.</summary>
    private static JsTypedArray TypedArrayCreate(JsRealm realm, JsValue ctor, JsValue[] args, int? mustHaveLength)
    {
        var result = AbstractOperations.Construct(realm.ActiveVm, ctor, args);
        if (!result.IsObject || result.AsObject is not JsTypedArray ta)
        {
            throw new JsThrow(realm.NewTypeError("Constructor did not return a TypedArray"));
        }

        if (ta.IsOutOfBounds)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray is detached or out of bounds"));
        }

        // Immutable-ArrayBuffer proposal: a view created to be written into
        // must not be backed by an immutable buffer.
        if (ta.Buffer.IsImmutable)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray is backed by an immutable ArrayBuffer"));
        }

        if (mustHaveLength is { } required && ta.Length < required)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray is too small"));
        }

        return ta;
    }

    /// <summary>Write during %TypedArray%.from/of — spec Set(…, throw=true), so a
    /// write refused by an immutable backing buffer is a TypeError.</summary>
    private static void SetCreated(JsRealm realm, JsTypedArray target, int index, JsValue value)
    {
        if (target.Buffer.IsImmutable)
        {
            throw new JsThrow(realm.NewTypeError("Cannot write into an immutable ArrayBuffer"));
        }

        target.SetIntegerIndexed(index, value, realm);
    }

    private static JsValue From(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!AbstractOperations.IsConstructor(thisV))
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.from requires a constructor receiver"));
        }

        var vm = realm.ActiveVm;
        var source = args.Length > 0 ? args[0] : JsValue.Undefined;
        var mapFn = args.Length > 1 ? args[1] : JsValue.Undefined;
        var thisArg = args.Length > 2 ? args[2] : JsValue.Undefined;
        if (!mapFn.IsUndefined && !AbstractOperations.IsCallable(mapFn))
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.from mapFn must be callable"));
        }

        var usingIterator = source.IsString
            ? JsValue.Undefined
            : AbstractOperations.GetMethod(vm, source, SymbolCtor.Iterator);
        if (source.IsString || !usingIterator.IsUndefined)
        {
            var values = IteratorToList(realm, source, usingIterator);
            var target = TypedArrayCreate(realm, thisV, new[] { JsValue.Number(values.Count) }, values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                var v = values[i];
                if (!mapFn.IsUndefined)
                {
                    v = AbstractOperations.Call(vm, mapFn, thisArg, new[] { v, JsValue.Number(i) });
                }

                SetCreated(realm, target, i, v);
            }

            return JsValue.Object(target);
        }

        if (source.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.from source must be array-like"));
        }

        var arrayLike = AbstractOperations.ToObject(realm, source);
        var len = LengthOfArrayLike(realm, arrayLike);
        var result = TypedArrayCreate(realm, thisV, new[] { JsValue.Number(len) }, len);
        for (var i = 0; i < len; i++)
        {
            var v = AbstractOperations.Get(vm, arrayLike, ArrayBufferCtor.IndexKey(i));
            if (!mapFn.IsUndefined)
            {
                v = AbstractOperations.Call(vm, mapFn, thisArg, new[] { v, JsValue.Number(i) });
            }

            SetCreated(realm, result, i, v);
        }

        return JsValue.Object(result);
    }

    private static JsValue Of(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!AbstractOperations.IsConstructor(thisV))
        {
            throw new JsThrow(realm.NewTypeError("TypedArray.of requires a constructor receiver"));
        }

        var target = TypedArrayCreate(realm, thisV, new[] { JsValue.Number(args.Length) }, args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            SetCreated(realm, target, i, args[i]);
        }

        return JsValue.Object(target);
    }

    private static void InstallSharedPrototype(JsRealm realm, JsObject proto)
    {
        ArrayBufferCtor.DefineMethod(realm, proto, "at", (thisV, args) => At(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "copyWithin", (thisV, args) => CopyWithin(realm, thisV, args), 2);
        ArrayBufferCtor.DefineMethod(realm, proto, "entries", (thisV, args) => Entries(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(realm, proto, "every", (thisV, args) => Every(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "fill", (thisV, args) => Fill(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "filter", (thisV, args) => Filter(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "find", (thisV, args) => Find(realm, thisV, args, last: false, indexOnly: false), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "findIndex", (thisV, args) => Find(realm, thisV, args, last: false, indexOnly: true), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "findLast", (thisV, args) => Find(realm, thisV, args, last: true, indexOnly: false), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "findLastIndex", (thisV, args) => Find(realm, thisV, args, last: true, indexOnly: true), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "forEach", (thisV, args) => ForEach(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "includes", (thisV, args) => Includes(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "indexOf", (thisV, args) => IndexOf(realm, thisV, args, last: false), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "join", (thisV, args) => Join(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "keys", (thisV, args) => Keys(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(realm, proto, "lastIndexOf", (thisV, args) => IndexOf(realm, thisV, args, last: true), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "map", (thisV, args) => Map(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "reduce", (thisV, args) => Reduce(realm, thisV, args, right: false), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "reduceRight", (thisV, args) => Reduce(realm, thisV, args, right: true), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "reverse", (thisV, args) => Reverse(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(realm, proto, "set", (thisV, args) => Set(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "slice", (thisV, args) => Slice(realm, thisV, args), 2);
        ArrayBufferCtor.DefineMethod(realm, proto, "some", (thisV, args) => Some(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "sort", (thisV, args) => Sort(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "subarray", (thisV, args) => Subarray(realm, thisV, args), 2);
        ArrayBufferCtor.DefineMethod(realm, proto, "toLocaleString", (thisV, args) => TypedToLocaleString(realm, thisV, args), 0);
        ArrayBufferCtor.DefineMethod(realm, proto, "toReversed", (thisV, args) => ToReversed(realm, thisV), 0);
        ArrayBufferCtor.DefineMethod(realm, proto, "toSorted", (thisV, args) => ToSorted(realm, thisV, args), 1);
        ArrayBufferCtor.DefineMethod(realm, proto, "toString", (thisV, args) => Join(realm, thisV, args), 0);
        var valuesFn = IntrinsicHelpers.DefineMethod(realm, proto, "values", 0,
            (thisV, args) => Values(realm, thisV));
        ArrayBufferCtor.DefineMethod(realm, proto, "with", (thisV, args) => With(realm, thisV, args), 2);

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

        // §23.2.3.{1,2,3,18} buffer / byteLength / byteOffset / length accessors.
        DefineTaAccessor(realm, proto, "buffer", ta => JsValue.Object(ta.Buffer));
        DefineTaAccessor(realm, proto, "byteLength", ta => JsValue.Number(ta.ByteLength));
        DefineTaAccessor(realm, proto, "byteOffset", ta => JsValue.Number(ta.IsOutOfBounds ? 0 : ta.ByteOffset));
        DefineTaAccessor(realm, proto, "length", ta => JsValue.Number(ta.Length));
    }

    private static void DefineTaAccessor(JsRealm realm, JsObject proto, string name, Func<JsTypedArray, JsValue> getter)
    {
        var fn = new JsNativeFunction(realm, "get " + name, 0, (thisV, _) =>
        {
            if (thisV.IsObject && thisV.AsObject is JsTypedArray ta)
            {
                return getter(ta);
            }

            throw new JsThrow(realm.NewTypeError("TypedArray." + name + " called on incompatible receiver"));
        }, isConstructor: false);
        proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(fn, setter: null, enumerable: false, configurable: true));
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
        var result = Allocate(realm, TypePrototype(realm, ta), ta.Kind, ta.Length);
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
        // §23.2.3.29 — a non-callable, non-undefined comparator is a TypeError
        // before any element is read.
        var compare = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!compare.IsUndefined && !AbstractOperations.IsCallable(compare))
        {
            throw new JsThrow(realm.NewTypeError("The comparison function must be either a function or undefined"));
        }

        var ta = ThisTA(realm, thisV);
        var values = ToList(realm, ta);
        try
        {
            values.Sort((a, b) =>
            {
                if (AbstractOperations.IsCallable(compare))
                {
                    var r = ArrayBufferCtor.Number(AbstractOperations.Call(realm.ActiveVm, compare, JsValue.Undefined, new[] { a, b }));
                    return double.IsNaN(r) ? 0 : Math.Sign(r);
                }

                // Default sort: BigInt elements compare as BigIntegers, numbers
                // per §23.2.4.7 (NaN sorts last; -0 before +0).
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
        var result = SpeciesAllocate(realm, ta, ta.Length);
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

        for (; right ? i >= 0 : i < ta.Length; i += right ? -1 : 1)
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
        for (var i = start; i < ta.Length; i++)
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
            for (var i = start; i < ta.Length; i++)
            {
                if (JsValue.StrictEquals(ta.GetElement(i), search))
                {
                    return JsValue.Number(i);
                }
            }
        }
        return JsValue.Number(-1);
    }

    private static JsValue TypedToLocaleString(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var ta = ThisTA(realm, thisV);
        var vm = realm.ActiveVm;
        var forwarded = new JsValue[2];
        forwarded[0] = args.Length > 0 ? args[0] : JsValue.Undefined;
        forwarded[1] = args.Length > 1 ? args[1] : JsValue.Undefined;
        var sb = new System.Text.StringBuilder(16);
        var len = ta.Length;
        for (var i = 0; i < len; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            var element = ta.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (element.IsUndefined || element.IsNull)
            {
                continue;
            }

            var fn = AbstractOperations.Get(vm, AbstractOperations.ToObject(realm, element), "toLocaleString");
            if (!fn.IsObject || !AbstractOperations.IsCallable(fn))
            {
                throw new JsThrow(realm.NewTypeError("element toLocaleString is not callable"));
            }

            var text = AbstractOperations.Call(vm, fn, element, forwarded);
            sb.Append(AbstractOperations.ToStringJs(vm, text));
        }

        return JsValue.String(sb.ToString());
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
