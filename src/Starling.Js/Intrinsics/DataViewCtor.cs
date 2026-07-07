using System.Buffers.Binary;
using System.Numerics;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>ECMA-262 §25.3 DataView Objects.</summary>
public static class DataViewCtor
{
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        var proto = realm.DataViewPrototype;

        JsNativeFunction? ctor = null;
        ctor = new JsNativeFunction("DataView", (newTarget, args) =>
        {
            if (!IntrinsicHelpers.IsConstructInvocation(newTarget))
            {
                throw new JsThrow(realm.NewTypeError("DataView constructor requires 'new'"));
            }

            if (args.Length == 0 || !args[0].IsObject || args[0].AsObject is not JsArrayBuffer buffer)
            {
                throw new JsThrow(realm.NewTypeError("DataView buffer must be an ArrayBuffer"));
            }

            var offset = args.Length > 1 ? ArrayBufferCtor.ToIndex(realm, args[1]) : 0;
            if (buffer.IsDetached)
            {
                throw new JsThrow(realm.NewTypeError("DataView viewed buffer is detached"));
            }

            var bufferByteLength = buffer.ByteLength;
            if (offset > bufferByteLength)
            {
                throw new JsThrow(realm.NewRangeError("DataView byteOffset out of range"));
            }

            var lengthArg = args.Length > 2 ? args[2] : JsValue.Undefined;
            var lengthTracking = false;
            var viewByteLength = 0;
            if (lengthArg.IsUndefined)
            {
                if (buffer.IsResizable)
                {
                    lengthTracking = true;
                }
                else
                {
                    viewByteLength = bufferByteLength - offset;
                }
            }
            else
            {
                viewByteLength = ArrayBufferCtor.ToIndex(realm, lengthArg);
                if ((long)offset + viewByteLength > bufferByteLength)
                {
                    throw new JsThrow(realm.NewRangeError("DataView byteLength out of range"));
                }
            }

            // §25.3.2.1 steps 10-14: getting the prototype off new.target can run
            // user code that detaches or shrinks the buffer, so re-validate.
            var instProto = IntrinsicHelpers.NewTargetPrototype(realm.ActiveVm, newTarget, proto, r => r.DataViewPrototype);
            if (buffer.IsDetached)
            {
                throw new JsThrow(realm.NewTypeError("DataView viewed buffer is detached"));
            }

            bufferByteLength = buffer.ByteLength;
            if (offset > bufferByteLength)
            {
                throw new JsThrow(realm.NewRangeError("DataView byteOffset out of range"));
            }

            if (!lengthArg.IsUndefined && (long)offset + viewByteLength > bufferByteLength)
            {
                throw new JsThrow(realm.NewRangeError("DataView byteLength out of range"));
            }

            return JsValue.Object(new JsDataView(instProto, buffer, offset, viewByteLength, lengthTracking));
        }, isConstructor: true);
        ctor.SetPrototypeOf(realm.FunctionPrototype);
        ArrayBufferCtor.DefineData(ctor, "prototype", JsValue.Object(proto), false, false, false);
        ArrayBufferCtor.DefineData(ctor, "name", JsValue.String("DataView"), false, false, true);
        ArrayBufferCtor.DefineData(ctor, "length", JsValue.Number(1), false, false, true);
        ArrayBufferCtor.DefineData(proto, "constructor", JsValue.Object(ctor), true, false, true);
        proto.DefineOwnProperty(SymbolCtor.ToStringTag,
            PropertyDescriptor.Data(JsValue.String("DataView"), writable: false, enumerable: false, configurable: true));

        DefineAccessor(realm, proto, "buffer", thisV => JsValue.Object(ThisView(realm, thisV).Buffer));
        DefineAccessor(realm, proto, "byteLength", thisV => JsValue.Number(CheckedByteLength(realm, ThisView(realm, thisV))));
        DefineAccessor(realm, proto, "byteOffset", thisV =>
        {
            var view = ThisView(realm, thisV);
            if (view.IsOutOfBounds)
            {
                throw new JsThrow(realm.NewTypeError("DataView is out of bounds"));
            }

            return JsValue.Number(view.ByteOffset);
        });

