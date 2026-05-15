using Tessera.Skia.Handles;

namespace Tessera.Paint;

/// <summary>
/// Per-document registry of <c>@font-face</c>-declared typefaces. The
/// <c>FontFaceFetcher</c> in <c>Tessera.Engine</c> populates this once
/// stylesheets have landed and their <c>url()</c> sources are fetched and
/// loaded. <see cref="FontResolver"/> consults it ahead of <c>SkFontMgr</c>,
/// so a site's web fonts take precedence over a system family of the same
/// name (CSS Fonts 3 §5).
/// </summary>
/// <remarks>
/// Lookup is case-insensitive on the family name to match CSS family matching
/// (family names compare ASCII case-insensitively per CSS Fonts 3 §5.1). The
/// registry owns the typefaces it holds and disposes them on
/// <see cref="Dispose"/> at end-of-document.
/// </remarks>
public sealed class FontFaceRegistry : IDisposable
{
    private readonly Dictionary<string, List<RegisteredFace>> _byFamily =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SkTypeface> _owned = [];
    private bool _disposed;

    /// <summary>
    /// Loads <paramref name="ttfBytes"/> as a typeface and registers it for
    /// <paramref name="family"/>. The caller (<c>FontFaceFetcher</c>) hands us
    /// the raw bytes fetched from each <c>@font-face url()</c> source so it
    /// stays free of Skia interop. Returns <c>false</c> if Skia could not
    /// parse the bytes (corrupt/wrong-format file); fail-soft to match the
    /// stylesheet/image fetchers.
    /// </summary>
    public bool TryAdd(string family, bool bold, bool italic, ReadOnlySpan<byte> ttfBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(family);
        if (ttfBytes.Length == 0) return false;

        SkTypeface typeface;
        try
        {
            typeface = SkTypeface.FromData(ttfBytes);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        if (!_byFamily.TryGetValue(family, out var faces))
        {
            faces = [];
            _byFamily[family] = faces;
        }
        faces.Add(new RegisteredFace(bold, italic, typeface));
        _owned.Add(typeface);
        return true;
    }

    /// <summary>
    /// Looks up a face for <paramref name="family"/> matching the requested
    /// bold/italic flags. An exact match wins; otherwise the first registered
    /// face for the family is returned (the paint stack synthesises bold/
    /// italic when the requested style isn't loaded separately).
    /// </summary>
    internal bool TryGet(string family, bool bold, bool italic, out SkTypeface typeface)
    {
        if (_disposed || !_byFamily.TryGetValue(family, out var faces) || faces.Count == 0)
        {
            typeface = null!;
            return false;
        }

        foreach (var f in faces)
        {
            if (f.Bold == bold && f.Italic == italic)
            {
                typeface = f.Typeface;
                return true;
            }
        }

        typeface = faces[0].Typeface;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var t in _owned) t.Dispose();
        _owned.Clear();
        _byFamily.Clear();
    }

    private readonly record struct RegisteredFace(bool Bold, bool Italic, SkTypeface Typeface);
}
