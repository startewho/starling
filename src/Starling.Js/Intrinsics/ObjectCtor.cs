using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §20.1 The Object intrinsic. Populates <see cref="JsRealm.ObjectConstructor"/>
/// and <see cref="JsRealm.ObjectPrototype"/>, then registers <c>Object</c> on
/// the realm's global object.
/// </summary>
/// <remarks>
/// <para>
/// Array-returning methods (<c>keys</c>, <c>values</c>, <c>entries</c>,
/// <c>getOwnPropertyNames</c>, and <c>getOwnPropertySymbols</c>) return real
/// <see cref="JsArray"/> instances.
/// </para>
/// </remarks>
public static class ObjectCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var objectProto = realm.ObjectPrototype;

        // ----------------------------------------------------------- Constructor
        // §20.1.1 The Object Constructor — called as Object(v) or new Object(v).
        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction("Object", (newTarget, args) =>
        {
            // §20.1.1.1 step 1: when constructed with a new.target that is NOT
            // the Object constructor itself (i.e. a subclass via super()),
            // OrdinaryCreateFromConstructor an object prototyped from new.target.
            // `class S extends Object {}; new S()` then yields an S-prototyped
            // instance regardless of any argument.
            if (newTarget.IsObject && AbstractOperations.IsConstructor(newTarget)
                && !ReferenceEquals(newTarget.AsObject, ctor))
            {
                var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, objectProto, static r => r.ObjectPrototype);
                return JsValue.Object(realm.NewObjectWithProto(instProto));
            }
            var value = args.Length > 0 ? args[0] : JsValue.Undefined;
            // When `value` is undefined or null → fresh ordinary object.
            if (value.IsNullish)
            {
                return JsValue.Object(realm.NewOrdinaryObject());
            }
            // Otherwise per §7.1.18 ToObject (objects pass through unchanged).
            return JsValue.Object(AbstractOperations.ToObject(realm, value));
        }, isConstructor: true);
        // Object.[[Prototype]] = Function.prototype
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        // Object.prototype is a non-writable, non-enumerable, non-configurable slot
        // on the constructor (§20.1.2.1).
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(objectProto), writable: false, enumerable: false, configurable: false));
        // §10.2.9/§10.2.10 order: length is installed before name.
        ctor.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(1), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("Object"), writable: false, enumerable: false, configurable: true));
        // -------------------------------------------------------- Static methods
        DefineMethod(realm, ctor, "assign", (thisV, args) => Assign(realm, thisV, args), length: 2);
        DefineMethod(realm, ctor, "create", (thisV, args) => Create(realm, args), length: 2);
        DefineMethod(realm, ctor, "defineProperty", (thisV, args) => DefineProperty(realm, args), length: 3);
        DefineMethod(realm, ctor, "defineProperties", (thisV, args) => DefineProperties(realm, args), length: 2);
        DefineMethod(realm, ctor, "getOwnPropertyDescriptor", (thisV, args) => GetOwnPropertyDescriptor(realm, args), length: 2);
        DefineMethod(realm, ctor, "getOwnPropertyDescriptors", (thisV, args) => GetOwnPropertyDescriptors(realm, args), length: 1);
        DefineMethod(realm, ctor, "getOwnPropertyNames", (thisV, args) => GetOwnPropertyNames(realm, args), length: 1);
        DefineMethod(realm, ctor, "getOwnPropertySymbols", (thisV, args) => GetOwnPropertySymbols(realm, args), length: 1);
        DefineMethod(realm, ctor, "getPrototypeOf", (thisV, args) => GetPrototypeOf(realm, args), length: 1);
        DefineMethod(realm, ctor, "setPrototypeOf", (thisV, args) => SetPrototypeOf(realm, args), length: 2);
        DefineMethod(realm, ctor, "keys", (thisV, args) => Keys(realm, args), length: 1);
        DefineMethod(realm, ctor, "values", (thisV, args) => Values(realm, args), length: 1);
        DefineMethod(realm, ctor, "entries", (thisV, args) => Entries(realm, args), length: 1);
        DefineMethod(realm, ctor, "freeze", (thisV, args) => SetIntegrityLevel(realm, args, frozen: true), length: 1);
        DefineMethod(realm, ctor, "isFrozen", (thisV, args) => IsFrozen(args), length: 1);
        DefineMethod(realm, ctor, "seal", (thisV, args) => SetIntegrityLevel(realm, args, frozen: false), length: 1);
        DefineMethod(realm, ctor, "isSealed", (thisV, args) => IsSealed(args), length: 1);
        DefineMethod(realm, ctor, "preventExtensions", (thisV, args) => PreventExtensions(realm, args), length: 1);
        DefineMethod(realm, ctor, "isExtensible", (thisV, args) => IsExtensible(args), length: 1);
        DefineMethod(realm, ctor, "is", (thisV, args) => Is(args), length: 2);
        DefineMethod(realm, ctor, "fromEntries", (thisV, args) => FromEntries(realm, args), length: 1);
        DefineMethod(realm, ctor, "groupBy", (thisV, args) => GroupBy(realm, args), length: 2);
        DefineMethod(realm, ctor, "hasOwn", (thisV, args) => HasOwn(realm, args), length: 2);

        // -------------------------------------------------------- Prototype methods
        // Bulk-install the constructor back-reference + the six prototype methods
        // by adopting one precomputed shape. Creation order (and thus
        // getOwnPropertyNames order) is exactly: constructor, hasOwnProperty,
        // isPrototypeOf, propertyIsEnumerable, toString, valueOf, toLocaleString —
        // unchanged from the prior sequential install. All are string-keyed builtin
        // data properties (W=true/E=false/C=true), so the result is byte-identical.
        IntrinsicHelpers.BulkInstallBuiltins(realm, objectProto, new[]
        {
            new IntrinsicHelpers.BulkMember("constructor", 0, null, JsValue.Object(ctor)),
            new IntrinsicHelpers.BulkMember("hasOwnProperty", 1, (thisV, args) => ProtoHasOwnProperty(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("isPrototypeOf", 1, (thisV, args) => ProtoIsPrototypeOf(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("propertyIsEnumerable", 1, (thisV, args) => ProtoPropertyIsEnumerable(realm, thisV, args)),
            new IntrinsicHelpers.BulkMember("toString", 0, (thisV, args) => ProtoToString(realm, thisV)),
            new IntrinsicHelpers.BulkMember("valueOf", 0, (thisV, args) => ProtoValueOf(realm, thisV)),
            new IntrinsicHelpers.BulkMember("toLocaleString", 0, (thisV, args) => ProtoToLocaleString(realm, thisV)),
        });

        realm.ObjectProtoToString = objectProto.Get("toString");

        // §B.2.2.2-5 __defineGetter__ / __defineSetter__ / __lookupGetter__ /
        // __lookupSetter__ (Annex B legacy accessor helpers).
        DefineMethod(realm, objectProto, "__defineGetter__",
            (thisV, args) => DunderDefineAccessor(realm, thisV, args, isGetter: true), length: 2);
        DefineMethod(realm, objectProto, "__defineSetter__",
            (thisV, args) => DunderDefineAccessor(realm, thisV, args, isGetter: false), length: 2);
        DefineMethod(realm, objectProto, "__lookupGetter__",
            (thisV, args) => DunderLookupAccessor(realm, thisV, args, isGetter: true), length: 1);
        DefineMethod(realm, objectProto, "__lookupSetter__",
            (thisV, args) => DunderLookupAccessor(realm, thisV, args, isGetter: false), length: 1);

        // §B.2.2.1 Object.prototype.__proto__ accessor.
        var protoGetter = new JsNativeFunction(realm, "get __proto__", 0, (thisV, _) =>
        {
            var o = AbstractOperations.ToObject(realm, thisV);
            var p = o.GetPrototypeOf();
            return p is null ? JsValue.Null : JsValue.Object(p);
        }, isConstructor: false);
        var protoSetter = new JsNativeFunction(realm, "set __proto__", 1, (thisV, args) =>
        {
            if (thisV.IsNullish)
            {
                throw new JsThrow(realm.NewTypeError("Object.prototype.__proto__ setter called on null or undefined"));
            }

            var v = args.Length > 0 ? args[0] : JsValue.Undefined;
            if (!v.IsObject && !v.IsNull)
            {
                return JsValue.Undefined;
            }

            if (!thisV.IsObject)
            {
                return JsValue.Undefined;
            }

            if (!thisV.AsObject.SetPrototypeOf(v.IsNull ? null : v.AsObject))
            {
                throw new JsThrow(realm.NewTypeError("Object.prototype.__proto__ setter: cyclic or non-extensible"));
            }

            return JsValue.Undefined;
        }, isConstructor: false);
        objectProto.DefineOwnProperty("__proto__",
            PropertyDescriptor.Accessor(protoGetter, protoSetter, enumerable: false, configurable: true));

        realm.ObjectConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Object",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    // ====================================================================
    //                            Helpers
    // ====================================================================

    /// <summary>Install a builtin method descriptor (W=true, E=false, C=true)
    /// per §17 default attributes for built-in functions. B2-2: routed through
    /// the realm-aware <see cref="JsNativeFunction"/> ctor so each method
    /// inherits from <c>Function.prototype</c>.</summary>
    private static void DefineMethod(JsRealm realm, JsObject target, string name,
        Func<JsValue, JsValue[], JsValue> body, int length)
    {
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor: false);
        target.DefineOwnProperty(name, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
    }

    /// <summary>RequireObjectCoercible-then-ToObject. Mirrors what most spec
    /// statics do at entry.</summary>
    private static JsObject RequireObject(JsRealm realm, JsValue v)
    {
        if (v.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("Cannot convert undefined or null to object"));
        }

        if (!v.IsObject)
        {
            return AbstractOperations.ToObject(realm, v);
        }

        return v.AsObject;
    }

    /// <summary>Build a freshly-allocated dense array (B2-4) populated with
    /// the given elements. Used by <c>Object.keys</c>, <c>values</c>,
    /// <c>entries</c>, <c>getOwnPropertyNames</c>, and
    /// <c>getOwnPropertySymbols</c> so the returned values are real arrays
    /// (<c>Array.isArray</c> ⇒ true).</summary>
    private static JsValue MakeArrayLike(JsRealm realm, IReadOnlyList<JsValue> items)
    {
        var arr = new JsArray(realm);
        for (var i = 0; i < items.Count; i++)
        {
            arr.Push(items[i]);
        }

        return JsValue.Object(arr);
    }

    /// <summary>§6.2.5.5 FromPropertyDescriptor — build a plain JS object from
    /// a host descriptor (data → {value, writable, enumerable, configurable},
    /// accessor → {get, set, enumerable, configurable}).</summary>
    private static JsValue FromPropertyDescriptor(JsRealm realm, PropertyDescriptor d)
    {
        var obj = realm.NewOrdinaryObject();
        if (d.IsAccessor)
        {
            obj.Set("get", d.Getter is null ? JsValue.Undefined : JsValue.Object(d.Getter));
            obj.Set("set", d.Setter is null ? JsValue.Undefined : JsValue.Object(d.Setter));
        }
        else
        {
            obj.Set("value", d.Value);
            obj.Set("writable", JsValue.Boolean(d.Writable));
        }
        obj.Set("enumerable", JsValue.Boolean(d.Enumerable));
        obj.Set("configurable", JsValue.Boolean(d.Configurable));
        return JsValue.Object(obj);
    }

    /// <summary>§6.2.5.6 ToPropertyDescriptor — read a JS object's hint
    /// fields and produce a host <see cref="PropertyDescriptor"/>. Throws
    /// TypeError on data+accessor mix per spec.</summary>
    private static PropertyDescriptor ToPropertyDescriptor(JsRealm realm, JsValue input)
    {
        if (!input.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Property descriptor must be an object"));
        }

        var obj = input.AsObject;

        var hasEnumerable = AbstractOperations.HasProperty(obj, "enumerable");
        var enumerable = hasEnumerable
            && JsValue.ToBoolean(AbstractOperations.Get(realm.ActiveVm, obj, "enumerable"));

        var hasConfigurable = AbstractOperations.HasProperty(obj, "configurable");
        var configurable = hasConfigurable
            && JsValue.ToBoolean(AbstractOperations.Get(realm.ActiveVm, obj, "configurable"));

        var hasValue = AbstractOperations.HasProperty(obj, "value");
        var value = hasValue
            ? AbstractOperations.Get(realm.ActiveVm, obj, "value")
            : JsValue.Undefined;

        var hasWritable = AbstractOperations.HasProperty(obj, "writable");
        var writable = hasWritable
            && JsValue.ToBoolean(AbstractOperations.Get(realm.ActiveVm, obj, "writable"));

        JsObject? getter = null;
        var hasGet = AbstractOperations.HasProperty(obj, "get");
        if (hasGet)
        {
            var g = AbstractOperations.Get(realm.ActiveVm, obj, "get");
            if (!g.IsUndefined)
            {
                if (!AbstractOperations.IsCallable(g))
                {
                    throw new JsThrow(realm.NewTypeError("Getter must be a function"));
                }

                getter = g.AsObject;
            }
        }

        JsObject? setter = null;
        var hasSet = AbstractOperations.HasProperty(obj, "set");
        if (hasSet)
        {
            var s = AbstractOperations.Get(realm.ActiveVm, obj, "set");
            if (!s.IsUndefined)
            {
                if (!AbstractOperations.IsCallable(s))
                {
                    throw new JsThrow(realm.NewTypeError("Setter must be a function"));
                }

                setter = s.AsObject;
            }
        }

        if ((hasValue || hasWritable) && (hasGet || hasSet))
        {
            throw new JsThrow(realm.NewTypeError(
                "Invalid property descriptor. Cannot both specify accessors and a value or writable attribute"));
        }

        if (hasGet || hasSet)
        {
            return PropertyDescriptor.Accessor(getter, setter, enumerable, configurable);
        }

        return PropertyDescriptor.Data(value, writable, enumerable, configurable);
    }

    // ====================================================================
    //                          Static implementations
    // ====================================================================

    /// <summary>§20.1.2.1 Object.assign — copy own enumerable string- AND
    /// symbol-keyed properties from each source (via [[OwnPropertyKeys]] so a
    /// Proxy's ownKeys trap fires and order is per-source key order), with
    /// throwing Set semantics on the target.</summary>
    private static JsValue Assign(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var vm = realm.ActiveVm;
        var to = AbstractOperations.ToObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        for (var i = 1; i < args.Length; i++)
        {
            var src = args[i];
            if (src.IsNullish)
            {
                continue;
            }

            var from = AbstractOperations.ToObject(realm, src);
            var keys = new List<JsPropertyKey>(from.OwnPropertyKeys);
            foreach (var key in keys)
            {
                var d = from.GetOwnPropertyDescriptor(key);
                if (d is not { Enumerable: true })
                {
                    continue;
                }

                var v = AbstractOperations.Get(vm, from, key);
                if (!AbstractOperations.Set(vm, to, key, v))
                {
                    throw new JsThrow(realm.NewTypeError($"Cannot assign to read only property '{key}'"));
                }
            }
        }
        return JsValue.Object(to);
    }

    /// <summary>§20.1.2.2 Object.create — create a fresh object with the
    /// supplied prototype and optionally apply a descriptor map.</summary>
    private static JsValue Create(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0)
        {
            throw new JsThrow(realm.NewTypeError("Object.create: prototype must be Object or null"));
        }

        var proto = args[0];
        JsObject? protoObj;
        if (proto.IsNull)
        {
            protoObj = null;
        }
        else if (proto.IsObject)
        {
            protoObj = proto.AsObject;
        }
        else
        {
            throw new JsThrow(realm.NewTypeError("Object.create: prototype must be Object or null"));
        }

        var obj = realm.NewObjectWithProto(protoObj);
        if (args.Length >= 2 && !args[1].IsUndefined)
        {
            ApplyDescriptors(realm, obj, args[1]);
        }
        return JsValue.Object(obj);
    }

    /// <summary>§20.1.2.3 Object.defineProperties.</summary>
    private static JsValue DefineProperties(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Object.defineProperties called on non-object"));
        }

        var target = args[0].AsObject;
        ApplyDescriptors(realm, target, args.Length > 1 ? args[1] : JsValue.Undefined);
        return args[0];
    }

    private static void ApplyDescriptors(JsRealm realm, JsObject target, JsValue props)
    {
        // §20.1.2.3.1 step 2 — ToObject(Properties): primitives are legal
        // (they just contribute no own enumerable keys); nullish throws.
        var p = AbstractOperations.ToObject(realm, props);
        // Two phases: read EVERY descriptor first (through the vm so
        // accessor-provided descriptors run, and so a bad descriptor throws
        // before ANY define lands), then apply in order. Symbol keys count.
        var keys = new List<JsPropertyKey>(p.OwnPropertyKeys);
        var pending = new List<(JsPropertyKey Key, PropertyDescriptor Desc, JsObject Source)>();
        foreach (var key in keys)
        {
            var d = p.GetOwnPropertyDescriptor(key);
            if (d is not { Enumerable: true })
            {
                continue;
            }

            var descVal = AbstractOperations.Get(realm.ActiveVm, p, key);
            var desc = ToPropertyDescriptor(realm, descVal);
            pending.Add((key, desc, descVal.AsObject));
        }

        foreach (var (key, desc, source) in pending)
        {
            if (!Starling.Js.Runtime.JsMappedArguments.DefineFromUser(target, key, desc, source))
            {
                throw new JsThrow(realm.NewTypeError($"Cannot define property '{key}'"));
            }
        }
    }

    /// <summary>§20.1.2.4 Object.defineProperty.</summary>
    private static JsValue DefineProperty(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Object.defineProperty called on non-object"));
        }

        if (args.Length < 2)
        {
            throw new JsThrow(realm.NewTypeError("Object.defineProperty requires a property key"));
        }

        if (args.Length < 3)
        {
            throw new JsThrow(realm.NewTypeError("Object.defineProperty requires a descriptor"));
        }

        var target = args[0].AsObject;
        var key = AbstractOperations.ToPropertyKey(realm.ActiveVm, args[1]);
        var desc = ToPropertyDescriptor(realm, args[2]);
        if (!Starling.Js.Runtime.JsMappedArguments.DefineFromUser(target, key, desc, args[2].AsObject))
        {
            throw new JsThrow(realm.NewTypeError($"Cannot redefine property '{key}'"));
        }

        return args[0];
    }

    /// <summary>§20.1.2.8 Object.getOwnPropertyDescriptor.</summary>
    private static JsValue GetOwnPropertyDescriptor(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var key = AbstractOperations.ToPropertyKey(realm.ActiveVm, args.Length > 1 ? args[1] : JsValue.Undefined);
        var d = target.GetOwnPropertyDescriptor(key);
        if (d is null)
        {
            return JsValue.Undefined;
        }

        return FromPropertyDescriptor(realm, d.Value);
    }

    /// <summary>§20.1.2.9 Object.getOwnPropertyDescriptors.</summary>
    private static JsValue GetOwnPropertyDescriptors(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var bag = realm.NewOrdinaryObject();
        foreach (var key in target.OwnPropertyKeys)
        {
            var d = target.GetOwnPropertyDescriptor(key);
            if (d is null)
            {
                continue;
            }

            bag.DefineOwnProperty(key,
                PropertyDescriptor.Data(FromPropertyDescriptor(realm, d.Value), writable: true, enumerable: true, configurable: true));
        }
        return JsValue.Object(bag);
    }

    /// <summary>§20.1.2.10 Object.getOwnPropertyNames.</summary>
    private static JsValue GetOwnPropertyNames(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var names = new List<JsValue>();
        foreach (var k in target.Keys)
        {
            names.Add(JsValue.String(k));
        }

        return MakeArrayLike(realm, names);
    }

    /// <summary>§20.1.2.11 Object.getOwnPropertySymbols.</summary>
    private static JsValue GetOwnPropertySymbols(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var symbols = new List<JsValue>();
        // §7.3.23 GetOwnPropertyKeys — route through [[OwnPropertyKeys]] so a
        // Proxy's ownKeys trap (and its invariant checks) fires even though
        // only the symbol keys survive the filter.
        foreach (var k in target.OwnPropertyKeys)
        {
            if (k.IsSymbol)
            {
                symbols.Add(JsValue.Symbol(k.AsSymbol));
            }
        }

        return MakeArrayLike(realm, symbols);
    }

    /// <summary>§20.1.2.12 Object.getPrototypeOf. Goes through the virtual
    /// <see cref="JsObject.GetPrototypeOf"/> so proxies' getPrototypeOf trap fires.</summary>
    private static JsValue GetPrototypeOf(JsRealm realm, JsValue[] args)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var proto = target.GetPrototypeOf();
        return proto is null ? JsValue.Null : JsValue.Object(proto);
    }

    /// <summary>§20.1.2.22 Object.setPrototypeOf.</summary>
    private static JsValue SetPrototypeOf(JsRealm realm, JsValue[] args)
    {
        if (args.Length == 0 || args[0].IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf called on null or undefined"));
        }

        if (args.Length < 2)
        {
            throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf requires a prototype argument"));
        }

        var proto = args[1];
        JsObject? protoObj;
        if (proto.IsNull)
        {
            protoObj = null;
        }
        else if (proto.IsObject)
        {
            protoObj = proto.AsObject;
        }
        else
        {
            throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf: prototype must be Object or null"));
        }
        // If target is a primitive, ToObject would box it (and the box is
        // discarded). Spec: SetPrototypeOf on a non-object is a no-op return.
        if (!args[0].IsObject)
        {
            return args[0];
        }

        if (!args[0].AsObject.SetPrototypeOf(protoObj))
        {
            throw new JsThrow(realm.NewTypeError("Object.setPrototypeOf: cycle detected or non-extensible"));
        }

        return args[0];
    }

    /// <summary>§7.3.24 EnumerableOwnProperties — a SNAPSHOT of the own
    /// string keys is taken first (a getter may add/remove keys mid-walk),
    /// each key re-checked for presence + enumerability, values read through
    /// the vm so getters fire.</summary>
    private static JsValue EnumerableOwnProperties(JsRealm realm, JsValue[] args, bool wantKeys, bool wantValues)
    {
        var target = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var vm = realm.ActiveVm;
        var keys = new List<JsPropertyKey>(target.OwnPropertyKeys);
        var results = new List<JsValue>(keys.Count);
        foreach (var key in keys)
        {
            if (key.IsSymbol)
            {
                continue;
            }

            var d = target.GetOwnPropertyDescriptor(key);
            if (d is not { Enumerable: true })
            {
                continue;
            }

            if (!wantValues)
            {
                results.Add(JsValue.String(key.AsString));
                continue;
            }

            var v = AbstractOperations.Get(vm, target, key);
            results.Add(wantKeys
                ? MakeArrayLike(realm, new[] { JsValue.String(key.AsString), v })
                : v);
        }
        return MakeArrayLike(realm, results);
    }

    /// <summary>§20.1.2.18 Object.keys.</summary>
    private static JsValue Keys(JsRealm realm, JsValue[] args)
        => EnumerableOwnProperties(realm, args, wantKeys: true, wantValues: false);

    /// <summary>§20.1.2.23 Object.values.</summary>
    private static JsValue Values(JsRealm realm, JsValue[] args)
        => EnumerableOwnProperties(realm, args, wantKeys: false, wantValues: true);

    /// <summary>§20.1.2.5 Object.entries.</summary>
    private static JsValue Entries(JsRealm realm, JsValue[] args)
        => EnumerableOwnProperties(realm, args, wantKeys: true, wantValues: true);

    /// <summary>§7.3.16 SetIntegrityLevel (Object.freeze / Object.seal):
    /// [[PreventExtensions]] FIRST (its rejection — e.g. a Proxy trap
    /// returning false — is a TypeError), then partial DefinePropertyOrThrow
    /// per key: sealed = {configurable:false}; frozen additionally clears
    /// [[Writable]] on data properties.</summary>
    private static JsValue SetIntegrityLevel(JsRealm realm, JsValue[] args, bool frozen)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject)
        {
            return v;
        }

        var obj = v.AsObject;
        if (!obj.PreventExtensions())
        {
            throw new JsThrow(realm.NewTypeError("Cannot prevent extensions"));
        }

        // Snapshot keys to avoid mutation-during-enumeration.
        var keys = new List<JsPropertyKey>(obj.OwnPropertyKeys);
        var sealOnly = DescriptorFields.Build(value: false, writable: false, enumerable: false,
            configurable: true, get: false, set: false);
        var freezeData = DescriptorFields.Build(value: false, writable: true, enumerable: false,
            configurable: true, get: false, set: false);
        foreach (var key in keys)
        {
            DescriptorFields fields;
            if (!frozen)
            {
                fields = sealOnly;
            }
            else
            {
                var d = obj.GetOwnPropertyDescriptor(key);
                if (d is null)
                {
                    continue;
                }

                fields = d.Value.IsAccessor ? sealOnly : freezeData;
            }

            if (!obj.DefineOwnPropertyPartial(key,
                    PropertyDescriptor.Data(JsValue.Undefined, writable: false, enumerable: false, configurable: false),
                    fields))
            {
                throw new JsThrow(realm.NewTypeError($"Cannot redefine property '{key}'"));
            }
        }
        return v;
    }

    /// <summary>§20.1.2.15 Object.isFrozen.</summary>
    private static JsValue IsFrozen(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject)
        {
            return JsValue.True; // primitives are frozen by definition
        }

        var obj = v.AsObject;
        if (obj.Extensible)
        {
            return JsValue.False;
        }

        foreach (var key in obj.OwnPropertyKeys)
        {
            var d = obj.GetOwnPropertyDescriptor(key);
            if (d is null)
            {
                continue;
            }

            var desc = d.Value;
            if (desc.Configurable)
            {
                return JsValue.False;
            }

            if (!desc.IsAccessor && desc.Writable)
            {
                return JsValue.False;
            }
        }
        return JsValue.True;
    }

    /// <summary>§20.1.2.16 Object.isSealed.</summary>
    private static JsValue IsSealed(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject)
        {
            return JsValue.True;
        }

        var obj = v.AsObject;
        if (obj.Extensible)
        {
            return JsValue.False;
        }

        foreach (var key in obj.OwnPropertyKeys)
        {
            var d = obj.GetOwnPropertyDescriptor(key);
            if (d is null)
            {
                continue;
            }

            if (d.Value.Configurable)
            {
                return JsValue.False;
            }
        }
        return JsValue.True;
    }

    /// <summary>§20.1.2.19 Object.preventExtensions — a false
    /// [[PreventExtensions]] (Proxy trap) is a TypeError.</summary>
    private static JsValue PreventExtensions(JsRealm realm, JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject)
        {
            return v;
        }

        if (!v.AsObject.PreventExtensions())
        {
            throw new JsThrow(realm.NewTypeError("Cannot prevent extensions"));
        }

        return v;
    }

    /// <summary>§20.1.2.14 Object.isExtensible.</summary>
    private static JsValue IsExtensible(JsValue[] args)
    {
        var v = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!v.IsObject)
        {
            return JsValue.False;
        }

        return JsValue.Boolean(v.AsObject.Extensible);
    }

    /// <summary>§20.1.2.13 Object.is.</summary>
    private static JsValue Is(JsValue[] args)
    {
        var a = args.Length > 0 ? args[0] : JsValue.Undefined;
        var b = args.Length > 1 ? args[1] : JsValue.Undefined;
        return JsValue.Boolean(AbstractOperations.SameValue(a, b));
    }

    /// <summary>§20.1.2.7 Object.fromEntries via §24.1.1.2
    /// AddEntriesFromIterable: strict iterator protocol only (no array-like
    /// fallback), with IteratorClose on every abrupt completion inside an
    /// entry — non-object entry, key/value getter throws, ToPropertyKey
    /// throws.</summary>
    private static JsValue FromEntries(JsRealm realm, JsValue[] args)
    {
        var srcV = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (srcV.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("Object.fromEntries requires an iterable"));
        }

        var vm = realm.ActiveVm;
        var result = realm.NewOrdinaryObject();
        var record = AbstractOperations.GetIterator(realm, vm, srcV);
        while (true)
        {
            var step = AbstractOperations.IteratorStep(realm, vm, ref record);
            if (step is null)
            {
                return JsValue.Object(result);
            }

            var entryV = AbstractOperations.IteratorValue(vm, step.Value);
            try
            {
                if (!entryV.IsObject)
                {
                    throw new JsThrow(realm.NewTypeError("Object.fromEntries: entry must be an object"));
                }

                var entry = entryV.AsObject;
                var k = AbstractOperations.Get(vm, entry, "0");
                var val = AbstractOperations.Get(vm, entry, "1");
                var key = AbstractOperations.ToPropertyKey(vm, k);
                result.DefineOwnProperty(key,
                    PropertyDescriptor.Data(val, writable: true, enumerable: true, configurable: true));
            }
            catch (JsThrow)
            {
                AbstractOperations.IteratorClose(vm, record, isThrowing: true);
                throw;
            }
        }
    }

    /// <summary>§20.1.2.13 (Array Grouping) Object.groupBy — iterate items,
    /// key each element by ToPropertyKey(callback(value, index)), and return
    /// a null-prototype object of dense arrays. Abrupt callback/coercion
    /// completions close the iterator.</summary>
    private static JsValue GroupBy(JsRealm realm, JsValue[] args)
    {
        var items = args.Length > 0 ? args[0] : JsValue.Undefined;
        var callback = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (items.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("Object.groupBy requires an iterable"));
        }

        if (!AbstractOperations.IsCallable(callback))
        {
            throw new JsThrow(realm.NewTypeError("Object.groupBy: callback must be a function"));
        }

        var vm = realm.ActiveVm;
        var order = new List<JsPropertyKey>();
        var groups = new Dictionary<string, JsArray>(StringComparer.Ordinal);
        var symbolGroups = new Dictionary<JsSymbol, JsArray>();
        var record = AbstractOperations.GetIterator(realm, vm, items);
        long k = 0;
        while (true)
        {
            var step = AbstractOperations.IteratorStep(realm, vm, ref record);
            if (step is null)
            {
                break;
            }

            var value = AbstractOperations.IteratorValue(vm, step.Value);
            JsPropertyKey key;
            try
            {
                var keyV = AbstractOperations.Call(vm, callback, JsValue.Undefined,
                    new[] { value, JsValue.Number(k) });
                key = AbstractOperations.ToPropertyKey(vm, keyV);
            }
            catch (JsThrow)
            {
                AbstractOperations.IteratorClose(vm, record, isThrowing: true);
                throw;
            }

            JsArray? group;
            if (key.IsSymbol)
            {
                if (!symbolGroups.TryGetValue(key.AsSymbol, out group))
                {
                    group = new JsArray(realm);
                    symbolGroups[key.AsSymbol] = group;
                    order.Add(key);
                }
            }
            else if (!groups.TryGetValue(key.AsString, out group))
            {
                group = new JsArray(realm);
                groups[key.AsString] = group;
                order.Add(key);
            }

            group!.Push(value);
            k++;
        }

        var result = realm.NewObjectWithProto(null);
        foreach (var key in order)
        {
            var group = key.IsSymbol ? symbolGroups[key.AsSymbol] : groups[key.AsString];
            result.DefineOwnProperty(key,
                PropertyDescriptor.Data(JsValue.Object(group), writable: true, enumerable: true, configurable: true));
        }
        return JsValue.Object(result);
    }

    /// <summary>§20.1.2.13 Object.hasOwn(obj, key) — ToObject(O) first
    /// (nullish throws), THEN ToPropertyKey(P).</summary>
    private static JsValue HasOwn(JsRealm realm, JsValue[] args)
    {
        var obj = RequireObject(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var key = AbstractOperations.ToPropertyKey(realm.ActiveVm, args.Length > 1 ? args[1] : JsValue.Undefined);
        return JsValue.Boolean(obj.HasOwn(key));
    }

    // ====================================================================
    //                       Prototype implementations
    // ====================================================================

    /// <summary>§B.2.2.2/§B.2.2.3 — a PARTIAL accessor define: only the
    /// requested get/set field plus enumerable+configurable are present, so
    /// the opposite accessor half of an existing property survives.</summary>
    private static JsValue DunderDefineAccessor(JsRealm realm, JsValue thisV, JsValue[] args, bool isGetter)
    {
        var obj = RequireObject(realm, thisV);
        var fn = args.Length > 1 ? args[1] : JsValue.Undefined;
        if (!AbstractOperations.IsCallable(fn))
        {
            throw new JsThrow(realm.NewTypeError(isGetter ? "Getter must be a function" : "Setter must be a function"));
        }

        var key = AbstractOperations.ToPropertyKey(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var desc = PropertyDescriptor.Accessor(
            isGetter ? fn.AsObject : null,
            isGetter ? null : fn.AsObject,
            enumerable: true, configurable: true);
        var fields = DescriptorFields.Build(value: false, writable: false, enumerable: true,
            configurable: true, get: isGetter, set: !isGetter);
        if (!obj.DefineOwnPropertyPartial(key, desc, fields))
        {
            throw new JsThrow(realm.NewTypeError($"Cannot redefine property '{key}'"));
        }

        return JsValue.Undefined;
    }

    /// <summary>§B.2.2.4/§B.2.2.5 — walk the prototype chain via the virtual
    /// [[GetOwnProperty]]/[[GetPrototypeOf]] (proxy traps fire) and return
    /// the requested accessor half of the first own descriptor found.</summary>
    private static JsValue DunderLookupAccessor(JsRealm realm, JsValue thisV, JsValue[] args, bool isGetter)
    {
        var obj = RequireObject(realm, thisV);
        var key = AbstractOperations.ToPropertyKey(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
        for (JsObject? o = obj; o is not null; o = o.GetPrototypeOf())
        {
            var d = o.GetOwnPropertyDescriptor(key);
            if (d is null)
            {
                continue;
            }

            if (!d.Value.IsAccessor)
            {
                return JsValue.Undefined;
            }

            var half = isGetter ? d.Value.Getter : d.Value.Setter;
            return half is null ? JsValue.Undefined : JsValue.Object(half);
        }
        return JsValue.Undefined;
    }

    /// <summary>§20.1.3.2 Object.prototype.hasOwnProperty.</summary>
    private static JsValue ProtoHasOwnProperty(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var key = AbstractOperations.ToPropertyKey(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var obj = RequireObject(realm, thisV);
        return JsValue.Boolean(obj.HasOwn(key));
    }

    /// <summary>§20.1.3.3 Object.prototype.isPrototypeOf — walks <c>V</c>'s
    /// prototype chain looking for <c>this</c>.</summary>
    private static JsValue ProtoIsPrototypeOf(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (args.Length == 0 || !args[0].IsObject)
        {
            return JsValue.False;
        }

        var self = RequireObject(realm, thisV);
        for (var p = args[0].AsObject.GetPrototypeOf(); p is not null; p = p.GetPrototypeOf())
        {
            if (ReferenceEquals(p, self))
            {
                return JsValue.True;
            }
        }
        return JsValue.False;
    }

    /// <summary>§20.1.3.4 Object.prototype.propertyIsEnumerable.</summary>
    private static JsValue ProtoPropertyIsEnumerable(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var key = AbstractOperations.ToPropertyKey(realm.ActiveVm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var obj = RequireObject(realm, thisV);
        var d = obj.GetOwnPropertyDescriptor(key);
        return JsValue.Boolean(d is { } dv && dv.Enumerable);
    }

    /// <summary>§20.1.3.6 Object.prototype.toString. Spec-faithful: classifies
    /// the receiver's internal class to pick a default tag, then consults
    /// <c>@@toStringTag</c> on the (ToObject'd) receiver — if present and a
    /// string, it overrides the default tag.</summary>
    private static JsValue ProtoToString(JsRealm realm, JsValue thisV)
    {
        if (thisV.IsUndefined)
        {
            return JsValue.String("[object Undefined]");
        }

        if (thisV.IsNull)
        {
            return JsValue.String("[object Null]");
        }

        var o = AbstractOperations.ToObject(realm, thisV);
        var defaultTag = DefaultToStringTag(realm, o);

        // Consult @@toStringTag on the receiver's prototype chain. Use the
        // AO-level Get so accessor descriptors (e.g. %TypedArray%.prototype's
        // toStringTag getter — §23.2.3.34) fire with the correct receiver.
        // Only string values override.
        var tag = defaultTag;
        var tagVal = AbstractOperations.Get(realm.ActiveVm, o, JsPropertyKey.Symbol(SymbolCtor.ToStringTag));
        if (tagVal.Kind == JsValueKind.String)
        {
            tag = tagVal.AsString;
        }

        return JsValue.String("[object " + tag + "]");
    }

    /// <summary>Pick the default built-in tag per §20.1.3.6 steps 4–14. We model
    /// the categories that have a corresponding host class, plus the Arguments
    /// exotic (step 5) via <see cref="JsObject.IsArgumentsExotic"/>.</summary>
    private static string DefaultToStringTag(JsRealm realm, JsObject o)
    {
        if (JsArray.IsArray(JsValue.Object(o), realm))
        {
            return "Array";
        }
        // §20.1.3.6 step 5 — [[ParameterMap]] ⇒ "Arguments". Must precede the
        // [[Call]] check (an arguments object isn't callable, but spec order).
        if (o.IsArgumentsExotic)
        {
            return "Arguments";
        }

        if (AbstractOperations.IsCallable(JsValue.Object(o)))
        {
            return "Function";
        }
        // §20.1.3.6 step 8 — [[ErrorData]] (realm-independent, unlike a
        // prototype-identity walk which misses cross-realm errors).
        if (o.IsErrorExotic)
        {
            return "Error";
        }
        // §20.1.3.6 step 7 — [[StringData]] ⇒ "String" (covers %String.prototype%
        // itself, which sits on Object.prototype).
        if (o is JsStringObject)
        {
            return "String";
        }
        // Boxed primitives (String / Number / Boolean) — detect via prototype.
        for (var p = o.Prototype; p is not null; p = p.Prototype)
        {
            if (ReferenceEquals(p, realm.StringPrototype))
            {
                return "String";
            }

            if (ReferenceEquals(p, realm.NumberPrototype))
            {
                return "Number";
            }

            if (ReferenceEquals(p, realm.BooleanPrototype))
            {
                return "Boolean";
            }
        }
        if (o is JsDate)
        {
            return "Date";
        }

        if (o is JsRegExp)
        {
            return "RegExp";
        }

        return "Object";
    }

    /// <summary>§20.1.3.7 Object.prototype.valueOf — return <c>this</c> coerced
    /// to an object (primitives box).</summary>
    private static JsValue ProtoValueOf(JsRealm realm, JsValue thisV)
        => JsValue.Object(RequireObject(realm, thisV));

    /// <summary>§20.1.3.5 Object.prototype.toLocaleString — Invoke(O, "toString"),
    /// with the ORIGINAL this value as receiver (primitives stay primitive).</summary>
    private static JsValue ProtoToLocaleString(JsRealm realm, JsValue thisV)
    {
        var obj = AbstractOperations.ToObject(realm, thisV);
        // GetV — the boxed object only serves the lookup; an accessor's getter
        // runs with the ORIGINAL (possibly primitive) receiver.
        var ts = AbstractOperations.Get(realm.ActiveVm, obj, "toString", thisV);
        if (!AbstractOperations.IsCallable(ts))
        {
            throw new JsThrow(realm.NewTypeError("toString is not a function"));
        }

        return AbstractOperations.Call(realm.ActiveVm, ts, thisV, Array.Empty<JsValue>());
    }
}
