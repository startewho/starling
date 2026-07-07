using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace Starling.Js.Runtime;

public enum JsTypedArrayKind
{
    Int8,
    Uint8,
    Uint8Clamped,
    Int16,
    Uint16,
    Int32,
    Uint32,
    Float32,
    Float64,
    BigInt64,
    BigUint64,
}

/// <summary>ECMA-262 §10.4.5 TypedArray (integer-indexed) exotic object backed
/// by an ArrayBuffer. Canonical numeric index keys resolve against the element
/// storage and never fall back to ordinary properties.</summary>
public sealed class JsTypedArray : JsObject
{
    private readonly int _fixedLength;

    public JsTypedArray(JsObject? prototype, JsTypedArrayKind kind, JsArrayBuffer buffer, int byteOffset, int length,
        bool lengthTracking = false, JsRealm? realm = null) : base(prototype)
    {
        DisableInlineCache();
        Kind = kind;
        Buffer = buffer;
        ByteOffset = byteOffset;
        _fixedLength = length;
        IsLengthTracking = lengthTracking;
        Realm = realm;
    }

    public JsTypedArrayKind Kind { get; }
    public JsArrayBuffer Buffer { get; }
    public int ByteOffset { get; }
    public bool IsLengthTracking { get; }

    /// <summary>§10.4.5.2 [[PreventExtensions]] — only a FIXED-length view
    /// (auto-length false AND non-resizable buffer) can become non-extensible;
    /// Object.freeze/seal on a resizable-buffer view is a TypeError.</summary>
    public override bool PreventExtensions()
    {
        if (IsLengthTracking || Buffer.MaxByteLength >= 0)
        {
            return false;
        }

        return base.PreventExtensions();
    }

    /// <summary>Creation realm, when known — lets coercions inside the exotic
    /// [[Set]] surface JS TypeErrors instead of host exceptions.</summary>
    public JsRealm? Realm { get; set; }

    public int BytesPerElement => BytesPerElementOf(Kind);

    public bool IsBigIntKind => Kind is JsTypedArrayKind.BigInt64 or JsTypedArrayKind.BigUint64;

    /// <summary>§10.4.5.13 IsTypedArrayOutOfBounds (with the detached case folded in).</summary>
    public bool IsOutOfBounds
    {
        get
        {
            if (Buffer.IsDetached)
            {
                return true;
            }

            if (ByteOffset > Buffer.ByteLength)
            {
                return true;
            }

            return !IsLengthTracking && ByteOffset + (long)_fixedLength * BytesPerElement > Buffer.ByteLength;
        }
    }

    /// <summary>§10.4.5.12 TypedArrayLength — 0 when detached or out of bounds.</summary>
    public int Length
    {
        get
        {
            if (IsOutOfBounds)
            {
                return 0;
            }

            return IsLengthTracking ? (Buffer.ByteLength - ByteOffset) / BytesPerElement : _fixedLength;
        }
    }

    public int ByteLength => Length * BytesPerElement;

    public static int BytesPerElementOf(JsTypedArrayKind kind) => kind switch
    {
        JsTypedArrayKind.Int8 or JsTypedArrayKind.Uint8 or JsTypedArrayKind.Uint8Clamped => 1,
        JsTypedArrayKind.Int16 or JsTypedArrayKind.Uint16 => 2,
        JsTypedArrayKind.Int32 or JsTypedArrayKind.Uint32 or JsTypedArrayKind.Float32 => 4,
        _ => 8,
    };

    /// <summary>§23.2.3.34 [[TypedArrayName]] — concrete constructor name
    /// (e.g. "Uint8Array", "Float32Array"). Read by the
    /// <c>%TypedArray%.prototype[@@toStringTag]</c> accessor.</summary>
    public string ConstructorName => Kind switch
    {
        JsTypedArrayKind.Int8 => "Int8Array",
        JsTypedArrayKind.Uint8 => "Uint8Array",
        JsTypedArrayKind.Uint8Clamped => "Uint8ClampedArray",
        JsTypedArrayKind.Int16 => "Int16Array",
        JsTypedArrayKind.Uint16 => "Uint16Array",
        JsTypedArrayKind.Int32 => "Int32Array",
        JsTypedArrayKind.Uint32 => "Uint32Array",
        JsTypedArrayKind.Float32 => "Float32Array",
        JsTypedArrayKind.Float64 => "Float64Array",
        JsTypedArrayKind.BigInt64 => "BigInt64Array",
        JsTypedArrayKind.BigUint64 => "BigUint64Array",
        _ => throw new InvalidOperationException(),
    };

