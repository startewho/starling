#if TESSERA_IMAGESHARP_DRAWING
using FluentAssertions;
using SixLabors.Fonts;
using Tessera.Layout.Text;
using Xunit;

namespace Tessera.Paint.Tests;

/// <summary>
/// Locks down the regression where the ImageSharp backend held only the
/// bundled OpenSans-Regular in its <see cref="FontCollection"/>, so
/// <c>font-family: "Helvetica Neue", Helvetica, Arial, sans-serif</c> resolved
/// to OpenSans Regular even on Bold/Italic requests. <see cref="ImageSharpFontLookup"/>
/// now loads system fonts and expands CSS generics so Bold/Italic faces
/// resolve to real fonts on macOS (and other supported platforms).
/// </summary>
public sealed class ImageSharpFontLookupTests
{
    private const float Size = 16f;

    /// <summary>Bundled OpenSans-Regular guarantees the collection is never empty.</summary>
    [Fact]
    public void Collection_loads_at_least_the_bundled_family()
    {
        var collection = ImageSharpFontLookup.LoadCollection();
        var families = collection.Families.ToList();
        families.Should().NotBeEmpty(
            "the bundled OpenSans-Regular.ttf is embedded in Starling.Paint.dll and is the final fallback");
        families.Should().Contain(f => f.Name.Contains("Open Sans", StringComparison.OrdinalIgnoreCase),
            "OpenSans is the deterministic fallback face the engine ships with");
    }

    /// <summary>
    /// Skip on platforms where the host has no installed sans-serif (sandboxed
    /// CI without any system fonts). The probe consults
    /// <see cref="SystemFonts"/> directly so a regression that prevents the
    /// lookup from consulting system fonts fails the test rather than
    /// skipping it.
    /// </summary>
    [Fact]
    public void Generic_sans_serif_resolves_to_a_system_family_when_available()
    {
        Assert.SkipUnless(HostHasInstalledSansSerif(),
            "no major system sans-serif installed on this host; nothing to assert");

        var collection = ImageSharpFontLookup.LoadCollection();
        var spec = new FontSpec(["sans-serif"], bold: false, italic: false);
        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        // sans-serif must expand to a non-OpenSans system family when one is
        // present. Resolving to OpenSans on a host that has Helvetica/Arial
        // is the regression this test guards against.
        font.Family.Name.Should().NotContain("Open Sans",
            "sans-serif should expand to a system family on a host that has one — the regression collapsed it back to bundled OpenSans");
    }

    /// <summary>
    /// The regression: h1 elements rendered as OpenSans Regular because no
    /// Bold face was registered. Verifies that a Bold request lands on a
    /// real Bold face (whichever family resolves on this host).
    /// </summary>
    [Fact]
    public void Bold_request_resolves_to_a_bold_face_when_one_exists()
    {
        Assert.SkipUnless(HostHasInstalledFontStyle(FontStyle.Bold),
            "no system family with a Bold face on this host; can't verify Bold selection");

        var collection = ImageSharpFontLookup.LoadCollection();
        var spec = new FontSpec(["sans-serif"], bold: true, italic: false);
        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        font.IsBold.Should().BeTrue(
            "a CSS Bold spec must round-trip to a real Bold face — synthesised bold doesn't fix the regression");
    }

    /// <summary>Same regression, italic variant.</summary>
    [Fact]
    public void Italic_request_resolves_to_an_italic_face_when_one_exists()
    {
        Assert.SkipUnless(HostHasInstalledFontStyle(FontStyle.Italic),
            "no system family with an Italic face on this host; can't verify Italic selection");

        var collection = ImageSharpFontLookup.LoadCollection();
        var spec = new FontSpec(["sans-serif"], bold: false, italic: true);
        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        font.IsItalic.Should().BeTrue(
            "a CSS Italic spec must round-trip to a real Italic face");
    }

    /// <summary>
    /// Unknown family names are expected to fall through to the bundled
    /// OpenSans rather than throwing — keeps obscure or platform-specific
    /// families from blowing up the renderer.
    /// </summary>
    [Fact]
    public void Unknown_family_falls_back_to_bundled_open_sans()
    {
        var collection = ImageSharpFontLookup.LoadCollection();
        var spec = new FontSpec(["This Family Definitely Does Not Exist 12345"], bold: false, italic: false);

        var font = ImageSharpFontLookup.CreateFont(collection, spec, Size);

        font.Family.Name.Should().Contain("Open Sans",
            "the bundled OpenSans is the documented terminal fallback");
    }

    /// <summary>
    /// Two specs that differ only in <see cref="FontSpec.Bold"/> must produce
    /// fonts with different advance widths — proof that the lookup is
    /// actually pulling a different face, not synthesising weight via stroke
    /// width. This is the failure mode the regression bisects on: without the
    /// fix, both renders measured to the same width because both resolved to
    /// OpenSans Regular.
    /// </summary>
    [Fact]
    public void Bold_and_regular_measure_different_widths()
    {
        Assert.SkipUnless(HostHasInstalledFontStyle(FontStyle.Bold),
            "no system Bold face on this host; can't compare Regular vs Bold metrics");

        var collection = ImageSharpFontLookup.LoadCollection();
        var regularSpec = new FontSpec(["sans-serif"], bold: false, italic: false);
        var boldSpec = new FontSpec(["sans-serif"], bold: true, italic: false);

        const string Probe = "The quick brown fox jumps over the lazy dog";
        var regularFont = ImageSharpFontLookup.CreateFont(collection, regularSpec, Size);
        var boldFont = ImageSharpFontLookup.CreateFont(collection, boldSpec, Size);

        var regularWidth = TextMeasurer.MeasureAdvance(Probe, new TextOptions(regularFont)).Width;
        var boldWidth = TextMeasurer.MeasureAdvance(Probe, new TextOptions(boldFont)).Width;

        boldWidth.Should().NotBe(regularWidth,
            "bold and regular faces must produce different advance widths; identical means we fell back to the same Regular face");
    }

    /// <summary>
    /// Probes the host's installed font collection directly (independent of
    /// <see cref="ImageSharpFontLookup"/>), so a regression that ignores
    /// system fonts fails the dependent test rather than skipping it.
    /// </summary>
    private static bool HostHasInstalledSansSerif()
    {
        try
        {
            var sys = SystemFonts.Collection;
            return sys.TryGet("Helvetica Neue", out _)
                || sys.TryGet("Helvetica", out _)
                || sys.TryGet("Arial", out _)
                || sys.TryGet("Segoe UI", out _)
                || sys.TryGet("DejaVu Sans", out _)
                || sys.TryGet("Liberation Sans", out _)
                || sys.TryGet("Inter", out _);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static bool HostHasInstalledFontStyle(FontStyle style)
    {
        try
        {
            foreach (var family in SystemFonts.Collection.Families)
            {
                var styles = family.GetAvailableStyles().Span;
                for (var i = 0; i < styles.Length; i++)
                    if (styles[i] == style) return true;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // System fonts unavailable on this host.
        }
        return false;
    }
}
#endif
