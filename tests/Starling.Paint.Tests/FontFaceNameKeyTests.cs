using AwesomeAssertions;
using Starling.Css;
using Starling.Css.Cascade;
using Starling.Css.FontFace;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Layout.Text;

namespace Starling.Paint.Tests;

/// <summary>
/// Locks down <c>@font-face</c> family-name keying (tasks/SITE_STYLING_PLAN.md
/// Tier 2 item 4): CSS family matching is ASCII case-insensitive and
/// quote-insensitive, so a face registered under any quoting/casing/padding of
/// a name must be found by every other form, on both keying sides —
/// <see cref="FontFaceRegistry"/> registration and the
/// <see cref="ImageSharpFontLookup"/> resolve walk that shaping uses.
/// </summary>
[TestClass]
public sealed class FontFaceNameKeyTests
{
    private const float Size = 16f;

    // --- Registry keying: register one form, look up with the others ---

    [TestMethod]
    public void Double_quoted_registration_matches_bare_lookup()
    {
        using var registry = new FontFaceRegistry();
        registry.TryAdd("\"TwitterChirp\"", bold: false, italic: false, OpenSansBytes()).Should().BeTrue();

        registry.TryGet("TwitterChirp", bold: false, italic: false, probeCodepoint: null, out var bytes)
            .Should().BeTrue("quotes are CSS syntax, not part of the family name");
        bytes.Should().NotBeEmpty();
    }

    [TestMethod]
    public void Bare_registration_matches_single_quoted_cased_lookup()
    {
        using var registry = new FontFaceRegistry();
        registry.TryAdd("TwitterChirp", bold: false, italic: false, OpenSansBytes()).Should().BeTrue();

        registry.TryGet("'tWITTERcHIRP'", bold: false, italic: false, probeCodepoint: null, out _)
            .Should().BeTrue("family matching is ASCII case-insensitive and quote-insensitive");
    }

    [TestMethod]
    public void Padded_quoted_registration_matches_clean_lookup()
    {
        using var registry = new FontFaceRegistry();
        registry.TryAdd("  ' Inter Tight '  ", bold: false, italic: false, OpenSansBytes()).Should().BeTrue();

        registry.TryGet("inter tight", bold: false, italic: false, probeCodepoint: null, out _)
            .Should().BeTrue("surrounding whitespace inside or outside the quotes is not part of the name");
    }

    [TestMethod]
    public void Quote_only_family_is_rejected()
    {
        using var registry = new FontFaceRegistry();
        registry.TryAdd("''", bold: false, italic: false, OpenSansBytes())
            .Should().BeFalse("an empty family name cannot key anything");
    }

    // --- Resolve walk: the shaping-side lookup must hit the registered face ---

    /// <summary>
    /// x.com's stack: <c>font-family: TwitterChirp, -apple-system, sans-serif</c>
    /// with the face declared as <c>@font-face { font-family: 'TwitterChirp'; … }</c>.
    /// The registered face must win over every later fallback. Geist (bundled,
    /// never produced by the sans-serif expansion) proves the alias hit: any
    /// fallback would resolve to a system sans or Open Sans, never Geist.
    /// </summary>
    [TestMethod]
    public void Quoted_registration_resolves_via_unquoted_fallback_chain()
    {
        using var registry = new FontFaceRegistry();
        registry.TryAdd("'TwitterChirp'", bold: false, italic: false, GeistBytes()).Should().BeTrue();

        var collection = ImageSharpFontLookup.LoadCollection(registry);
        var spec = new FontSpec(["TwitterChirp", "-apple-system", "sans-serif"], bold: false, italic: false);

        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        font.Family.Name.Should().Contain("Geist",
            "the @font-face-registered family must be matched quote-insensitively before any fallback");
    }

    /// <summary>
    /// The family list is a fallback chain: a missing first family must not
    /// derail resolution — the registered web font is next and must win.
    /// </summary>
    [TestMethod]
    public void Fallback_chain_tries_each_family_in_order()
    {
        using var registry = new FontFaceRegistry();
        registry.TryAdd("TwitterChirp", bold: false, italic: false, GeistBytes()).Should().BeTrue();

        var collection = ImageSharpFontLookup.LoadCollection(registry);
        var spec = new FontSpec(["NoSuchFamily12345", "twitterchirp", "sans-serif"], bold: false, italic: false);

        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        font.Family.Name.Should().Contain("Geist",
            "resolution must skip the missing family and take the next (registered, case-variant) one");
    }

    /// <summary>
    /// Generics must keep working when the web font never registered: the
    /// chain falls through to sans-serif and resolves to a real face instead
    /// of the registered-but-absent name throwing or pinning everything to
    /// the terminal fallback.
    /// </summary>
    [TestMethod]
    public void Generic_fallback_still_resolves_without_the_web_font()
    {
        using var registry = new FontFaceRegistry();
        var collection = ImageSharpFontLookup.LoadCollection(registry);
        var spec = new FontSpec(["TwitterChirp", "sans-serif"], bold: false, italic: false);

        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        font.Family.Name.Should().NotContain("Geist",
            "nothing registered TwitterChirp, so it must fall through to the generic expansion");
        font.Family.Name.Should().NotBeNullOrEmpty();
    }

    // --- End to end: real @font-face rule + real cascade, both keying sides ---

    /// <summary>
    /// The full pipeline minus the network: a quoted <c>@font-face</c> rule
    /// parsed by <see cref="FontFaceParser"/> keys the registry (as
    /// <c>FontFaceFetcher.RegisterAsync</c> does), the unquoted use-side
    /// <c>font-family</c> flows through the real cascade into
    /// <see cref="FontSpec"/>, and the shaping-side lookup must connect the two.
    /// </summary>
    [TestMethod]
    public void AtFontFace_rule_to_shaping_lookup_round_trips_across_quote_forms()
    {
        var sheet = CssParser.ParseStyleSheet(
            "@font-face { font-family: 'TwitterChirp'; src: url(chirp.woff2) format('woff2'); }" +
            "body { font-family: TwitterChirp, -apple-system, sans-serif; }",
            StyleOrigin.Author);

        var rules = new List<FontFaceRule>(FontFaceParser.ParseAll(sheet));
        rules.Should().ContainSingle();
        rules[0].FamilyName.Should().Be("TwitterChirp", "the parser strips the quotes");

        using var registry = new FontFaceRegistry();
        registry.TryAdd(rules[0].FamilyName, rules[0].Bold, rules[0].Italic, GeistBytes(), rules[0].UnicodeRange)
            .Should().BeTrue();

        var engine = new StyleEngine();
        engine.AddStyleSheet(sheet);
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var spec = FontSpec.FromStyle(engine.Compute(body, context: null));
        spec.Families[0].Should().Be("TwitterChirp", "the cascade preserves the authored family name");

        var collection = ImageSharpFontLookup.LoadCollection(registry);
        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        font.Family.Name.Should().Contain("Geist",
            "the @font-face family and the cascade's font-family must meet at one registry key");
    }

    private static byte[] OpenSansBytes() => BundledFontBytes("OpenSans-Regular.ttf");

    private static byte[] GeistBytes() => BundledFontBytes("Geist-Variable.ttf");

    private static byte[] BundledFontBytes(string suffix)
    {
        var asm = typeof(ImageSharpFontLookup).Assembly;
        var res = asm.GetManifestResourceNames()
            .First(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        using var stream = asm.GetManifestResourceStream(res)!;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