    /// <summary>§7.1.21 CanonicalNumericIndexString — true when <paramref name="name"/>
    /// is "-0" or round-trips through ToNumber/ToString.</summary>
    internal static bool TryCanonicalNumericIndex(string name, out double index)
    {
        if (name.Length == 0)
        {
            index = 0;
            return false;
        }

        // Fast path: plain decimal integers without a leading zero.
        var allDigits = true;
        for (var i = 0; i < name.Length; i++)
        {
            if (name[i] is < '0' or > '9')
            {
                allDigits = false;
                break;
            }
        }

        if (allDigits)
        {
            if (name.Length > 1 && name[0] == '0')
            {
                index = 0;
                return false;
            }

            if (name.Length <= 15)
            {
                index = double.Parse(name, NumberStyles.None, CultureInfo.InvariantCulture);
                return true;
            }
        }

        if (name == "-0")
        {
            index = -0.0;
            return true;
        }

        var first = name[0];
        if (first != '-' && first != '.' && (first < '0' || first > '9')
            && name != "NaN" && name != "Infinity" && name != "-Infinity")
        {
            index = 0;
            return false;
        }

        var n = JsValue.ToNumber(JsValue.String(name));
        if (JsValue.ToStringValue(JsValue.Number(n)) == name)
        {
            index = n;
            return true;
        }

        index = 0;
        return false;
    }

    /// <summary>§10.4.5.14 IsValidIntegerIndex.</summary>
    public bool IsValidIndex(double index)
    {
        if (double.IsNaN(index) || double.IsInfinity(index) || Math.Truncate(index) != index)
        {
            return false;
        }

        if (index == 0 && double.IsNegative(index))
        {
            return false;
        }

        return index >= 0 && index < Length;
    }

    public override JsValue Get(string name)
    {
        if (TryCanonicalNumericIndex(name, out var index))
        {
            return IsValidIndex(index) ? GetElement((int)index) : JsValue.Undefined;
        }

        return base.Get(name);
    }

    public override void Set(string name, JsValue value)
    {
        if (TryCanonicalNumericIndex(name, out var index))
        {
            SetIntegerIndexed(index, value, Realm);
            return;
        }
        base.Set(name, value);
    }

    public override bool Has(string name)
    {
        if (TryCanonicalNumericIndex(name, out var index))
        {
            return IsValidIndex(index);
        }

        return base.Has(name);
    }

    public override bool HasOwn(string name)
    {
        if (TryCanonicalNumericIndex(name, out var index))
        {
            return IsValidIndex(index);
        }

        return base.HasOwn(name);
    }

    public override PropertyDescriptor? GetOwnPropertyDescriptor(string name)
    {
        if (TryCanonicalNumericIndex(name, out var index))
        {
            if (!IsValidIndex(index))
            {
                return null;
            }

            return PropertyDescriptor.Data(GetElement((int)index), writable: true, enumerable: true, configurable: true);
        }

        return base.GetOwnPropertyDescriptor(name);
    }

    public override bool DefineOwnProperty(string name, PropertyDescriptor desc)
    {
        if (TryCanonicalNumericIndex(name, out var index))
        {
            return DefineIntegerIndexed(index, desc,
                DescriptorFields.Build(value: desc.IsData, writable: desc.IsData, enumerable: true, configurable: true, get: desc.IsAccessor, set: desc.IsAccessor));
        }

        return base.DefineOwnProperty(name, desc);
    }

    internal override bool DefineOwnPropertyPartial(JsPropertyKey key, PropertyDescriptor desc, DescriptorFields present)
    {
        if (key.IsString && TryCanonicalNumericIndex(key.AsString, out var index))
        {
            return DefineIntegerIndexed(index, desc, present);
        }

        return base.DefineOwnPropertyPartial(key, desc, present);
    }

    /// <summary>§10.4.5.3 [[DefineOwnProperty]] for a canonical numeric index.</summary>
    private bool DefineIntegerIndexed(double index, PropertyDescriptor desc, DescriptorFields present)
    {
        if (!IsValidIndex(index))
        {
            return false;
        }

        if (present.HasGet || present.HasSet || desc.IsAccessor)
        {
            return false;
        }

        if (present.HasConfigurable && !desc.Configurable)
        {
            return false;
        }

        if (present.HasEnumerable && !desc.Enumerable)
        {
            return false;
        }

        if (present.HasWritable && !desc.Writable)
        {
            return false;
        }

        if (present.HasValue)
        {
            SetIntegerIndexed(index, desc.Value, Realm);
        }

        return true;
    }

