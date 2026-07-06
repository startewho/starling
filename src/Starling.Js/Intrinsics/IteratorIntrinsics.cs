using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §27.1 The %IteratorPrototype% intrinsic and the per-kind iterator
/// prototypes (%ArrayIteratorPrototype%, %StringIteratorPrototype%). Installs
/// the <c>@@iterator</c>-returns-this trick on %IteratorPrototype% so every
/// downstream iterator object is itself iterable.
/// </summary>
public static class IteratorIntrinsics
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);

        // %IteratorPrototype% — has [@@iterator]() { return this; }. The
        // ArrayIterator and StringIterator prototypes already inherit from
        // this via JsRealm bootstrap, so installing it once threads through.
        var iterProto = realm.IteratorPrototype;
        var iteratorReturnsThis = new JsNativeFunction(realm, "[Symbol.iterator]", 0,
            (thisV, _) => thisV, isConstructor: false);
        iterProto.DefineOwnProperty(SymbolCtor.Iterator,
            PropertyDescriptor.BuiltinMethod(JsValue.Object(iteratorReturnsThis)));

        // %ArrayIteratorPrototype%.next()
        var arrIterProto = realm.ArrayIteratorPrototype;
        var arrayNext = new JsNativeFunction(realm, "next", 0,
            (thisV, _) => ArrayIteratorNext(realm, thisV), isConstructor: false);
        arrIterProto.DefineOwnProperty("next",
            PropertyDescriptor.BuiltinMethod(JsValue.Object(arrayNext)));
        arrIterProto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Array Iterator"), writable: false, enumerable: false, configurable: true));

        // %StringIteratorPrototype%.next()
        var strIterProto = realm.StringIteratorPrototype;
        var stringNext = new JsNativeFunction(realm, "next", 0,
            (thisV, _) => StringIteratorNext(realm, thisV), isConstructor: false);
        strIterProto.DefineOwnProperty("next",
            PropertyDescriptor.BuiltinMethod(JsValue.Object(stringNext)));
        strIterProto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("String Iterator"), writable: false, enumerable: false, configurable: true));

        InstallIteratorGlobal(realm);
    }

    // ==================================================================
    //     §27.1.3 The Iterator constructor + ES2025 iterator helpers
    // ==================================================================

    private static void InstallIteratorGlobal(JsRealm realm)
    {
        var iterProto = realm.IteratorPrototype;

        JsNativeFunction? ctorBox = null;
        var ctor = new JsNativeFunction("Iterator", (newTarget, args) =>
        {
            // §27.1.3.1 — Iterator is abstract: plain calls and direct `new
            // Iterator()` both throw; only subclass super() reaches the body.
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("Constructor Iterator requires 'new'"));
            }

            if (newTarget.IsObject && ReferenceEquals(newTarget.AsObject, ctorBox))
            {
                throw new JsThrow(realm.NewTypeError("Abstract class Iterator not directly constructable"));
            }

            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, iterProto,
                static r => r.IteratorPrototype);
            return JsValue.Object(new JsObject(instProto));
        }, isConstructor: true);
        ctorBox = ctor;
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ctor.DefineOwnProperty("length",
            PropertyDescriptor.Data(JsValue.Number(0), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String("Iterator"), writable: false, enumerable: false, configurable: true));
        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(iterProto), writable: false, enumerable: false, configurable: false));

        IntrinsicHelpers.DefineMethod(realm, ctor, "from", 1,
            (thisV, args) => IteratorFrom(realm, args.Length > 0 ? args[0] : JsValue.Undefined));
        IntrinsicHelpers.DefineMethod(realm, ctor, "zip", 1, (thisV, args) =>
            IteratorZipStatic(realm,
                args.Length > 0 ? args[0] : JsValue.Undefined,
                args.Length > 1 ? args[1] : JsValue.Undefined));
        IntrinsicHelpers.DefineMethod(realm, ctor, "zipKeyed", 1, (thisV, args) =>
            IteratorZipKeyedStatic(realm,
                args.Length > 0 ? args[0] : JsValue.Undefined,
                args.Length > 1 ? args[1] : JsValue.Undefined));

        // §27.1.4.1 / §27.1.4.14 — `constructor` and @@toStringTag are
        // accessors whose setter mimics an ordinary data write EXCEPT on
        // %Iterator.prototype% itself (SetterThatIgnoresPrototypeProperties).
        DefineProtoAccessorPair(realm, iterProto, JsPropertyKey.String("constructor"), "constructor",
            () => ctorBox is null ? JsValue.Undefined : JsValue.Object(ctorBox));
        DefineProtoAccessorPair(realm, iterProto, JsPropertyKey.Symbol(SymbolCtor.ToStringTag), "[Symbol.toStringTag]",
            () => JsValue.String("Iterator"));

        // ---- Lazy helpers (§27.1.4.x) -------------------------------------
        IntrinsicHelpers.DefineMethod(realm, iterProto, "map", 1,
            (thisV, args) => MakeMappingHelper(realm, thisV, args, HelperKind.Map));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "filter", 1,
            (thisV, args) => MakeMappingHelper(realm, thisV, args, HelperKind.Filter));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "flatMap", 1,
            (thisV, args) => MakeMappingHelper(realm, thisV, args, HelperKind.FlatMap));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "take", 1,
            (thisV, args) => MakeCountedHelper(realm, thisV, args, isTake: true));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "drop", 1,
            (thisV, args) => MakeCountedHelper(realm, thisV, args, isTake: false));

        // ---- Eager consumers (§27.1.4.x) ----------------------------------
        IntrinsicHelpers.DefineMethod(realm, iterProto, "reduce", 1,
            (thisV, args) => Reduce(realm, thisV, args));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "toArray", 0,
            (thisV, args) => ToArray(realm, thisV));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "forEach", 1,
            (thisV, args) => EagerVisit(realm, thisV, args, EagerKind.ForEach));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "some", 1,
            (thisV, args) => EagerVisit(realm, thisV, args, EagerKind.Some));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "every", 1,
            (thisV, args) => EagerVisit(realm, thisV, args, EagerKind.Every));
        IntrinsicHelpers.DefineMethod(realm, iterProto, "find", 1,
            (thisV, args) => EagerVisit(realm, thisV, args, EagerKind.Find));

        // ---- %IteratorHelperPrototype% ------------------------------------
        var helperProto = realm.IteratorHelperPrototype;
        IntrinsicHelpers.DefineMethod(realm, helperProto, "next", 0,
            (thisV, args) => HelperNext(realm, thisV));
        IntrinsicHelpers.DefineMethod(realm, helperProto, "return", 0,
            (thisV, args) => HelperReturn(realm, thisV));
        helperProto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("Iterator Helper"), writable: false, enumerable: false, configurable: true));

        // ---- %WrapForValidIteratorPrototype% -------------------------------
        var wrapProto = realm.WrapForValidIteratorPrototype;
        IntrinsicHelpers.DefineMethod(realm, wrapProto, "next", 0, (thisV, args) =>
        {
            if (!thisV.IsObject || thisV.AsObject is not JsIteratorWrapper w)
            {
                throw new JsThrow(realm.NewTypeError("next called on incompatible Iterator wrapper"));
            }

            return AbstractOperations.Call(realm.ActiveVm, w.Iterated.NextMethod,
                w.Iterated.Iterator, Array.Empty<JsValue>());
        });
        IntrinsicHelpers.DefineMethod(realm, wrapProto, "return", 0, (thisV, args) =>
        {
            if (!thisV.IsObject || thisV.AsObject is not JsIteratorWrapper w)
            {
                throw new JsThrow(realm.NewTypeError("return called on incompatible Iterator wrapper"));
            }

            var vm = realm.ActiveVm;
            var ret = AbstractOperations.GetMethod(vm, w.Iterated.Iterator, "return");
            if (ret.IsUndefined || ret.IsNull)
            {
                return MakeResult(realm, JsValue.Undefined, done: true);
            }

            return AbstractOperations.Call(vm, ret, w.Iterated.Iterator, Array.Empty<JsValue>());
        });

        realm.IteratorConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("Iterator",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    /// <summary>§27.1.4.1 — accessor with SetterThatIgnoresPrototypeProperties
    /// semantics: assigning through a DERIVED object creates an own data
    /// property there; assigning on the home object throws.</summary>
    private static void DefineProtoAccessorPair(JsRealm realm, JsObject home, JsPropertyKey key,
        string label, Func<JsValue> getValue)
    {
        var getter = new JsNativeFunction(realm, "get " + label, 0,
            (_, _) => getValue(), isConstructor: false);
        var setter = new JsNativeFunction(realm, "set " + label, 1, (thisV, args) =>
        {
            if (!thisV.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("Cannot assign on a primitive receiver"));
            }

            var obj = thisV.AsObject;
            if (ReferenceEquals(obj, home))
            {
                throw new JsThrow(realm.NewTypeError($"Cannot assign to read-only property '{label}'"));
            }

            var value = args.Length > 0 ? args[0] : JsValue.Undefined;
            var desc = key.IsSymbol
                ? obj.GetOwnPropertyDescriptor(key.AsSymbol)
                : obj.GetOwnPropertyDescriptor(key.AsString);
            if (desc is null)
            {
                var data = PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true);
                var created = key.IsSymbol
                    ? obj.DefineOwnProperty(key.AsSymbol, data)
                    : obj.DefineOwnProperty(key.AsString, data);
                if (!created)
                {
                    throw new JsThrow(realm.NewTypeError($"Cannot define property '{label}'"));
                }
            }
            else
            {
                AbstractOperations.Set(realm.ActiveVm, obj, key, value, JsValue.Object(obj));
            }
            return JsValue.Undefined;
        }, isConstructor: false);
        var acc = PropertyDescriptor.Accessor(getter, setter, enumerable: false, configurable: true);
        if (key.IsSymbol)
        {
            home.DefineOwnProperty(key.AsSymbol, acc);
        }
        else
        {
            home.DefineOwnProperty(key.AsString, acc);
        }
    }

    // ------------------------------------------------------------------
    //                    Shared iterator plumbing
    // ------------------------------------------------------------------

    /// <summary>§27.1.2.1 GetIteratorDirect.</summary>
    private static IteratorRecord GetIteratorDirect(JsRealm realm, JsObject obj)
    {
        var next = AbstractOperations.Get(realm.ActiveVm, obj, "next");
        return new IteratorRecord(JsValue.Object(obj), next, Done: false);
    }

    /// <summary>§27.1.2.2 GetIteratorFlattenable.</summary>
    private static IteratorRecord GetIteratorFlattenable(JsRealm realm, JsValue obj, bool allowStrings)
    {
        var vm = realm.ActiveVm;
        if (!obj.IsObject)
        {
            if (!allowStrings || obj.Kind != JsValueKind.String)
            {
                throw new JsThrow(realm.NewTypeError("value is not iterable"));
            }
        }

        JsValue method;
        if (obj.IsObject)
        {
            method = AbstractOperations.GetMethod(vm, obj, SymbolCtor.Iterator);
        }
        else
        {
            // GetV — the wrapper prototype is walked but a getter observes
            // the PRIMITIVE receiver.
            var boxed = AbstractOperations.ToObject(realm, obj);
            method = AbstractOperations.Get(vm, boxed,
                JsPropertyKey.Symbol(SymbolCtor.Iterator), receiver: obj);
            if (!method.IsNullish && !AbstractOperations.IsCallable(method))
            {
                throw new JsThrow(realm.NewTypeError("@@iterator is not a function"));
            }
        }
        JsValue iterator;
        if (method.IsUndefined || method.IsNull)
        {
            iterator = obj;
        }
        else
        {
            iterator = AbstractOperations.Call(vm, method, obj, Array.Empty<JsValue>());
        }
        if (!iterator.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("iterator is not an object"));
        }

        return GetIteratorDirect(realm, iterator.AsObject);
    }

    /// <summary>§7.4.8 IteratorStepValue — one observable step; done and
    /// abrupt completions mark the record closed.</summary>
    private static bool IteratorStepValue(JsRealm realm, IteratorRecord[] records, int index, out JsValue value)
    {
        var vm = realm.ActiveVm;
        JsValue result;
        try
        {
            result = AbstractOperations.IteratorNext(realm, vm, records[index]);
        }
        catch
        {
            records[index] = records[index] with { Done = true };
            throw;
        }
        bool done;
        try
        {
            done = AbstractOperations.IteratorComplete(vm, result);
        }
        catch
        {
            records[index] = records[index] with { Done = true };
            throw;
        }
        if (done)
        {
            records[index] = records[index] with { Done = true };
            value = JsValue.Undefined;
            return false;
        }
        try
        {
            value = AbstractOperations.IteratorValue(vm, result);
        }
        catch
        {
            records[index] = records[index] with { Done = true };
            throw;
        }
        return true;
    }

    /// <summary>Close an iterator OBJECT directly (only `return` is read —
    /// argument-validation failures must not touch `next`), keeping
    /// <paramref name="pending"/> as the propagated error.</summary>
    private static void CloseIteratorObject(JsRealm realm, JsObject obj, JsThrow pending)
    {
        var record = new IteratorRecord(JsValue.Object(obj), JsValue.Undefined, Done: false);
        try
        {
            AbstractOperations.IteratorClose(realm.ActiveVm, record, isThrowing: true);
        }
        catch (JsThrow)
        {
            // throw-completion threading: the pending error wins.
        }
        throw pending;
    }

    /// <summary>IteratorCloseAll (joint-iteration) — close in reverse order;
    /// the first close error (walking in that order) wins, later ones are
    /// swallowed per the throw-completion threading.</summary>
    internal static void IteratorCloseAll(JsRealm realm, IReadOnlyList<IteratorRecord> iters, JsThrow? pending = null)
    {
        var vm = realm.ActiveVm;
        for (var i = iters.Count - 1; i >= 0; i--)
        {
            try
            {
                AbstractOperations.IteratorClose(vm, iters[i], isThrowing: pending is not null);
            }
            catch (JsThrow ex)
            {
                pending ??= ex;
            }
        }
        if (pending is not null)
        {
            throw pending;
        }
    }

    // ------------------------------------------------------------------
    //                       Iterator.from
    // ------------------------------------------------------------------

    private static JsValue IteratorFrom(JsRealm realm, JsValue value)
    {
        var record = GetIteratorFlattenable(realm, value, allowStrings: true);
        var iterObj = record.Iterator.AsObject;
        if (realm.IteratorConstructor is { } iterCtor
            && FunctionCtor.OrdinaryHasInstance(realm, JsValue.Object(iterCtor), record.Iterator))
        {
            return record.Iterator;
        }

        return JsValue.Object(new JsIteratorWrapper(realm, record));
    }

    // ------------------------------------------------------------------
    //                 Lazy helper construction (§27.1.4)
    // ------------------------------------------------------------------

    private enum HelperKind : byte
    {
        Map,
        Filter,
        FlatMap,
    }

    private static JsValue MakeMappingHelper(JsRealm realm, JsValue thisV, JsValue[] args, HelperKind kind)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Iterator helper called on non-object"));
        }

        var fn = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!AbstractOperations.IsCallable(fn))
        {
            CloseIteratorObject(realm, thisV.AsObject,
                new JsThrow(realm.NewTypeError("Iterator helper callback is not a function")));
        }

        var records = new[] { GetIteratorDirect(realm, thisV.AsObject) };
        var helper = new JsIteratorHelper(realm, records);
        var counter = new long[1];
        switch (kind)
        {
            case HelperKind.Map:
                helper.StepFn = h =>
                {
                    if (!IteratorStepValue(realm, h.Records, 0, out var value))
                    {
                        return (true, JsValue.Undefined);
                    }

                    JsValue mapped;
                    try
                    {
                        mapped = AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Undefined,
                            new[] { value, JsValue.Number(counter[0]) });
                    }
                    catch (JsThrow ex)
                    {
                        IteratorCloseAll(realm, new[] { h.Records[0] }, ex);
                        throw;
                    }
                    counter[0]++;
                    return (false, mapped);
                };
                break;
            case HelperKind.Filter:
                helper.StepFn = h =>
                {
                    while (true)
                    {
                        if (!IteratorStepValue(realm, h.Records, 0, out var value))
                        {
                            return (true, JsValue.Undefined);
                        }

                        JsValue selected;
                        try
                        {
                            selected = AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Undefined,
                                new[] { value, JsValue.Number(counter[0]) });
                        }
                        catch (JsThrow ex)
                        {
                            IteratorCloseAll(realm, new[] { h.Records[0] }, ex);
                            throw;
                        }
                        counter[0]++;
                        if (JsValue.ToBoolean(selected))
                        {
                            return (false, value);
                        }
                    }
                };
                break;
            default:
            {
                // flatMap: inner iterator record boxed so return() can close
                // inner-then-outer.
                var inner = new IteratorRecord?[1];
                helper.CloseFn = h =>
                {
                    JsThrow? pending = null;
                    if (inner[0] is { } innerRec)
                    {
                        inner[0] = null;
                        try
                        {
                            AbstractOperations.IteratorClose(realm.ActiveVm, innerRec, isThrowing: false);
                        }
                        catch (JsThrow ex)
                        {
                            pending = ex;
                        }
                    }
                    IteratorCloseAll(realm, new[] { h.Records[0] }, pending);
                };
                helper.StepFn = h =>
                {
                    while (true)
                    {
                        if (inner[0] is { } innerRec)
                        {
                            var innerRecords = new[] { innerRec };
                            bool has;
                            JsValue innerValue;
                            try
                            {
                                has = IteratorStepValue(realm, innerRecords, 0, out innerValue);
                            }
                            catch (JsThrow ex)
                            {
                                inner[0] = null;
                                IteratorCloseAll(realm, new[] { h.Records[0] }, ex);
                                throw;
                            }
                            inner[0] = innerRecords[0];
                            if (has)
                            {
                                return (false, innerValue);
                            }

                            inner[0] = null;
                            continue;
                        }
                        if (!IteratorStepValue(realm, h.Records, 0, out var value))
                        {
                            return (true, JsValue.Undefined);
                        }

                        try
                        {
                            var mapped = AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Undefined,
                                new[] { value, JsValue.Number(counter[0]) });
                            counter[0]++;
                            inner[0] = GetIteratorFlattenable(realm, mapped, allowStrings: false);
                        }
                        catch (JsThrow ex)
                        {
                            IteratorCloseAll(realm, new[] { h.Records[0] }, ex);
                            throw;
                        }
                    }
                };
                break;
            }
        }
        return JsValue.Object(helper);
    }

    private static JsValue MakeCountedHelper(JsRealm realm, JsValue thisV, JsValue[] args, bool isTake)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Iterator helper called on non-object"));
        }

        var limitV = args.Length > 0 ? args[0] : JsValue.Undefined;
        double num;
        try
        {
            num = NumberCtor.ToNumber(limitV);
        }
        catch (JsThrow ex)
        {
            CloseIteratorObject(realm, thisV.AsObject, ex);
            throw;
        }
        if (double.IsNaN(num))
        {
            CloseIteratorObject(realm, thisV.AsObject,
                new JsThrow(realm.NewRangeError(isTake ? "take limit must not be NaN" : "drop limit must not be NaN")));
        }

        var integerLimit = double.IsInfinity(num) ? num : Math.Truncate(num);
        if (integerLimit is 0 or -0d && double.IsNegative(integerLimit))
        {
            integerLimit = 0;
        }

        if (integerLimit < 0)
        {
            CloseIteratorObject(realm, thisV.AsObject,
                new JsThrow(realm.NewRangeError(isTake ? "take limit must be non-negative" : "drop limit must be non-negative")));
        }

        var records = new[] { GetIteratorDirect(realm, thisV.AsObject) };
        var helper = new JsIteratorHelper(realm, records);
        var remaining = new double[1] { integerLimit };
        if (isTake)
        {
            helper.StepFn = h =>
            {
                if (remaining[0] <= 0)
                {
                    // Limit reached — close the underlying iterator.
                    IteratorCloseAll(realm, new[] { h.Records[0] });
                    return (true, JsValue.Undefined);
                }

                if (!double.IsPositiveInfinity(remaining[0]))
                {
                    remaining[0]--;
                }

                if (!IteratorStepValue(realm, h.Records, 0, out var value))
                {
                    return (true, JsValue.Undefined);
                }

                return (false, value);
            };
        }
        else
        {
            helper.StepFn = h =>
            {
                while (remaining[0] > 0)
                {
                    if (!double.IsPositiveInfinity(remaining[0]))
                    {
                        remaining[0]--;
                    }

                    if (!IteratorStepValue(realm, h.Records, 0, out _))
                    {
                        return (true, JsValue.Undefined);
                    }
                }
                if (!IteratorStepValue(realm, h.Records, 0, out var value))
                {
                    return (true, JsValue.Undefined);
                }

                return (false, value);
            };
        }
        return JsValue.Object(helper);
    }

    // ------------------------------------------------------------------
    //                 %IteratorHelperPrototype%.next/return
    // ------------------------------------------------------------------

    private static JsValue HelperNext(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsIteratorHelper h)
        {
            throw new JsThrow(realm.NewTypeError("next called on incompatible Iterator Helper"));
        }

        switch (h.State)
        {
            case IteratorHelperState.Completed:
                return MakeResult(realm, JsValue.Undefined, done: true);
            case IteratorHelperState.Executing:
                throw new JsThrow(realm.NewTypeError("Iterator Helper is already running"));
        }
        h.State = IteratorHelperState.Executing;
        bool done;
        JsValue value;
        try
        {
            (done, value) = h.StepFn(h);
        }
        catch
        {
            h.State = IteratorHelperState.Completed;
            throw;
        }
        h.State = done ? IteratorHelperState.Completed : IteratorHelperState.SuspendedYield;
        return MakeResult(realm, value, done);
    }

    private static JsValue HelperReturn(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsIteratorHelper h)
        {
            throw new JsThrow(realm.NewTypeError("return called on incompatible Iterator Helper"));
        }

        switch (h.State)
        {
            case IteratorHelperState.Executing:
                throw new JsThrow(realm.NewTypeError("Iterator Helper is already running"));
            case IteratorHelperState.Completed:
                return MakeResult(realm, JsValue.Undefined, done: true);
        }
        var fromStart = h.State == IteratorHelperState.SuspendedStart;
        // §27.1.2.2: from suspended-start the state flips to completed BEFORE
        // closing (reentrant calls observe a finished helper); from
        // suspended-yield the abrupt resume runs with the generator EXECUTING,
        // so reentrant next()/return() during the close throw TypeError.
        h.State = fromStart ? IteratorHelperState.Completed : IteratorHelperState.Executing;
        try
        {
            if (h.CloseFn is { } close)
            {
                close(h);
            }
            else
            {
                IteratorCloseAll(realm, h.OpenRecords());
            }
        }
        finally
        {
            h.State = IteratorHelperState.Completed;
        }
        return MakeResult(realm, JsValue.Undefined, done: true);
    }

    // ------------------------------------------------------------------
    //                       Eager consumers
    // ------------------------------------------------------------------

    private enum EagerKind : byte
    {
        ForEach,
        Some,
        Every,
        Find,
    }

    private static JsValue Reduce(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Iterator.prototype.reduce called on non-object"));
        }

        var reducer = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!AbstractOperations.IsCallable(reducer))
        {
            CloseIteratorObject(realm, thisV.AsObject,
                new JsThrow(realm.NewTypeError("reducer is not a function")));
        }

        var records = new[] { GetIteratorDirect(realm, thisV.AsObject) };
        JsValue accumulator;
        long counter = 0;
        if (args.Length < 2)
        {
            if (!IteratorStepValue(realm, records, 0, out var first))
            {
                throw new JsThrow(realm.NewTypeError("reduce of a done iterator with no initial value"));
            }

            accumulator = first;
            counter = 1;
        }
        else
        {
            accumulator = args[1];
        }
        while (true)
        {
            if (!IteratorStepValue(realm, records, 0, out var value))
            {
                return accumulator;
            }

            try
            {
                accumulator = AbstractOperations.Call(realm.ActiveVm, reducer, JsValue.Undefined,
                    new[] { accumulator, value, JsValue.Number(counter) });
            }
            catch (JsThrow ex)
            {
                IteratorCloseAll(realm, records, ex);
                throw;
            }
            counter++;
        }
    }

    private static JsValue ToArray(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Iterator.prototype.toArray called on non-object"));
        }

        var records = new[] { GetIteratorDirect(realm, thisV.AsObject) };
        var items = new List<JsValue>();
        while (IteratorStepValue(realm, records, 0, out var value))
        {
            items.Add(value);
        }
        return JsValue.Object(new JsArray(realm, items));
    }

    private static JsValue EagerVisit(JsRealm realm, JsValue thisV, JsValue[] args, EagerKind kind)
    {
        if (!thisV.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Iterator helper called on non-object"));
        }

        var fn = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!AbstractOperations.IsCallable(fn))
        {
            CloseIteratorObject(realm, thisV.AsObject,
                new JsThrow(realm.NewTypeError("Iterator helper callback is not a function")));
        }

        var records = new[] { GetIteratorDirect(realm, thisV.AsObject) };
        long counter = 0;
        while (true)
        {
            if (!IteratorStepValue(realm, records, 0, out var value))
            {
                return kind switch
                {
                    EagerKind.Some => JsValue.Boolean(false),
                    EagerKind.Every => JsValue.Boolean(true),
                    EagerKind.Find => JsValue.Undefined,
                    _ => JsValue.Undefined,
                };
            }

            JsValue result;
            try
            {
                result = AbstractOperations.Call(realm.ActiveVm, fn, JsValue.Undefined,
                    new[] { value, JsValue.Number(counter) });
            }
            catch (JsThrow ex)
            {
                IteratorCloseAll(realm, records, ex);
                throw;
            }
            counter++;
            switch (kind)
            {
                case EagerKind.Some when JsValue.ToBoolean(result):
                    IteratorCloseAll(realm, records);
                    return JsValue.Boolean(true);
                case EagerKind.Every when !JsValue.ToBoolean(result):
                    IteratorCloseAll(realm, records);
                    return JsValue.Boolean(false);
                case EagerKind.Find when JsValue.ToBoolean(result):
                    IteratorCloseAll(realm, records);
                    return value;
            }
        }
    }

    // ------------------------------------------------------------------
    //             Iterator.zip / Iterator.zipKeyed (joint-iteration)
    // ------------------------------------------------------------------

    /// <summary>GetOptionsObject — undefined becomes a null-prototype empty
    /// object; any other non-object is a TypeError.</summary>
    private static JsObject GetOptionsObject(JsRealm realm, JsValue options)
    {
        if (options.IsUndefined)
        {
            return new JsObject(prototype: null);
        }

        if (!options.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("options must be an object or undefined"));
        }

        return options.AsObject;
    }

    private static (string Mode, JsValue PaddingOption) ReadZipOptions(JsRealm realm, JsValue optionsV)
    {
        var vm = realm.ActiveVm;
        var options = GetOptionsObject(realm, optionsV);
        var modeV = AbstractOperations.Get(vm, options, "mode");
        string mode;
        if (modeV.IsUndefined)
        {
            mode = "shortest";
        }
        else if (modeV.Kind == JsValueKind.String
            && modeV.AsString is "shortest" or "longest" or "strict")
        {
            mode = modeV.AsString;
        }
        else
        {
            throw new JsThrow(realm.NewTypeError("Invalid zip mode"));
        }

        var paddingOption = JsValue.Undefined;
        if (mode == "longest")
        {
            paddingOption = AbstractOperations.Get(vm, options, "padding");
            if (!paddingOption.IsUndefined && !paddingOption.IsObject)
            {
                throw new JsThrow(realm.NewTypeError("padding must be an object or undefined"));
            }
        }
        return (mode, paddingOption);
    }

    private static JsValue IteratorZipStatic(JsRealm realm, JsValue iterables, JsValue optionsV)
    {
        var vm = realm.ActiveVm;
        if (!iterables.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Iterator.zip iterables must be an object"));
        }

        var (mode, paddingOption) = ReadZipOptions(realm, optionsV);

        var iters = new List<IteratorRecord>();
        var inputRecord = AbstractOperations.GetIterator(realm, vm, iterables);
        var inputRecords = new[] { inputRecord };
        while (true)
        {
            bool has;
            JsValue next;
            try
            {
                has = IteratorStepValue(realm, inputRecords, 0, out next);
            }
            catch (JsThrow ex)
            {
                IteratorCloseAll(realm, iters, ex);
                throw;
            }
            if (!has)
            {
                break;
            }

            try
            {
                iters.Add(GetIteratorFlattenable(realm, next, allowStrings: false));
            }
            catch (JsThrow ex)
            {
                var toClose = new List<IteratorRecord> { inputRecords[0] };
                toClose.AddRange(iters);
                IteratorCloseAll(realm, toClose, ex);
                throw;
            }
        }
        var iterCount = iters.Count;
        var padding = new JsValue[iterCount];
        for (var i = 0; i < iterCount; i++)
        {
            padding[i] = JsValue.Undefined;
        }
        if (mode == "longest" && !paddingOption.IsUndefined)
        {
            try
            {
                var padRecord = AbstractOperations.GetIterator(realm, vm, paddingOption);
                var padRecords = new[] { padRecord };
                var usingIterator = true;
                for (var i = 0; i < iterCount && usingIterator; i++)
                {
                    if (IteratorStepValue(realm, padRecords, 0, out var padValue))
                    {
                        padding[i] = padValue;
                    }
                    else
                    {
                        usingIterator = false;
                    }
                }
                if (usingIterator)
                {
                    AbstractOperations.IteratorClose(vm, padRecords[0], isThrowing: false);
                }
            }
            catch (JsThrow ex)
            {
                IteratorCloseAll(realm, iters, ex);
                throw;
            }
        }
        return JsValue.Object(MakeZipHelper(realm, iters, mode, padding,
            results => JsValue.Object(new JsArray(realm, results))));
    }

    private static JsValue IteratorZipKeyedStatic(JsRealm realm, JsValue promises, JsValue optionsV)
    {
        var vm = realm.ActiveVm;
        if (!promises.IsObject)
        {
            throw new JsThrow(realm.NewTypeError("Iterator.zipKeyed argument must be an object"));
        }

        var (mode, paddingOption) = ReadZipOptions(realm, optionsV);

        var source = promises.AsObject;
        var allKeys = new List<JsPropertyKey>(source.OwnPropertyKeys);
        var keys = new List<JsPropertyKey>();
        var iters = new List<IteratorRecord>();
        foreach (var key in allKeys)
        {
            try
            {
                var desc = key.IsSymbol
                    ? source.GetOwnPropertyDescriptor(key.AsSymbol)
                    : source.GetOwnPropertyDescriptor(key.AsString);
                if (desc is not { } dv || !dv.Enumerable)
                {
                    continue;
                }

                var value = AbstractOperations.Get(vm, source, key);
                if (value.IsUndefined)
                {
                    // Own enumerable properties holding undefined are ignored.
                    continue;
                }

                var record = GetIteratorFlattenable(realm, value, allowStrings: false);
                keys.Add(key);
                iters.Add(record);
            }
            catch (JsThrow ex)
            {
                IteratorCloseAll(realm, iters, ex);
                throw;
            }
        }
        var iterCount = iters.Count;
        var padding = new JsValue[iterCount];
        for (var i = 0; i < iterCount; i++)
        {
            padding[i] = JsValue.Undefined;
        }
        if (mode == "longest" && !paddingOption.IsUndefined)
        {
            var padSource = paddingOption.AsObject;
            for (var i = 0; i < iterCount; i++)
            {
                try
                {
                    padding[i] = AbstractOperations.Get(vm, padSource, keys[i]);
                }
                catch (JsThrow ex)
                {
                    IteratorCloseAll(realm, iters, ex);
                    throw;
                }
            }
        }
        var keysArr = keys.ToArray();
        return JsValue.Object(MakeZipHelper(realm, iters, mode, padding, results =>
        {
            var obj = new JsObject(prototype: null);
            for (var i = 0; i < keysArr.Length; i++)
            {
                var d = PropertyDescriptor.Data(results[i], writable: true, enumerable: true, configurable: true);
                if (keysArr[i].IsSymbol)
                {
                    obj.DefineOwnProperty(keysArr[i].AsSymbol, d);
                }
                else
                {
                    obj.DefineOwnProperty(keysArr[i].AsString, d);
                }
            }
            return JsValue.Object(obj);
        }));
    }

    /// <summary>IteratorZip (joint-iteration) — the shared row loop.</summary>
    private static JsIteratorHelper MakeZipHelper(JsRealm realm, List<IteratorRecord> iters,
        string mode, JsValue[] padding, Func<JsValue[], JsValue> finishResults)
    {
        var iterCount = iters.Count;
        var records = iters.ToArray();
        var exhausted = new bool[iterCount];
        var helper = new JsIteratorHelper(realm, records);

        List<IteratorRecord> Open()
        {
            var open = new List<IteratorRecord>(iterCount);
            for (var i = 0; i < iterCount; i++)
            {
                if (!exhausted[i])
                {
                    open.Add(records[i]);
                }
            }
            return open;
        }

        helper.CloseFn = _ => IteratorCloseAll(realm, Open());
        helper.StepFn = h =>
        {
            if (iterCount == 0)
            {
                return (true, JsValue.Undefined);
            }

            var results = new JsValue[iterCount];
            for (var i = 0; i < iterCount; i++)
            {
                if (exhausted[i])
                {
                    results[i] = padding[i];
                    continue;
                }

                bool has;
                JsValue value;
                try
                {
                    has = IteratorStepValue(realm, records, i, out value);
                }
                catch (JsThrow ex)
                {
                    exhausted[i] = true;
                    IteratorCloseAll(realm, Open(), ex);
                    throw;
                }
                if (!has)
                {
                    exhausted[i] = true;
                    if (mode == "shortest")
                    {
                        IteratorCloseAll(realm, Open());
                        return (true, JsValue.Undefined);
                    }

                    if (mode == "strict")
                    {
                        if (i != 0)
                        {
                            var err = new JsThrow(realm.NewTypeError(
                                "Iterator.zip strict mode: iterators have different lengths"));
                            IteratorCloseAll(realm, Open(), err);
                            throw err;
                        }
                        for (var k = 1; k < iterCount; k++)
                        {
                            bool otherHas;
                            try
                            {
                                otherHas = IteratorStepValue(realm, records, k, out _);
                            }
                            catch (JsThrow ex)
                            {
                                exhausted[k] = true;
                                IteratorCloseAll(realm, Open(), ex);
                                throw;
                            }
                            if (otherHas)
                            {
                                var err = new JsThrow(realm.NewTypeError(
                                    "Iterator.zip strict mode: iterators have different lengths"));
                                IteratorCloseAll(realm, Open(), err);
                                throw err;
                            }

                            exhausted[k] = true;
                        }
                        return (true, JsValue.Undefined);
                    }

                    // longest
                    value = padding[i];
                    var anyOpen = false;
                    for (var k = 0; k < iterCount; k++)
                    {
                        if (!exhausted[k])
                        {
                            anyOpen = true;
                            break;
                        }
                    }
                    if (!anyOpen)
                    {
                        return (true, JsValue.Undefined);
                    }
                }
                results[i] = value;
            }
            return (false, finishResults(results));
        };
        return helper;
    }

    /// <summary>§23.1.5.1 CreateArrayIterator — public factory used by
    /// <c>Array.prototype.{keys,values,entries}</c> and the <c>@@iterator</c>
    /// installer.</summary>
    public static JsValue CreateArrayIterator(JsRealm realm, JsValue thisV, ArrayIteratorKind kind)
    {
        if (thisV.IsNullish)
        {
            throw new JsThrow(realm.NewTypeError("Array iterator requires an object"));
        }

        var obj = thisV.IsObject ? thisV.AsObject : AbstractOperations.ToObject(realm, thisV);
        return JsValue.Object(new JsArrayIterator(realm, obj, kind));
    }

    /// <summary>§22.1.5.1 CreateStringIterator — used by
    /// <c>String.prototype[@@iterator]</c>.</summary>
    public static JsValue CreateStringIterator(JsRealm realm, string s)
        => JsValue.Object(new JsStringIterator(realm, s));

    // ------------------------------------------------------------------
    //                       Array iterator.next()
    // ------------------------------------------------------------------

    private static JsValue ArrayIteratorNext(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsArrayIterator it)
        {
            throw new JsThrow(realm.NewTypeError("Array Iterator.prototype.next called on incompatible receiver"));
        }

        return it.Next(realm);
    }

    // ------------------------------------------------------------------
    //                      String iterator.next()
    // ------------------------------------------------------------------

    private static JsValue StringIteratorNext(JsRealm realm, JsValue thisV)
    {
        if (!thisV.IsObject || thisV.AsObject is not JsStringIterator it)
        {
            throw new JsThrow(realm.NewTypeError("String Iterator.prototype.next called on incompatible receiver"));
        }

        return it.Next(realm);
    }

    /// <summary>Build the iterator-result object <c>{value, done}</c>.</summary>
    internal static JsValue MakeResult(JsRealm realm, JsValue value, bool done)
    {
        var obj = realm.NewOrdinaryObject();
        obj.DefineOwnProperty("value", PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true));
        obj.DefineOwnProperty("done", PropertyDescriptor.Data(JsValue.Boolean(done), writable: true, enumerable: true, configurable: true));
        return JsValue.Object(obj);
    }
}

