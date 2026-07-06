namespace Starling.Js.Runtime;

/// <summary>ECMA-262 §25.1 ArrayBuffer object with pluggable [[ArrayBufferData]].</summary>
public sealed class JsArrayBuffer : JsObject
{
    public JsArrayBuffer(JsObject? prototype, int byteLength) : base(prototype)
    {
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        Storage = new ManagedArrayBufferStorage(new byte[byteLength]);
        RefreshByteLength();
    }

    private JsArrayBuffer(JsObject? prototype, byte[] bytes) : base(prototype)
    {
        Storage = new ManagedArrayBufferStorage(bytes);
        RefreshByteLength();
    }

    private JsArrayBuffer(JsObject? prototype, IJsArrayBufferStorage storage) : base(prototype)
    {
        Storage = storage;
        RefreshByteLength();
    }

    private IJsArrayBufferStorage Storage { get; set; }

    public int ByteLength => Storage.ByteLength;

    /// <summary>ES2024 resizable buffers — [[ArrayBufferMaxByteLength]]. Null
    /// for a fixed-length buffer.</summary>
    public int? MaxByteLength { get; internal set; }

    /// <summary>§25.1.3.5 DetachArrayBuffer — [[ArrayBufferData]] is null.
    /// Views over a detached buffer are out of bounds; operations that
    /// ValidateTypedArray throw TypeError.</summary>
    public bool IsDetached { get; private set; }

    public void Detach()
    {
        Storage = new ManagedArrayBufferStorage(Array.Empty<byte>());
        IsDetached = true;
        RefreshByteLength();
    }

    public bool IsResizable => MaxByteLength.HasValue;

    /// <summary>§25.1.5.5 ArrayBuffer.prototype.resize — reallocate to
    /// <paramref name="newByteLength"/>, preserving the common prefix and
    /// zero-filling growth. Caller validates resizability and bounds.</summary>
    public void Resize(int newByteLength)
    {
        var fresh = new byte[newByteLength];
        var old = Storage.GetSpan();
        old[..Math.Min(old.Length, newByteLength)].CopyTo(fresh);
        Storage = new ManagedArrayBufferStorage(fresh);
        RefreshByteLength();
    }

    public Span<byte> GetSpan() => Storage.GetSpan();

    public Span<byte> GetSpan(int start) => Storage.GetSpan()[start..];

    public Span<byte> GetSpan(int start, int length) => Storage.GetSpan().Slice(start, length);

    public static JsArrayBuffer Wrap(JsObject? prototype, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new JsArrayBuffer(prototype, bytes);
    }

    public static JsArrayBuffer Wrap(JsObject? prototype, IJsArrayBufferStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        return new JsArrayBuffer(prototype, storage);
    }

    public void ReplaceBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        Storage = new ManagedArrayBufferStorage(bytes);
        RefreshByteLength();
    }

    public void RefreshByteLength()
    {
        DefineOwnProperty("byteLength",
            PropertyDescriptor.Data(JsValue.Number(ByteLength), writable: false, enumerable: false, configurable: true));
    }

    public JsArrayBuffer Slice(JsObject? prototype, int begin, int end)
    {
        var len = Math.Max(end - begin, 0);
        var copy = new byte[len];
        GetSpan(begin, len).CopyTo(copy);
        return new JsArrayBuffer(prototype, copy);
    }

    public override string ToString() => "[object ArrayBuffer]";
}

public interface IJsArrayBufferStorage
{
    int ByteLength { get; }

    Span<byte> GetSpan();
}

internal sealed class ManagedArrayBufferStorage : IJsArrayBufferStorage
{
    private readonly byte[] _bytes;

    public ManagedArrayBufferStorage(byte[] bytes)
        => _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));

    public int ByteLength => _bytes.Length;

    public Span<byte> GetSpan() => _bytes;
}
