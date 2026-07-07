namespace Starling.Js.Runtime;

/// <summary>ECMA-262 §25.3 DataView exotic-slot object. Length tracking follows
/// the viewed resizable buffer when constructed without an explicit byteLength.</summary>
public sealed class JsDataView : JsObject
{
    public JsDataView(JsObject? prototype, JsArrayBuffer buffer, int byteOffset, int byteLength, bool lengthTracking) : base(prototype)
    {
        Buffer = buffer;
        ByteOffset = byteOffset;
        FixedByteLength = byteLength;
        IsLengthTracking = lengthTracking;
    }

    public JsArrayBuffer Buffer { get; }
    public int ByteOffset { get; }
    public bool IsLengthTracking { get; }

    /// <summary>[[ByteLength]] recorded at construction; meaningless when
    /// <see cref="IsLengthTracking"/>.</summary>
    public int FixedByteLength { get; }

    /// <summary>§25.3.1.2 GetViewByteLength — assumes the view is in bounds.</summary>
    public int ViewByteLength => IsLengthTracking ? Buffer.ByteLength - ByteOffset : FixedByteLength;

    /// <summary>§25.3.1.3 IsViewOutOfBounds.</summary>
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

            return !IsLengthTracking && ByteOffset + FixedByteLength > Buffer.ByteLength;
        }
    }

    public override string ToString() => "[object DataView]";
}
