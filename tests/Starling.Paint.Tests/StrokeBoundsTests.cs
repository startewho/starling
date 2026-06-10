using AwesomeAssertions;
using Starling.Css.Values;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint.Tests;

/// <summary>
/// Strokes draw centre-line on the rect path, so the painted ring overhangs
/// the item's Bounds by half the pen width on every side. The layer-bounds
/// union reads these AABBs to size compositor tiles — without the overhang,
/// an outline that defines a layer's extreme edge loses its outer half.
/// </summary>
[TestClass]
public sealed class StrokeBoundsTests
{
    [TestMethod]
    public void Rounded_stroke_bounds_include_the_pen_overhang()
    {
        var item = new StrokeRoundedRect(
            new LayoutRect(10, 10, 100, 50), CornerRadii.None, new CssColor(0, 0, 0, 255), Width: 4);

        DisplayItemBounds.TryGet(item, out var b).Should().BeTrue();
        b.X.Should().Be(8);
        b.Y.Should().Be(8);
        b.Width.Should().Be(104);
        b.Height.Should().Be(54);
    }

    [TestMethod]
    public void Square_stroke_bounds_include_the_pen_overhang()
    {
        var item = new StrokeRect(new LayoutRect(0, 0, 20, 20), new CssColor(0, 0, 0, 255), Width: 2);

        DisplayItemBounds.TryGet(item, out var b).Should().BeTrue();
        b.X.Should().Be(-1);
        b.Y.Should().Be(-1);
        b.Width.Should().Be(22);
        b.Height.Should().Be(22);
    }
}
