using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tessera.Common.Diagnostics;
using Tessera.Dom;
using Tessera.Layout.Tree;
using Tessera.Net;
using Tessera.Url;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Engine;

/// <summary>
/// Resolves every <c>&lt;img src&gt;</c> in a <see cref="Document"/> to a
/// decoded <see cref="Image{Rgba32}"/> and exposes the results as an
/// <see cref="IImageResolver"/> for layout to query. Owns the decoded
/// bitmaps and disposes them when the fetcher itself is disposed.
/// </summary>
/// <remarks>
/// Caches per absolute URL so an image referenced N times decodes once. The
/// fetcher is fail-soft: a network or decode failure leaves the element
/// without a resolved image, which BoxTreeBuilder degrades to its
/// <c>alt</c> text.
/// </remarks>
internal sealed class ImageFetcher : IImageResolver, IDisposable
{
    private readonly Dictionary<Element, ResolvedImage> _byElement = [];
    private readonly Dictionary<string, Image<Rgba32>> _byUrl = new(StringComparer.Ordinal);
    private readonly IDiagnostics _diag;
    private readonly Func<TesseraHttpClient> _httpFactory;
    private TesseraHttpClient? _sharedHttp;

    public ImageFetcher(IDiagnostics diag, Func<TesseraHttpClient> httpFactory)
    {
        _diag = diag;
        _httpFactory = httpFactory;
    }

    public bool TryResolve(Element element, out ResolvedImage image)
        => _byElement.TryGetValue(element, out image);

    public async Task FetchAllAsync(Document document, TesseraUrl? baseUrl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var img in document.GetElementsByTagName("img"))
        {
            ct.ThrowIfCancellationRequested();

            var src = img.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(src)) continue;

            var absolute = ResolveAbsolute(src, baseUrl);
            if (absolute is null)
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Could not resolve <img src='{src}'>");
                continue;
            }

            var bitmap = await FetchAndDecodeAsync(absolute, ct).ConfigureAwait(false);
            if (bitmap is null) continue;

            _byElement[img] = new ResolvedImage(bitmap.Width, bitmap.Height, bitmap);
        }
    }

    private async Task<Image<Rgba32>?> FetchAndDecodeAsync(TesseraUrl url, CancellationToken ct)
    {
        var key = url.ToString();
        if (_byUrl.TryGetValue(key, out var cached)) return cached;

        try
        {
            byte[] bytes;
            if (url.IsFile)
            {
                var path = url.ToFileSystemPath();
                if (!File.Exists(path))
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Missing local image: {path}");
                    return null;
                }
                bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            }
            else if (url.IsHttp || url.IsHttps)
            {
                _sharedHttp ??= _httpFactory();
                var response = await _sharedHttp.GetAsync(url, ct).ConfigureAwait(false);
                if (response.IsErr)
                {
                    _diag.Log(DiagLevel.Warn, "engine", $"Image fetch failed {url}: {response.Error}");
                    return null;
                }
                if (response.Value.StatusCode is < 200 or >= 400)
                {
                    _diag.Log(DiagLevel.Warn, "engine",
                        $"Image fetch HTTP {response.Value.StatusCode} from {url}");
                    return null;
                }
                bytes = response.Value.Body.ToArray();
            }
            else
            {
                _diag.Log(DiagLevel.Warn, "engine", $"Unsupported image scheme '{url.Scheme}' for {url}");
                return null;
            }

            // ImageSharp auto-detects PNG/JPEG/GIF/BMP/WebP. CloneAs<Rgba32>
            // normalizes to the painter's pixel format and lets us dispose the
            // intermediate Image without surprising the caller.
            using var loaded = Image.Load(bytes);
            var bitmap = loaded.CloneAs<Rgba32>();
            _byUrl[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or SixLabors.ImageSharp.UnknownImageFormatException
                                   or SixLabors.ImageSharp.InvalidImageContentException)
        {
            _diag.Log(DiagLevel.Warn, "engine", $"Image decode failed {url}: {ex.Message}");
            return null;
        }
    }

    private static TesseraUrl? ResolveAbsolute(string src, TesseraUrl? baseUrl)
    {
        var parsed = baseUrl is null
            ? UrlParser.Parse(src)
            : UrlParser.Parse(src, baseUrl);
        return parsed.IsOk ? parsed.Value : null;
    }

    public void Dispose()
    {
        foreach (var bitmap in _byUrl.Values)
            bitmap.Dispose();
        _byUrl.Clear();
        _byElement.Clear();
        _sharedHttp?.Dispose();
        _sharedHttp = null;
    }
}
