using FluentAssertions;
using Tessera.Paint.Backend;
using Xunit;

namespace Tessera.Paint.Tests;

public sealed class PaintBackendSelectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_or_blank_defaults_to_skia(string? value)
        => PaintBackendSelector.Parse(value).Should().Be(PaintBackendKind.Skia);

    [Theory]
    [InlineData("skia", "Skia")]
    [InlineData("SKIA", "Skia")]
    [InlineData(" Skia ", "Skia")]
    [InlineData("imagesharp", "ImageSharp")]
    [InlineData("ImageSharp", "ImageSharp")]
    [InlineData(" IMAGESHARP ", "ImageSharp")]
    public void Recognised_values_parse_case_and_whitespace_insensitive(string value, string expectedName)
        => PaintBackendSelector.Parse(value).ToString().Should().Be(expectedName);

    [Theory]
    [InlineData("graphite")]
    [InlineData("vello")]
    [InlineData("skia2")]
    public void Unknown_values_throw_naming_the_env_var_and_allowed_values(string value)
    {
        var act = () => PaintBackendSelector.Parse(value);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*TESSERA_PAINT_BACKEND*")
            .WithMessage("*skia*")
            .WithMessage("*imagesharp*");
    }
}