    public override bool Delete(string name)
    {
        if (TryCanonicalNumericIndex(name, out var index))
        {
            return !IsValidIndex(index);
        }

        return base.Delete(name);
    }

    public override IEnumerable<string> Keys
    {
        get
        {
            var len = Length;
            for (var i = 0; i < len; i++)
            {
                yield return IndexToString(i);
            }

            foreach (var key in base.Keys)
            {
                yield return key;
            }
        }
    }

    public override IEnumerable<JsPropertyKey> OwnPropertyKeys
    {
        get
        {
            var len = Length;
            for (var i = 0; i < len; i++)
            {
                yield return JsPropertyKey.String(IndexToString(i));
            }

            foreach (var key in base.OwnPropertyKeys)
            {
                yield return key;
            }
        }
    }

    public override IEnumerable<string> EnumerableKeys()
    {
        var len = Length;
        for (var i = 0; i < len; i++)
        {
            yield return IndexToString(i);
        }

        foreach (var key in base.EnumerableKeys())
        {
            yield return key;
        }
    }

    private static string IndexToString(int i) => i.ToString(CultureInfo.InvariantCulture);

    public JsValue GetElement(int index)
    {
        var offset = CheckedOffset(index);
        var bytes = Buffer.GetSpan();
        var span = bytes[offset..];
        return Kind switch
        {
            JsTypedArrayKind.Int8 => JsValue.Number(unchecked((sbyte)bytes[offset])),
            JsTypedArrayKind.Uint8 or JsTypedArrayKind.Uint8Clamped => JsValue.Number(bytes[offset]),
            JsTypedArrayKind.Int16 => JsValue.Number(BinaryPrimitives.ReadInt16LittleEndian(span)),
            JsTypedArrayKind.Uint16 => JsValue.Number(BinaryPrimitives.ReadUInt16LittleEndian(span)),
            JsTypedArrayKind.Int32 => JsValue.Number(BinaryPrimitives.ReadInt32LittleEndian(span)),
            JsTypedArrayKind.Uint32 => JsValue.Number(BinaryPrimitives.ReadUInt32LittleEndian(span)),
            JsTypedArrayKind.Float32 => JsValue.Number(BinaryPrimitives.ReadSingleLittleEndian(span)),
            JsTypedArrayKind.Float64 => JsValue.Number(BinaryPrimitives.ReadDoubleLittleEndian(span)),
            JsTypedArrayKind.BigInt64 => JsValue.BigInt(new BigInteger(BinaryPrimitives.ReadInt64LittleEndian(span))),
            JsTypedArrayKind.BigUint64 => JsValue.BigInt(new BigInteger(BinaryPrimitives.ReadUInt64LittleEndian(span))),
            _ => JsValue.Undefined,
        };
    }

    /// <summary>§10.4.5.16 TypedArraySetElement — coerce first (observable side
    /// effects), then re-check validity and drop the write when out of bounds,
    /// detached, or immutable.</summary>
    public void SetIntegerIndexed(double index, JsValue value, JsRealm? realm)
    {
        if (IsBigIntKind)
        {
            var b = ToBigInt(value, realm);
            if (IsValidIndex(index) && !Buffer.IsImmutable)
            {
                WriteBigInt((int)index, b);
            }

            return;
        }

        var n = ToNumber(value, realm);
        if (IsValidIndex(index) && !Buffer.IsImmutable)
        {
            WriteNumber((int)index, n);
        }
    }

    public void SetElement(int index, JsValue value, JsRealm? realm = null)
        => SetIntegerIndexed(index, value, realm ?? Realm);

    private void WriteBigInt(int index, BigInteger b)
    {
        var span = Buffer.GetSpan()[CheckedOffset(index)..];
        if (Kind == JsTypedArrayKind.BigInt64)
        {
            BinaryPrimitives.WriteInt64LittleEndian(span, (long)AsIntN(64, b));
        }
        else
        {
            BinaryPrimitives.WriteUInt64LittleEndian(span, (ulong)AsUintN(64, b));
        }
    }

