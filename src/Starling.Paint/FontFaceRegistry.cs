using Tessera.Css.FontFace;
using Tessera.Paint.WebFonts;

namespace Tessera.Paint;

/// <summary>
/// Per-document registry of <c>@font-face</c>-declared typefaces. The
/// <c>FontFaceFetcher</c> in <c>Tessera.Engine</c> populates this once
/// stylesheets have landed and their <c>url()</c> sources are fetched and
/// loaded.
/// <para>
/// The engine paints through ImageSharp.Drawing 3, which owns its own
/// font collection. <see cref="ImageSharpFontLookup.LoadCollection(FontFaceRegistry?)"/>
/// snapshots this registry at backend construction time, so any face
/// registered before <c>Painter.RenderDocument</c> participates in
/// font-family resolution alongside the bundled and system fonts.
/// </para>
/// </summary>
/// <remarks>
/// Lookup is case-insensitive on the family name to match CSS family matching
/// (family names compare ASCII case-insensitively per CSS Fonts 3 §5.1).
/// </remarks>
public sealed class FontFaceRegistry : IDisposable
{
    private readonly Dictionary<string, List<RegisteredFace>> _byFamily =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>
    /// Loads <paramref name="fontBytes"/> as a face and registers it for
    /// <paramref name="family"/>. Sniffs WOFF / WOFF2 magic bytes and unwraps
    /// them in-process so a font fetched from any of the common web-font
    /// formats lands here as raw SFNT bytes. <paramref name="unicodeRange"/>
    /// (optional) restricts which codepoints the face applies to; when null
    /// the face covers everything. Returns <c>false</c> if the bytes are
    /// unrecognisable or the WOFF2 container uses transforms we don't yet
    /// support; fail-soft to match the stylesheet/image fetchers.
    /// </summary>
    public bool TryAdd(
        string family,
        bool bold,
        bool italic,
        ReadOnlySpan<byte> fontBytes,
        UnicodeRangeSet? unicodeRange = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(family);
        if (fontBytes.Length == 0) return false;

        byte[] sfnt;
        try
        {
            if (Woff2Decoder.IsWoff2(fontBytes))
                sfnt = Woff2Decoder.Decode(fontBytes);
            else if (WoffDecoder.IsWoff(fontBytes))
                sfnt = WoffDecoder.Decode(fontBytes);
            else
                sfnt = fontBytes.ToArray();
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (Woff2UnsupportedTransformException)
        {
            return false;
        }

        if (!_byFamily.TryGetValue(family, out var faces))
        {
            faces = [];
            _byFamily[family] = faces;
        }
        faces.Add(new RegisteredFace(bold, italic, unicodeRange, sfnt));
        return true;
    }

    /// <summary>
    /// Enumerates every registered face as raw SFNT bytes. Used by the paint
    /// backend (<see cref="ImageSharpFontLookup.LoadCollection(FontFaceRegistry?)"/>)
    /// to fold web fonts into the per-render <c>FontCollection</c> snapshot.
    /// <para>
    /// The CSS family name from the <c>@font-face</c> rule is intentionally
    /// not returned alongside the bytes — SixLabors.Fonts identifies families
    /// from each SFNT's own <c>name</c> table on <c>FontCollection.Add</c>,
    /// not from an externally-supplied alias. In practice this works because
    /// authors keep the <c>@font-face</c> <c>font-family</c> descriptor in
    /// sync with the font's internal family name; a stylesheet that uses a
    /// renamed family will still register the bytes but only resolve by the
    /// SFNT's own name.
    /// </para>
    /// </summary>
    public IEnumerable<ReadOnlyMemory<byte>> EnumerateRegisteredSfnt()
    {
        if (_disposed) yield break;
        foreach (var faces in _byFamily.Values)
            foreach (var face in faces)
                yield return face.SfntBytes;
    }

    /// <summary>
    /// Returns the registered SFNT bytes for the given family / weight / style,
    /// honouring <c>unicode-range</c> when <paramref name="probeCodepoint"/>
    /// is provided. Exact bold/italic match wins; otherwise the first
    /// covering face wins. Used by tests that exercise the registry directly;
    /// the paint backend consumes the registry via
    /// <see cref="EnumerateRegisteredSfnt"/>.
    /// </summary>
    internal bool TryGet(string family, bool bold, bool italic, int? probeCodepoint, out byte[] sfntBytes)
    {
        if (_disposed || !_byFamily.TryGetValue(family, out var faces) || faces.Count == 0)
        {
            sfntBytes = [];
            return false;
        }

        RegisteredFace? firstCovering = null;
        foreach (var f in faces)
        {
            if (probeCodepoint is int cp && f.UnicodeRange is { } range && !range.Contains(cp))
                continue;
            firstCovering ??= f;
            if (f.Bold == bold && f.Italic == italic)
            {
                sfntBytes = f.SfntBytes;
                return true;
            }
        }

        if (firstCovering is { } any)
        {
            sfntBytes = any.SfntBytes;
            return true;
        }

        sfntBytes = [];
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _byFamily.Clear();
    }

    private readonly record struct RegisteredFace(
        bool Bold,
        bool Italic,
        UnicodeRangeSet? UnicodeRange,
        byte[] SfntBytes);
}