        DefineGet(realm, proto, "getInt8", 1, (s, le) => JsValue.Number(unchecked((sbyte)s[0])));
        DefineGet(realm, proto, "getUint8", 1, (s, le) => JsValue.Number(s[0]));
        DefineGet(realm, proto, "getInt16", 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadInt16BigEndian(s)));
        DefineGet(realm, proto, "getUint16", 2, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadUInt16LittleEndian(s) : BinaryPrimitives.ReadUInt16BigEndian(s)));
        DefineGet(realm, proto, "getInt32", 4, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadInt32LittleEndian(s) : BinaryPrimitives.ReadInt32BigEndian(s)));
        DefineGet(realm, proto, "getUint32", 4, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s)));
        DefineGet(realm, proto, "getFloat16", 2, (s, le) => JsValue.Number((double)(le ? BinaryPrimitives.ReadHalfLittleEndian(s) : BinaryPrimitives.ReadHalfBigEndian(s))));
        DefineGet(realm, proto, "getFloat32", 4, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadSingleLittleEndian(s) : BinaryPrimitives.ReadSingleBigEndian(s)));
        DefineGet(realm, proto, "getFloat64", 8, (s, le) => JsValue.Number(le ? BinaryPrimitives.ReadDoubleLittleEndian(s) : BinaryPrimitives.ReadDoubleBigEndian(s)));
        DefineGet(realm, proto, "getBigInt64", 8, (s, le) => JsValue.BigInt(new BigInteger(le ? BinaryPrimitives.ReadInt64LittleEndian(s) : BinaryPrimitives.ReadInt64BigEndian(s))));
        DefineGet(realm, proto, "getBigUint64", 8, (s, le) => JsValue.BigInt(new BigInteger(le ? BinaryPrimitives.ReadUInt64LittleEndian(s) : BinaryPrimitives.ReadUInt64BigEndian(s))));

        DefineSet(realm, proto, "setInt8", 1, isBigInt: false, (s, n, le) => s[0] = unchecked((byte)(sbyte)ToInt32(n)));
        DefineSet(realm, proto, "setUint8", 1, isBigInt: false, (s, n, le) => s[0] = unchecked((byte)ToUint32(n)));
        DefineSet(realm, proto, "setInt16", 2, isBigInt: false, (s, n, le) => Write(le, s, unchecked((short)ToInt32(n))));
        DefineSet(realm, proto, "setUint16", 2, isBigInt: false, (s, n, le) => Write(le, s, unchecked((ushort)ToUint32(n))));
        DefineSet(realm, proto, "setInt32", 4, isBigInt: false, (s, n, le) => Write(le, s, ToInt32(n)));
        DefineSet(realm, proto, "setUint32", 4, isBigInt: false, (s, n, le) => Write(le, s, ToUint32(n)));
        DefineSet(realm, proto, "setFloat16", 2, isBigInt: false, (s, n, le) =>
        {
            var h = (Half)n;
            if (le)
            {
                BinaryPrimitives.WriteHalfLittleEndian(s, h);
            }
            else
            {
                BinaryPrimitives.WriteHalfBigEndian(s, h);
            }
        });
        DefineSet(realm, proto, "setFloat32", 4, isBigInt: false, (s, n, le) =>
        {
            var f = (float)n;
            if (le)
            {
                BinaryPrimitives.WriteSingleLittleEndian(s, f);
            }
            else
            {
                BinaryPrimitives.WriteSingleBigEndian(s, f);
            }
        });
        DefineSet(realm, proto, "setFloat64", 8, isBigInt: false, (s, n, le) =>
        {
            if (le)
            {
                BinaryPrimitives.WriteDoubleLittleEndian(s, n);
            }
            else
            {
                BinaryPrimitives.WriteDoubleBigEndian(s, n);
            }
        });
        DefineBigSet(realm, proto, "setBigInt64", (s, b, le) =>
        {
            var n = (long)BigIntOps.AsIntN(realm, 64, b);
            if (le)
            {
                BinaryPrimitives.WriteInt64LittleEndian(s, n);
            }
            else
            {
                BinaryPrimitives.WriteInt64BigEndian(s, n);
            }
        });
        DefineBigSet(realm, proto, "setBigUint64", (s, b, le) =>
        {
            var n = (ulong)BigIntOps.AsUintN(realm, 64, b);
            if (le)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(s, n);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(s, n);
            }
        });

        realm.GlobalObject.DefineOwnProperty("DataView", PropertyDescriptor.Data(JsValue.Object(ctor), true, false, true));
    }

    private static void DefineAccessor(JsRealm realm, JsObject proto, string name, Func<JsValue, JsValue> getter)
    {
        var fn = new JsNativeFunction(realm, "get " + name, 0, (thisV, _) => getter(thisV), isConstructor: false);
        proto.DefineOwnProperty(name, PropertyDescriptor.Accessor(fn, setter: null, enumerable: false, configurable: true));
    }

    private static void DefineGet(JsRealm realm, JsObject proto, string name, int size, Func<ReadOnlySpan<byte>, bool, JsValue> read)
        => ArrayBufferCtor.DefineMethod(realm, proto, name, (thisV, args) =>
        {
            // §25.3.1.1 GetViewValue: receiver check, ToIndex, ToBoolean, then
            // bounds validation (detach/OOB = TypeError, range = RangeError).
            var view = ThisView(realm, thisV);
            var offset = ArrayBufferCtor.ToIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var le = args.Length > 1 && JsValue.ToBoolean(args[1]);
            var span = CheckedSpan(realm, view, offset, size);
            return read(span, le);
        }, 1);

    private static void DefineSet(JsRealm realm, JsObject proto, string name, int size, bool isBigInt, Action<Span<byte>, double, bool> write)
        => ArrayBufferCtor.DefineMethod(realm, proto, name, (thisV, args) =>
        {
            // §25.3.1.4 SetViewValue: immutable check precedes all coercion; the
            // value coercion precedes the bounds checks.
            var view = ThisView(realm, thisV);
            if (view.Buffer.IsImmutable)
            {
                throw new JsThrow(realm.NewTypeError("Cannot write into an immutable ArrayBuffer"));
            }

            var offset = ArrayBufferCtor.ToIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var n = ArrayBufferCtor.Number(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var le = args.Length > 2 && JsValue.ToBoolean(args[2]);
            var span = CheckedSpan(realm, view, offset, size);
            write(span, n, le);
            return JsValue.Undefined;
        }, 2);

    private static void DefineBigSet(JsRealm realm, JsObject proto, string name, Action<Span<byte>, BigInteger, bool> write)
        => ArrayBufferCtor.DefineMethod(realm, proto, name, (thisV, args) =>
        {
            var view = ThisView(realm, thisV);
            if (view.Buffer.IsImmutable)
            {
                throw new JsThrow(realm.NewTypeError("Cannot write into an immutable ArrayBuffer"));
            }

            var offset = ArrayBufferCtor.ToIndex(realm, args.Length > 0 ? args[0] : JsValue.Undefined);
            var b = ToBigInt(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var le = args.Length > 2 && JsValue.ToBoolean(args[2]);
            var span = CheckedSpan(realm, view, offset, 8);
            write(span, b, le);
            return JsValue.Undefined;
        }, 2);

    private static JsDataView ThisView(JsRealm realm, JsValue thisV)
        => thisV.IsObject && thisV.AsObject is JsDataView view
            ? view
            : throw new JsThrow(realm.NewTypeError("DataView method called on incompatible receiver"));

    private static int CheckedByteLength(JsRealm realm, JsDataView view)
    {
        if (view.IsOutOfBounds)
        {
            throw new JsThrow(realm.NewTypeError("DataView is out of bounds"));
        }

        return view.ViewByteLength;
    }

    private static Span<byte> CheckedSpan(JsRealm realm, JsDataView view, int offset, int size)
    {
        var viewSize = CheckedByteLength(realm, view);
        if ((long)offset + size > viewSize)
        {
            throw new JsThrow(realm.NewRangeError("DataView byteOffset out of range"));
        }

        return view.Buffer.GetSpan(view.ByteOffset + offset, size);
    }

    private static BigInteger ToBigInt(JsRealm realm, JsValue value)
    {
        if (value.IsObject)
        {
            value = AbstractOperations.ToPrimitive(realm.ActiveVm, value, "number");
        }

        return value.Kind switch
        {
            JsValueKind.BigInt => value.AsBigInt,
            JsValueKind.Boolean => value.AsBool ? BigInteger.One : BigInteger.Zero,
            JsValueKind.String => BigIntOps.ParseStringToBigInt(realm, value.AsString),
            _ => throw new JsThrow(realm.NewTypeError("Cannot convert value to BigInt"))
        };
    }

    // ECMA-262 §7.1.6 / §7.1.7 conversions used by §25.3 setters.
    private static int ToInt32(double n) => unchecked((int)ToUint32(n));

    private static uint ToUint32(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0)
        {
            return 0;
        }

        var t = Math.Truncate(n);
        var mod = t - Math.Floor(t / 4294967296d) * 4294967296d;
        return (uint)mod;
    }

    private static void Write(bool le, Span<byte> s, short v)
    {
        if (le)
        {
            BinaryPrimitives.WriteInt16LittleEndian(s, v);
        }
        else
        {
            BinaryPrimitives.WriteInt16BigEndian(s, v);
        }
    }
    private static void Write(bool le, Span<byte> s, ushort v)
    {
        if (le)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(s, v);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(s, v);
        }
    }
    private static void Write(bool le, Span<byte> s, int v)
    {
        if (le)
        {
            BinaryPrimitives.WriteInt32LittleEndian(s, v);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(s, v);
        }
    }
    private static void Write(bool le, Span<byte> s, uint v)
    {
        if (le)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(s, v);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(s, v);
        }
    }
}