    private void WriteNumber(int index, double n)
    {
        var offset = CheckedOffset(index);
        var bytes = Buffer.GetSpan();
        var span = bytes[offset..];
        switch (Kind)
        {
            case JsTypedArrayKind.Int8:
                bytes[offset] = unchecked((byte)(sbyte)ToInt32(n));
                break;
            case JsTypedArrayKind.Uint8:
                bytes[offset] = unchecked((byte)ToUint32(n));
                break;
            case JsTypedArrayKind.Uint8Clamped:
                bytes[offset] = ToUint8Clamp(n);
                break;
            case JsTypedArrayKind.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(span, unchecked((short)ToInt32(n)));
                break;
            case JsTypedArrayKind.Uint16:
                BinaryPrimitives.WriteUInt16LittleEndian(span, unchecked((ushort)ToUint32(n)));
                break;
            case JsTypedArrayKind.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(span, ToInt32(n));
                break;
            case JsTypedArrayKind.Uint32:
                BinaryPrimitives.WriteUInt32LittleEndian(span, ToUint32(n));
                break;
            case JsTypedArrayKind.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(span, (float)n);
                break;
            case JsTypedArrayKind.Float64:
                BinaryPrimitives.WriteDoubleLittleEndian(span, n);
                break;
        }
    }

    public JsTypedArray CreateSameKind(JsObject? prototype, JsArrayBuffer buffer, int byteOffset, int length)
        => new(prototype, Kind, buffer, byteOffset, length, lengthTracking: false, Realm);

    private int CheckedOffset(int index)
    {
        if ((uint)index >= (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return ByteOffset + index * BytesPerElement;
    }

    private static double ToNumber(JsValue value, JsRealm? realm)
    {
        try
        {
            if (!value.IsObject)
            {
                return JsValue.ToNumber(value);
            }

            return JsValue.ToNumber(AbstractOperations.ToPrimitive(realm?.ActiveVm, value, "number"));
        }
        catch (InvalidOperationException ex) when (realm is not null)
        {
            throw new JsThrow(realm.NewTypeError(ex.Message));
        }
    }

    private static BigInteger ToBigInt(JsValue value, JsRealm? realm)
    {
        if (value.IsObject)
        {
            value = AbstractOperations.ToPrimitive(realm?.ActiveVm, value, "number");
        }

        return value.Kind switch
        {
            JsValueKind.BigInt => value.AsBigInt,
            JsValueKind.Boolean => value.AsBool ? BigInteger.One : BigInteger.Zero,
            JsValueKind.String when realm is not null => BigIntOps.ParseStringToBigInt(realm, value.AsString),
            JsValueKind.String => BigInteger.Parse(value.AsString, CultureInfo.InvariantCulture),
            _ when realm is not null => throw new JsThrow(realm.NewTypeError("Cannot convert value to BigInt")),
            _ => throw new InvalidOperationException("Cannot convert value to BigInt"),
        };
    }

    private static BigInteger AsIntN(int bits, BigInteger value)
    {
        var mod = BigInteger.One << bits;
        var rem = ((value % mod) + mod) % mod;
        var signBit = BigInteger.One << (bits - 1);
        return rem >= signBit ? rem - mod : rem;
    }

    private static BigInteger AsUintN(int bits, BigInteger value)
    {
        var mod = BigInteger.One << bits;
        var rem = value % mod;
        return rem.Sign < 0 ? rem + mod : rem;
    }

    // ECMA-262 §7.1.6 ToInt32: truncate, modulo 2^32, reinterpret signed.
    private static int ToInt32(double n) => unchecked((int)ToUint32(n));

    // ECMA-262 §7.1.7 ToUint32: truncate and wrap modulo 2^32.
    private static uint ToUint32(double n)
    {
        if (double.IsNaN(n) || double.IsInfinity(n) || n == 0)
        {
            return 0;
        }

        var m = Math.Truncate(n) % 4294967296d;
        if (m < 0)
        {
            m += 4294967296d;
        }

        return m >= 4294967296d ? 0u : (uint)m;
    }

    // ECMA-262 §7.1.12 ToUint8Clamp: clamp, then round half to even.
    private static byte ToUint8Clamp(double n)
    {
        if (double.IsNaN(n) || n <= 0)
        {
            return 0;
        }

        if (n >= 255)
        {
            return 255;
        }

        var f = Math.Floor(n);
        if (n < f + 0.5)
        {
            return (byte)f;
        }

        if (n > f + 0.5)
        {
            return (byte)(f + 1);
        }

        return (byte)(((int)f % 2) == 0 ? f : f + 1);
    }

    public override string ToString() => $"[object {Kind}Array]";
}
