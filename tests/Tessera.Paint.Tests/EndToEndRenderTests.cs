using FluentAssertions;
using Tessera.Html;
using Xunit;
using LayoutSize = Tessera.Layout.Size;

namespace Tessera.Paint.Tests;

public sealed class EndToEndRenderTests
{
    [Fact]
    public void Rendering_a_paragraph_produces_an_image_of_requested_size()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse("<body><p>Hello, world.</p></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(640, 480));

        image.Width.Should().Be(640);
        image.Height.Should().Be(480);
    }

    [Fact]
    public void Rendered_paragraph_leaves_visible_pixels()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse("<body><p>visible</p></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(400, 200));

        BitmapPixels.CountNonWhite(image).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Background_color_fills_pixels()
    {
        var painter = new Painter();
        var document = HtmlParser.Parse(
            "<body><div style=\"background-color: #008000; height: 50px\"></div></body>");
        using var image = painter.RenderDocument(document, new LayoutSize(200, 100));

        BitmapPixels.CountExact(image, 0, 128, 0).Should().BeGreaterThan(100);
    }
}
