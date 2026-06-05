using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Starling.Css;
using Starling.Css.Parser;
using StarlingUrl = Starling.Url.Url;

namespace Starling.Engine.Tests;

/// <summary>
/// The background prefetch pass also has to prefetch <c>mask-image</c> URLs (and
/// the <c>url()</c>s behind custom properties a <c>mask-image: var(--x)</c>
/// reads), or <see cref="ImageFetcher.TryResolveUrl"/> misses at paint time and
/// the mask silently does not apply — the exact reason angular.dev's masked glow
/// rendered as a solid blob.
/// </summary>
[TestClass]
public sealed class MaskImagePrefetchTests
{
    // A data: URI that decodes through the managed SVG decoder (no network).
    private static string SvgDataUri()
    {
        const string svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='4' height='4'>" +
            "<rect width='4' height='4' fill='black'/></svg>";
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    private static ImageFetcher NewFetcher()
        => new(NullLoggerFactory.Instance,
            () => throw new InvalidOperationException("data: URIs must not hit the network"));

    private static async Task PrefetchAsync(ImageFetcher fetcher, string css)
    {
        var sheet = CssParser.ParseStyleSheet(css, StyleOrigin.Author);
        await fetcher.FetchBackgroundsAsync(
            [(sheet, (StarlingUrl?)null)], documentBaseUrl: null, CancellationToken.None);
    }

    [TestMethod]
    public async Task Mask_image_url_is_prefetched_and_resolvable()
    {
        var uri = SvgDataUri();
        using var fetcher = NewFetcher();
        await PrefetchAsync(fetcher, $".x {{ mask-image: url(\"{uri}\"); }}");

        fetcher.TryResolveUrl(uri, out var image).Should().BeTrue(
            "a mask-image url() must be prefetched so paint can resolve it");
        image.Width.Should().Be(4);
    }

    [TestMethod]
    public async Task Webkit_mask_image_url_is_prefetched()
    {
        var uri = SvgDataUri();
        using var fetcher = NewFetcher();
        await PrefetchAsync(fetcher, $".x {{ -webkit-mask-image: url(\"{uri}\"); }}");

        fetcher.TryResolveUrl(uri, out _).Should().BeTrue();
    }

    [TestMethod]
    public async Task Custom_property_url_behind_var_mask_is_prefetched()
    {
        // The angular.dev shape: the url() lives on a custom property and the
        // mask-image only references it via var(). The raw url() is on --pattern,
        // so the prefetch must scan custom properties too.
        var uri = SvgDataUri();
        using var fetcher = NewFetcher();
        await PrefetchAsync(fetcher,
            $":root {{ --pattern: url(\"{uri}\"); }} .x {{ mask-image: var(--pattern); }}");

        fetcher.TryResolveUrl(uri, out _).Should().BeTrue(
            "a url() carried by a custom property (read via var() in mask-image) must be prefetched");
    }
}