/// <summary>Opaque VM-side handle wrapping an <see cref="IteratorRecord"/>.
/// Lives on the operand stack as a JsValue.Object so the existing dispatcher
/// doesn't need a new value kind. Never exposed to user code.</summary>
public sealed class JsIteratorRecordHandle : JsObject
{
    public IteratorRecord Record;

    /// <summary>wp:M3-04g — true when this record was produced by
    /// <c>GetAsyncIterator</c> on an object that only has a sync
    /// <c>[Symbol.iterator]</c> (CreateAsyncFromSyncIterator, §27.1.4.1).
    /// The driver wraps each sync iterator-result in a resolved Promise and
    /// awaits its <c>value</c>.</summary>
    public bool SyncWrapped;

    public JsIteratorRecordHandle(IteratorRecord record) : base(null)
    {
        Record = record;
    }
}

/// <summary>§23.1.5.2 Array Iterator kind tag — also reused for ArrayLike.</summary>
public enum ArrayIteratorKind
{
    Key,
    Value,
    KeyAndValue,
}

/// <summary>§23.1.5.2 Array Iterator instances. Carries the
/// [[IteratedObject]] + [[NextIndex]] + [[Kind]] internal slots inline.</summary>
public sealed class JsArrayIterator : JsObject
{
    private readonly JsObject _iterated;
    private readonly ArrayIteratorKind _kind;
    private int _nextIndex;
    private bool _done;

