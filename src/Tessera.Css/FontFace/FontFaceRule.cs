namespace Tessera.Css.FontFace;

/// <summary>
/// A parsed <c>@font-face</c> at-rule. The font-face fetcher resolves each
/// <see cref="Sources"/> entry — <see cref="LocalFontSource"/> via the system
/// font manager, <see cref="UrlFontSource"/> via the document loader — and
/// registers the first source that loads under <see cref="FamilyName"/>.
/// </summary>
public sealed record FontFaceRule(
    string FamilyName,
    IReadOnlyList<FontFaceSource> Sources,
    bool Bold,
    bool Italic);

/// <summary>A single entry in a <c>@font-face src:</c> list.</summary>
public abstract record FontFaceSource;

/// <summary>
/// <c>src: local(name)</c> — refer to a font installed on the system by face
/// name. Matched via the platform <c>SkFontMgr</c>; first hit wins.
/// </summary>
public sealed record LocalFontSource(string Name) : FontFaceSource;

/// <summary>
/// <c>src: url("file.woff2") format("woff2")</c> — refer to a font fetched
/// from a URL. <see cref="Format"/> is the optional <c>format()</c> hint;
/// the fetcher uses it to skip formats Skia can't parse (e.g. WOFF2 today)
/// before paying for the request.
/// </summary>
public sealed record UrlFontSource(string Url, string? Format) : FontFaceSource;
