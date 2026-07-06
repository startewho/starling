using System.Globalization;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-262 §25.1 ArrayBuffer Objects.</summary>
public static class ArrayBufferCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.ArrayBufferPrototype;

        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction("ArrayBuffer", (newTarget, args) =>
        {
            // §25.1.4.1 step 1: requires `new`. OrdinaryCreateFromConstructor
            // picks the prototype from new.target for `class B extends ArrayBuffer {}`.
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("ArrayBuffer constructor requires 'new'"));
            }

            var length = ToIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var maxByteLength = GetMaxByteLengthOption(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            if (maxByteLength >= 0 && length > maxByteLength)
            {
                throw new JsThrow(realm.NewRangeError("ArrayBuffer length exceeds maxByteLength"));
            }

            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto);
            return JsValue.Object(maxByteLength >= 0
                ? new JsArrayBuffer(instProto, length, maxByteLength)
                : new JsArrayBuffer(instProto, length));
        }, isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        DefineData(ctor, "prototype", JsValue.Object(proto), false, false, false);
        DefineData(ctor, "name", JsValue.String("ArrayBuffer"), false, false, true);
        DefineData(ctor, "length", JsValue.Number(1), false, false, true);
        ctor.DefineOwnProperty(SymbolCtor.Species,
            PropertyDescriptor.Accessor(
                new JsNativeFunction(realm, "get [Symbol.species]", 0, (thisV, _) => thisV, isConstructor: false), null));
        DefineData(proto, "constructor", JsValue.Object(ctor), true, false, true);
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("ArrayBuffer"), writable: false, enumerable: false, configurable: true));

        DefineMethod(realm, proto, "slice", (thisV, args) => Slice(realm, thisV, args), 2);
        DefineMethod(realm, proto, "resize", (thisV, args) => Resize(realm, thisV, args), 1);
        DefineMethod(realm, proto, "transfer", (thisV, args) => Transfer(realm, thisV, args, toImmutable: false), 0);
        DefineMethod(realm, proto, "transferToImmutable", (thisV, args) => Transfer(realm, thisV, args, toImmutable: true), 0);
        DefineMethod(realm, ctor, "isView", (_, args) => JsValue.Boolean(args.Length > 0 && IsView(args[0])), 1);
        DefineGetter(realm, proto, "resizable", thisV => JsValue.Boolean(ThisBuffer(realm, thisV, "resizable").IsResizable));
        DefineGetter(realm, proto, "detached", thisV => JsValue.Boolean(ThisBuffer(realm, thisV, "detached").IsDetached));
        DefineGetter(realm, proto, "immutable", thisV => JsValue.Boolean(ThisBuffer(realm, thisV, "immutable").IsImmutable));
        DefineGetter(realm, proto, "maxByteLength", thisV =>
        {
            var buffer = ThisBuffer(realm, thisV, "maxByteLength");
            if (buffer.IsDetached)
            {
                return JsValue.Number(0);
            }

            return JsValue.Number(buffer.IsResizable ? buffer.MaxByteLength : buffer.ByteLength);
        });

        realm.GlobalObject.DefineOwnProperty("ArrayBuffer", PropertyDescriptor.Data(JsValue.Object(ctor), true, false, true));
    }

    private static JsArrayBuffer ThisBuffer(JsRealm realm, JsValue thisV, string what)
        => thisV.IsObject && thisV.AsObject is JsArrayBuffer b
            ? b
            : throw new JsThrow(realm.NewTypeError("ArrayBuffer." + what + " called on incompatible receiver"));

    private static void DefineGetter(JsRealm realm, JsObject proto, string name, Func<JsValue, JsValue> getter)
    {
        var fn = new JsNativeFunction(realm, "get " + name, 0, (thisV, _) => getter(thisV), isConstructor: false);
        proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(fn, setter: null, enumerable: false, configurable: true));
    }

    /// <summary>§25.1.3.7 GetArrayBufferMaxByteLengthOption; -1 = fixed length.</summary>
    private static int GetMaxByteLengthOption(JsRealm realm, JsValue options)
    {
        if (!options.IsObject)
        {
            return -1;
        }

        var maxV = AbstractOperations.Get(realm.ActiveVm, options.AsObject, "maxByteLength");
        if (maxV.IsUndefined)
        {
            return -1;
        }

        return ToIndex(realm, maxV);
    }

    private static JsValue Resize(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var buffer = ThisBuffer(realm, thisV, "prototype.resize");
        if (!buffer.IsResizable)
        {
            throw new JsThrow(realm.NewTypeError("ArrayBuffer.prototype.resize requires a resizable buffer"));
        }

        var newByteLength = ToIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        if (buffer.IsDetached)
        {
            throw new JsThrow(realm.NewTypeError("ArrayBuffer is detached"));
        }

        if (newByteLength > buffer.MaxByteLength)
        {
            throw new JsThrow(realm.NewRangeError("ArrayBuffer.prototype.resize beyond maxByteLength"));
        }

        buffer.Resize(newByteLength);
        return JsValue.Undefined;
    }

    /// <summary>§25.1.3.1 ArrayBufferCopyAndDetach — backs both
    /// <c>transfer</c> (preserves resizability) and <c>transferToImmutable</c>.</summary>
    private static JsValue Transfer(JsRealm realm, JsValue thisV, JsValue[] args, bool toImmutable)
    {
        var buffer = ThisBuffer(realm, thisV, toImmutable ? "prototype.transferToImmutable" : "prototype.transfer");
        if (buffer.IsImmutable)
        {
            throw new JsThrow(realm.NewTypeError("Cannot transfer an immutable ArrayBuffer"));
        }

        var lengthArg = args.Length > 0 ? args[0] : JsValue.Undefined;
        var newByteLength = lengthArg.IsUndefined ? buffer.ByteLength : ToIndex(realm, lengthArg);
        if (buffer.IsDetached)
        {
            throw new JsThrow(realm.NewTypeError("ArrayBuffer is detached"));
        }

        var preserveResizable = !toImmutable && buffer.IsResizable;
        if (preserveResizable && newByteLength > buffer.MaxByteLength)
        {
            throw new JsThrow(realm.NewRangeError("ArrayBuffer.prototype.transfer beyond maxByteLength"));
        }

        var result = preserveResizable
            ? new JsArrayBuffer(realm.ArrayBufferPrototype, newByteLength, buffer.MaxByteLength)
            : new JsArrayBuffer(realm.ArrayBufferPrototype, newByteLength);
        var src = buffer.GetSpan();
        var copy = Math.Min(src.Length, newByteLength);
        src[..copy].CopyTo(result.GetSpan(0, copy));
        buffer.Detach();
        if (toImmutable)
        {
            result.MarkImmutable();
        }

        return JsValue.Object(result);
    }

    private static JsValue Slice(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var buffer = thisV.IsObject && thisV.AsObject is JsArrayBuffer b
            ? b
            : throw new JsThrow(realm.NewTypeError("ArrayBuffer.prototype.slice called on incompatible receiver"));
        if (buffer.IsDetached)
        {
            throw new JsThrow(realm.NewTypeError("ArrayBuffer is detached"));
        }

        var len = buffer.ByteLength;
        var relativeStart = ToIntegerOrInfinity(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
        var first = relativeStart < 0 ? Math.Max(len + relativeStart, 0) : Math.Min(relativeStart, len);
        var relativeEnd = args.Length > 1 && !args[1].IsUndefined ? ToIntegerOrInfinity(realm, args[1]) : len;
        var final = relativeEnd < 0 ? Math.Max(len + relativeEnd, 0) : Math.Min(relativeEnd, len);
        return JsValue.Object(buffer.Slice(realm.ArrayBufferPrototype, (int)first, (int)final));
    }

    private static bool IsView(JsValue value)
        => value.IsObject && value.AsObject is JsTypedArray or JsDataView;

    internal static int ToIndex(JsRealm realm, JsValue value)
    {
        var n = Number(realm, value);
        if (double.IsNaN(n) || n == 0)
        {
            return 0;
        }

        n = Math.Truncate(n);
        if (n < 0)
        {
            throw new JsThrow(realm.NewRangeError("ArrayBuffer length is out of range"));
        }

        if (!double.IsFinite(n) || n > int.MaxValue)
        {
            throw new JsThrow(realm.NewRangeError("ArrayBuffer length is out of range"));
        }

        return (int)n;
    }

    internal static double ToIntegerOrInfinity(JsValue value)
    {
        var n = Number(value);
        return ToIntegerOrInfinityCore(n);
    }

    internal static double ToIntegerOrInfinity(JsRealm realm, JsValue value)
    {
        var n = Number(realm, value);
        return ToIntegerOrInfinityCore(n);
    }

    private static double ToIntegerOrInfinityCore(double n)
    {
        if (double.IsNaN(n) || n == 0)
        {
            return 0;
        }

        if (double.IsInfinity(n))
        {
            return n;
        }

        return Math.Truncate(n);
    }

    internal static double Number(JsValue value)
        => value.IsObject ? JsValue.ToNumber(AbstractOperations.ToPrimitive(value, "number")) : JsValue.ToNumber(value);

    internal static double Number(JsRealm realm, JsValue value)
    {
        try
        {
            return value.IsObject
                ? JsValue.ToNumber(AbstractOperations.ToPrimitive(realm.ActiveVm, value, "number"))
                : JsValue.ToNumber(value);
        }
        catch (InvalidOperationException ex)
        {
            throw new JsThrow(realm.NewTypeError(ex.Message));
        }
    }

    internal static string IndexKey(int i) => i.ToString(CultureInfo.InvariantCulture);

    internal static void DefineMethod(JsRealm realm, JsObject target, string name, Func<JsValue, JsValue[], JsValue> body, int length)
        => IntrinsicHelpers.DefineMethod(realm, target, name, length, body);

    internal static void DefineData(JsObject target, string name, JsValue value, bool writable, bool enumerable, bool configurable)
        => target.DefineOwnProperty(name, PropertyDescriptor.Data(value, writable, enumerable, configurable));
}