    public JsArrayIterator(JsRealm realm, JsObject iterated, ArrayIteratorKind kind)
        : base(realm.ArrayIteratorPrototype)
    {
        _iterated = iterated;
        _kind = kind;
        _nextIndex = 0;
    }

    public JsValue Next(JsRealm realm)
    {
        if (_done)
        {
            return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        }

        // §23.1.5.1 step 6.b.i — a typed array that went out of bounds
        // (shrunk/detached resizable buffer) throws from next(), it does not
        // complete quietly.
        if (_iterated is JsTypedArray { IsOutOfBounds: true })
        {
            throw new JsThrow(realm.NewTypeError("TypedArray is out of bounds"));
        }

        var len = GetLength(_iterated);
        if (_nextIndex >= len)
        {
            _done = true;
            return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        }
        var index = _nextIndex;
        _nextIndex++;
        switch (_kind)
        {
            case ArrayIteratorKind.Key:
                return IteratorIntrinsics.MakeResult(realm, JsValue.Number(index), done: false);
            case ArrayIteratorKind.Value:
                return IteratorIntrinsics.MakeResult(realm, GetElement(_iterated, index), done: false);
            case ArrayIteratorKind.KeyAndValue:
                var pair = new JsArray(realm);
                pair.Push(JsValue.Number(index));
                pair.Push(GetElement(_iterated, index));
                return IteratorIntrinsics.MakeResult(realm, JsValue.Object(pair), done: false);
            default:
                throw new InvalidOperationException($"unknown array iterator kind {_kind}");
        }
    }

