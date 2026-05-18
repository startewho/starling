#if TESSERA_IMAGESHARP_DRAWING
using SixLabors.Fonts;
using Tessera.Layout.Text;

namespace Tessera.Paint;

/// <summary>
/// Shared font lookup for the ImageSharp backend and text measurer. Walks the
/// CSS <c>font-family</c> list (expanding generic keywords) against a font
/// collection that bundles both the embedded fonts in <c>Starling.Paint.dll</c>
/// and the host's installed system fonts. Mirrors how <see cref="FontResolver"/>
/// finds typefaces for Skia, so paragraph widths and bold/italic faces match
/// across the two backends.
/// </summary>
internal static class ImageSharpFontLookup
{
    /// <summary>
    /// Loads every embedded TTF/OTF resource from <c>Starling.Paint.dll</c> and
    /// every system-installed family into a single <see cref="FontCollection"/>.
    /// Both are needed: the embedded OpenSans is the deterministic fallback,
    /// and the system collection supplies real Helvetica/Times/Arial faces
    /// (with their bold/italic variants) so author CSS like
    /// <c>font-family: "Helvetica Neue", Helvetica, Arial, sans-serif</c>
    /// resolves to the same face Skia's CoreText path returns.
    /// </summary>
    public static FontCollection LoadCollection()
    {
        var collection = new FontCollection();
        AddEmbeddedFonts(collection);
        TryAddSystemFonts(collection);
        return collection;
    }

    /// <summary>
    /// Resolves <paramref name="spec"/> against <paramref name="collection"/>,
    /// returning a <see cref="Font"/> at the requested <paramref name="size"/>.
    /// Walks the family list, expanding CSS generics (<c>sans-serif</c>,
    /// <c>serif</c>, etc.) the same way <see cref="FontResolver"/> does, then
    /// falls back to the first family in the collection (always the bundled
    /// OpenSans). Returns the requested <see cref="FontStyle"/> only if the
    /// matched family carries that face — callers should not assume
    /// synthesised bold/italic.
    /// </summary>
    public static Font CreateFont(FontCollection collection, FontSpec spec, float size)
    {
        var style = (spec.Bold, spec.Italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            _ => FontStyle.Regular,
        };

        foreach (var candidate in EnumerateCandidates(spec.Families))
        {
            if (collection.TryGet(candidate, out var family) && HasStyle(family, style))
                return family.CreateFont(size, style);
        }

        // Second pass — accept any family-name match even if the requested
        // style isn't present, so we at least render in the right family
        // (matching SixLabors.Fonts' regular face).
        foreach (var candidate in EnumerateCandidates(spec.Families))
        {
            if (collection.TryGet(candidate, out var family))
                return family.CreateFont(size, FontStyle.Regular);
        }

        // Last-resort fallback: the first registered family (the bundled
        // OpenSans on every supported platform).
        foreach (var family in collection.Families)
            return family.CreateFont(size, style);

        throw new InvalidOperationException(
            "ImageSharpFontLookup: no fonts available. Ensure Starling.Paint.dll bundles at least one TTF/OTF embedded resource.");
    }

    private static bool HasStyle(FontFamily family, FontStyle style)
    {
        var styles = family.GetAvailableStyles().Span;
        for (var i = 0; i < styles.Length; i++)
            if (styles[i] == style) return true;
        return false;
    }

    private static IEnumerable<string> EnumerateCandidates(IReadOnlyList<string> families)
    {
        foreach (var family in families)
        {
            yield return family;
            foreach (var sub in ExpandGeneric(family))
                yield return sub;
        }
    }

    /// <summary>
    /// CSS generic-family expansions, mirroring <see cref="FontResolver.ExpandGeneric"/>.
    /// macOS names come first because osx-arm64 is the only shipped RID today.
    /// </summary>
    private static IEnumerable<string> ExpandGeneric(string family) => family.ToLowerInvariant() switch
    {
        "serif" => ["Times New Roman", "Times", "Georgia", "Liberation Serif", "DejaVu Serif", "Noto Serif"],
        "sans-serif" => ["Helvetica Neue", "Helvetica", "Arial", "Inter", "Liberation Sans", "DejaVu Sans", "Segoe UI", "Noto Sans", "Verdana"],
        "monospace" => ["Menlo", "Monaco", "SF Mono", "Courier New", "Courier", "Liberation Mono", "DejaVu Sans Mono", "Consolas", "Noto Sans Mono"],
        "cursive" => ["Snell Roundhand", "Apple Chancery", "Comic Sans MS", "Brush Script MT"],
        "fantasy" => ["Papyrus", "Impact", "Herculanum"],
        "system-ui" or "ui-sans-serif" => ["-apple-system", "system-ui", "SF Pro Text", "Helvetica Neue", "Segoe UI", "Roboto"],
        "ui-serif" => ["-apple-system", "Times New Roman", "Times"],
        "ui-monospace" => ["Menlo", "SF Mono", "Consolas"],
        _ => [],
    };

    private static void AddEmbeddedFonts(FontCollection collection)
    {
        var asm = typeof(ImageSharpFontLookup).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                continue;
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            try { collection.Add(stream); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { /* skip malformed */ }
        }
    }

    private static void TryAddSystemFonts(FontCollection collection)
    {
        try
        {
            collection.AddSystemFonts();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // System fonts unavailable (sandbox, unsupported platform, etc.).
            // Bundled fonts still serve as the fallback.
        }
    }
}
#endif
