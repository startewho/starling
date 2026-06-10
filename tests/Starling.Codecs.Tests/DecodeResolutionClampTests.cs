using AwesomeAssertions;
using Starling.Common.Image;

namespace Starling.Codecs.Tests;

/// <summary>
/// Tests for the decode-resolution clamp: images whose longest side exceeds
/// <see cref="NativeImageDecoder.MaxDecodeDimension"/> decode into a
/// proportionally smaller pixel buffer while the <see cref="DecodedImage"/>
/// keeps the true intrinsic dimensions for layout.
/// </summary>
[TestClass]
public sealed class DecodeResolutionClampTests
{
    // --- ClampDecodeTarget math --------------------------------------------

    [TestMethod]
    public void SmallImagesAreNotClamped()
    {
        NativeImageDecoder.ClampDecodeTarget(800, 600).Should().Be((800, 600));
        NativeImageDecoder.ClampDecodeTarget(2048, 2048).Should().Be((2048, 2048));
        NativeImageDecoder.ClampDecodeTarget(1, 1).Should().Be((1, 1));
    }

    [TestMethod]
    public void LongestSideClampsToTheCapPreservingAspect()
    {
        var (w, h) = NativeImageDecoder.ClampDecodeTarget(4096, 2048);
        w.Should().Be(2048);
        h.Should().Be(1024);

        (w, h) = NativeImageDecoder.ClampDecodeTarget(3000, 3000);
        w.Should().Be(2048);
        h.Should().Be(2048);

        // Portrait orientation clamps the height.
        (w, h) = NativeImageDecoder.ClampDecodeTarget(1000, 8192);
        h.Should().Be(2048);
        w.Should().Be(250);
    }

    [TestMethod]
    public void ExtremeAspectRatiosNeverClampToZero()
    {
        var (w, h) = NativeImageDecoder.ClampDecodeTarget(100_000, 2);
        w.Should().Be(2048);
        h.Should().Be(1);
    }

    // --- ClampToDecodeCap (portable downscale fallback) --------------------

    [TestMethod]
    public void WithinCapImagePassesThroughUntouched()
    {
        var img = DecodedImage.FromBuffer(4, 4, new byte[4 * 4 * 4]);
        NativeImageDecoder.ClampToDecodeCap(img).Should().BeSameAs(img);
        img.Dispose();
    }