    private static int GetLength(JsObject obj)
    {
        if (obj is JsArray arr)
        {
            return arr.Length;
        }

        // Typed arrays report length via a PROTOTYPE ACCESSOR (§23.2.3) which
        // the vm-less data-only Get below cannot invoke; read the exotic
        // object's live length directly (also picks up length-tracking views).
        if (obj is JsTypedArray ta)
        {
            return ta.Length;
        }

        var v = obj.Get("length");
        var n = JsValue.ToNumber(v);
        if (double.IsNaN(n) || n <= 0)
        {
            return 0;
        }

        if (n > int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)Math.Min(Math.Truncate(n), (double)(1L << 53) - 1);
    }

    private static JsValue GetElement(JsObject obj, int i)
    {
        if (obj is JsArray arr)
        {
            return arr[i];
        }

        return obj.Get(i.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>§27.1.2.1 Iterator Helper instances — the generator-like objects
/// returned by the lazy iterator helpers and Iterator.zip/zipKeyed. Carries
/// the [[UnderlyingIterators]] and a [[GeneratorState]]-equivalent.</summary>
public sealed class JsIteratorHelper : JsObject
{
    internal IteratorHelperState State = IteratorHelperState.SuspendedStart;
    internal IteratorRecord[] Records;
    internal Func<JsIteratorHelper, (bool Done, JsValue Value)> StepFn = null!;
    /// <summary>Custom close-underlying action used by return(); defaults to
    /// closing every non-done record in reverse order.</summary>
    internal Action<JsIteratorHelper>? CloseFn;

    internal JsIteratorHelper(JsRealm realm, IteratorRecord[] records)
        : base(realm.IteratorHelperPrototype)
    {
        Records = records;
    }

    internal IReadOnlyList<IteratorRecord> OpenRecords()
    {
        var open = new List<IteratorRecord>(Records.Length);
        foreach (var r in Records)
        {
            if (!r.Done)
            {
                open.Add(r);
            }
        }
        return open;
    }
}

internal enum IteratorHelperState : byte
{
    SuspendedStart = 0,
    SuspendedYield = 1,
    Executing = 2,
    Completed = 3,
}

/// <summary>§27.1.3.2.1 — the wrapper Iterator.from returns for iterators
/// that do not already inherit from %Iterator.prototype%.</summary>
public sealed class JsIteratorWrapper : JsObject
{
    internal IteratorRecord Iterated;

    internal JsIteratorWrapper(JsRealm realm, IteratorRecord record)
        : base(realm.WrapForValidIteratorPrototype)
    {
        Iterated = record;
    }
}

/// <summary>§22.1.5 String Iterator instances. Walks the underlying string
/// by Unicode code point (surrogate-pair-aware) so
/// <c>[..."😀ab"].length === 3</c>, matching ECMA-262.</summary>
public sealed class JsStringIterator : JsObject
{
    private readonly string _source;
    private int _nextIndex;
    private bool _done;

    public JsStringIterator(JsRealm realm, string source)
        : base(realm.StringIteratorPrototype)
    {
        _source = source ?? string.Empty;
        _nextIndex = 0;
    }

    public JsValue Next(JsRealm realm)
    {
        if (_done)
        {
            return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        }

        if (_nextIndex >= _source.Length)
        {
            _done = true;
            return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        }
        var first = _source[_nextIndex];
        string codePoint;
        if (char.IsHighSurrogate(first) && _nextIndex + 1 < _source.Length && char.IsLowSurrogate(_source[_nextIndex + 1]))
        {
            codePoint = _source.Substring(_nextIndex, 2);
            _nextIndex += 2;
        }
        else
        {
            codePoint = first.ToString();
            _nextIndex += 1;
        }
        return IteratorIntrinsics.MakeResult(realm, JsValue.String(codePoint), done: false);
    }
}
