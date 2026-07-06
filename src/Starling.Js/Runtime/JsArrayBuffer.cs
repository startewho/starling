namespace Starling.Js.Runtime;

/// <summary>ECMA-262 §25.1 ArrayBuffer object with pluggable [[ArrayBufferData]].</summary>
public sealed class JsArrayBuffer : JsObject
{
    private static readonly ManagedArrayBufferStorage DetachedStorage = new(System.Array.Empty<byte>());

    public JsArrayBuffer(JsObject? prototype, int byteLength) : base(prototype)
    {
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        Storage = new ManagedArrayBufferStorage(new byte[byteLength]);
        MaxByteLength = -1;
        RefreshByteLength();
    }

    public JsArrayBuffer(JsObject? prototype, int byteLength, int maxByteLength) : base(prototype)
    {
        if (byteLength < 0 || maxByteLength < byteLength)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        Storage = new ManagedArrayBufferStorage(new byte[byteLength]);
        MaxByteLength = maxByteLength;
        RefreshByteLength();
    }

    private JsArrayBuffer(JsObject? prototype, byte[] bytes) : base(prototype)
    {
        Storage = new ManagedArrayBufferStorage(bytes);
        MaxByteLength = -1;
        RefreshByteLength();
    }

    private JsArrayBuffer(JsObject? prototype, IJsArrayBufferStorage storage) : base(prototype)
    {
        Storage = storage;
        MaxByteLength = -1;
        RefreshByteLength();
    }

    private IJsArrayBufferStorage Storage { get; set; }

    public int ByteLength => Storage.ByteLength;

    /// <summary>[[ArrayBufferMaxByteLength]]; -1 for a fixed-length buffer.</summary>
    public int MaxByteLength { get; private set; }

    public bool IsResizable => MaxByteLength >= 0;

    public bool IsDetached { get; private set; }

    /// <summary>[[ArrayBufferIsImmutable]] (immutable ArrayBuffer proposal).</summary>
    public bool IsImmutable { get; private set; }

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

    /// <summary>§25.1.3.5 DetachArrayBuffer — zero the data block. Callers must
    /// reject immutable buffers first.</summary>
    public void Detach()
    {
        Storage = DetachedStorage;
        IsDetached = true;
        MaxByteLength = -1;
        RefreshByteLength();
    }

    /// <summary>§25.1.6.13 ArrayBuffer.prototype.resize step 6+ — the range and
    /// resizability checks belong to the caller.</summary>
    public void Resize(int newByteLength)
    {
        var next = new byte[newByteLength];
        var old = Storage.GetSpan();
        old[..Math.Min(old.Length, newByteLength)].CopyTo(next);
        Storage = new ManagedArrayBufferStorage(next);
        RefreshByteLength();
    }

    public void MarkImmutable()
    {
        IsImmutable = true;
        MaxByteLength = -1;
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