    [TestMethod]
    public void OverCapImageDownscalesAndKeepsIntrinsicDimensions()
    {
        // 4000x2 solid opaque red. The box filter must preserve a solid fill
        // exactly through any shrink.
        const int srcW = 4000, srcH = 2;
        var buffer = new byte[srcW * srcH * 4];
        for (int i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = 200;     // R
            buffer[i + 1] = 10;  // G
            buffer[i + 2] = 30;  // B
            buffer[i + 3] = 255; // A
        }

        using var clamped = NativeImageDecoder.ClampToDecodeCap(
            DecodedImage.FromBuffer(srcW, srcH, buffer));

        clamped.Width.Should().Be(2048);
        clamped.Height.Should().Be(1);
        clamped.IntrinsicWidth.Should().Be(srcW);
        clamped.IntrinsicHeight.Should().Be(srcH);
        clamped.Pixels.Length.Should().Be(2048 * 1 * 4);

        var px = clamped.Pixels.Span;
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i].Should().Be(200);
            px[i + 1].Should().Be(10);
            px[i + 2].Should().Be(30);
            px[i + 3].Should().Be(255);
        }
    }

    [TestMethod]
    public void DownscaleDoesNotBleedColorFromTransparentTexels()
    {
        // Alternate fully-transparent green and opaque red columns. With
        // alpha-weighted (premultiplied) averaging the result must stay pure
        // red at half alpha — naive averaging would bleed green in.
        const int srcW = 4096, srcH = 1;
        var buffer = new byte[srcW * srcH * 4];
        for (int x = 0; x < srcW; x++)
        {
            int i = x * 4;
            if (x % 2 == 0)
            {
                buffer[i] = 255; buffer[i + 3] = 255; // opaque red
            }
            else
            {
                buffer[i + 1] = 255; buffer[i + 3] = 0; // transparent green
            }
        }

        using var clamped = NativeImageDecoder.ClampToDecodeCap(
            DecodedImage.FromBuffer(srcW, srcH, buffer));

        clamped.Width.Should().Be(2048);
        var px = clamped.Pixels.Span;
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i].Should().Be(255, "red must survive the average");
            px[i + 1].Should().Be(0, "transparent green must not bleed in");
            ((int)px[i + 3]).Should().BeInRange(127, 128);
        }
    }

    // --- end to end through the OS-native decoder --------------------------

    [TestMethod]
    public void NativeDecodeClampsOversizedImages()
    {
        // Synthesize a 3000x16 24-bit BMP (trivially encodable without any
        // compression) in a solid color and decode it through the OS codec.
        const int w = 3000, h = 16;
        var bmp = MakeSolidBmp24(w, h, r: 40, g: 90, b: 200);

        using var img = NativeImageDecoder.Decode(bmp);

        img.IntrinsicWidth.Should().Be(w);
        img.IntrinsicHeight.Should().Be(h);
        img.Width.Should().Be(2048);
        img.Height.Should().Be(Math.Max(1, (int)Math.Round(h * 2048.0 / w)));
        img.Pixels.Length.Should().Be(img.Width * img.Height * 4);

        // Solid fill survives any high-quality downscale exactly (within
        // rounding in the native resampler).
        var px = img.Pixels.Span;
        int mid = ((img.Height / 2) * img.Width + img.Width / 2) * 4;
        ((int)px[mid]).Should().BeInRange(38, 42);
        ((int)px[mid + 1]).Should().BeInRange(88, 92);
        ((int)px[mid + 2]).Should().BeInRange(198, 202);
        px[mid + 3].Should().Be(255);
    }

    [TestMethod]
    public void NativeDecodeWithinCapKeepsFullResolution()
    {
        const int w = 64, h = 8;
        var bmp = MakeSolidBmp24(w, h, r: 1, g: 2, b: 3);

        using var img = NativeImageDecoder.Decode(bmp);

        img.Width.Should().Be(w);
        img.Height.Should().Be(h);
        img.IntrinsicWidth.Should().Be(w);
        img.IntrinsicHeight.Should().Be(h);
    }

    /// <summary>
    /// Builds an uncompressed bottom-up 24-bit BMP filled with one color.
    /// </summary>
    private static byte[] MakeSolidBmp24(int width, int height, byte r, byte g, byte b)
    {
        int rowBytes = width * 3;
        int rowPadded = (rowBytes + 3) & ~3;
        int pixelBytes = rowPadded * height;
        int fileSize = 14 + 40 + pixelBytes;
        var bmp = new byte[fileSize];

        // BITMAPFILEHEADER
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteI32(bmp, 2, fileSize);
        WriteI32(bmp, 10, 14 + 40); // pixel data offset

        // BITMAPINFOHEADER
        WriteI32(bmp, 14, 40);
        WriteI32(bmp, 18, width);
        WriteI32(bmp, 22, height); // positive = bottom-up
        bmp[26] = 1;               // planes
        bmp[28] = 24;              // bpp
        WriteI32(bmp, 34, pixelBytes);

        for (int y = 0; y < height; y++)
        {
            int row = 54 + y * rowPadded;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * 3;
                bmp[i] = b;
                bmp[i + 1] = g;
                bmp[i + 2] = r;
            }
        }
        return bmp;
    }

    private static void WriteI32(byte[] dst, int offset, int value)
    {
        dst[offset] = (byte)value;
        dst[offset + 1] = (byte)(value >> 8);
        dst[offset + 2] = (byte)(value >> 16);
        dst[offset + 3] = (byte)(value >> 24);
    }
}
