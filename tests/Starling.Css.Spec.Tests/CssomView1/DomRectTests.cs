using AwesomeAssertions;
using Starling.Css.CssomView;

namespace Starling.Css.Spec.Tests.CssomView1;

/// <summary>
/// Geometry conformance for
/// <see href="https://drafts.csswg.org/cssom-view/">CSSOM View Module Level 1</see>
/// — <c>DOMRect</c> and <c>DOMRectReadOnly</c> interfaces.
/// </summary>
[TestClass]
[Spec("cssom-view", "https://drafts.csswg.org/cssom-view/")]
[SpecImplementedCategory]
public sealed class DomRectTests
{
    // ------------------------------------------------------------------
    //  DomRectReadOnly — positive dimensions
    // ------------------------------------------------------------------

    /// <summary>
    /// §DOMRectReadOnly: for positive width/height, Top == Y, Left == X,
    /// Right == X + Width, Bottom == Y + Height.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-top")]
    [SpecFact]
    public void ReadOnly_PositiveDims_TopLeftRightBottom_DeriveCorrectly()
    {
        var r = new DomRectReadOnly(x: 10, y: 20, width: 100, height: 50);

        r.X.Should().Be(10);
        r.Y.Should().Be(20);
        r.Width.Should().Be(100);
        r.Height.Should().Be(50);

        r.Top.Should().Be(20);
        r.Left.Should().Be(10);
        r.Right.Should().Be(110);
        r.Bottom.Should().Be(70);
    }

    /// <summary>
    /// §DOMRectReadOnly: a zero-size rect placed at the origin has all edges at 0.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-top")]
    [SpecFact]
    public void ReadOnly_ZeroRect_AllEdgesZero()
    {
        var r = new DomRectReadOnly(0, 0, 0, 0);

        r.Top.Should().Be(0);
        r.Left.Should().Be(0);
        r.Right.Should().Be(0);
        r.Bottom.Should().Be(0);
    }

    // ------------------------------------------------------------------
    //  DomRectReadOnly — negative dimensions (spec flip behaviour)
    // ------------------------------------------------------------------

    /// <summary>
    /// §DOMRectReadOnly: negative width flips Left and Right.
    /// Left = min(x, x+width) and Right = max(x, x+width).
    /// With x=100, width=-40: Left=60, Right=100.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-left")]
    [SpecFact]
    public void ReadOnly_NegativeWidth_FlipsLeftAndRight()
    {
        var r = new DomRectReadOnly(x: 100, y: 0, width: -40, height: 10);

        r.Left.Should().Be(60);   // min(100, 60)
        r.Right.Should().Be(100); // max(100, 60)
        // Unaffected edges.
        r.Top.Should().Be(0);
        r.Bottom.Should().Be(10);
    }

    /// <summary>
    /// §DOMRectReadOnly: negative height flips Top and Bottom.
    /// Top = min(y, y+height) and Bottom = max(y, y+height).
    /// With y=80, height=-30: Top=50, Bottom=80.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-top")]
    [SpecFact]
    public void ReadOnly_NegativeHeight_FlipsTopAndBottom()
    {
        var r = new DomRectReadOnly(x: 0, y: 80, width: 20, height: -30);

        r.Top.Should().Be(50);    // min(80, 50)
        r.Bottom.Should().Be(80); // max(80, 50)
        // Unaffected edges.
        r.Left.Should().Be(0);
        r.Right.Should().Be(20);
    }

    /// <summary>
    /// §DOMRectReadOnly: both width and height negative — all four edges flip.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-top")]
    [SpecFact]
    public void ReadOnly_BothDimsNegative_AllEdgesFlip()
    {
        var r = new DomRectReadOnly(x: 50, y: 100, width: -20, height: -40);

        r.Left.Should().Be(30);    // min(50, 30)
        r.Right.Should().Be(50);   // max(50, 30)
        r.Top.Should().Be(60);     // min(100, 60)
        r.Bottom.Should().Be(100); // max(100, 60)
    }

    // ------------------------------------------------------------------
    //  DomRect (mutable)
    // ------------------------------------------------------------------

    /// <summary>
    /// §DOMRect: settable X/Y/Width/Height; derived edges recompute
    /// after mutation.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#domrect")]
    [SpecFact]
    public void Mutable_SetProperties_EdgesRecompute()
    {
        var r = new DomRect(x: 5, y: 5, width: 50, height: 50);

        r.Right.Should().Be(55);
        r.Bottom.Should().Be(55);

        // Mutate and verify edges update.
        r.Width = 200;
        r.Height = 100;

        r.Right.Should().Be(205);
        r.Bottom.Should().Be(105);
    }

    /// <summary>
    /// §DOMRect: setting a negative width after construction flips Left/Right
    /// the same way DOMRectReadOnly does.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-left")]
    [SpecFact]
    public void Mutable_NegativeWidthAfterMutation_FlipsLeftRight()
    {
        var r = new DomRect(x: 100, y: 0, width: 50, height: 10);
        r.Width = -40;

        r.Left.Should().Be(60);
        r.Right.Should().Be(100);
    }

    /// <summary>
    /// §DOMRect: default constructor (no args) yields a zero rect.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#domrect")]
    [SpecFact]
    public void Mutable_DefaultConstructor_AllZero()
    {
        var r = new DomRect();

        r.X.Should().Be(0);
        r.Y.Should().Be(0);
        r.Width.Should().Be(0);
        r.Height.Should().Be(0);
        r.Top.Should().Be(0);
        r.Left.Should().Be(0);
        r.Right.Should().Be(0);
        r.Bottom.Should().Be(0);
    }

    // ------------------------------------------------------------------
    //  Cross-type round-trip
    // ------------------------------------------------------------------

    /// <summary>
    /// Round-trip: DomRectReadOnly.ToMutable() produces a DomRect with the same values.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#domrect")]
    [SpecFact]
    public void ReadOnly_ToMutable_PreservesValues()
    {
        var ro = new DomRectReadOnly(x: 3, y: 7, width: 99, height: -5);
        var m = ro.ToMutable();

        m.X.Should().Be(ro.X);
        m.Y.Should().Be(ro.Y);
        m.Width.Should().Be(ro.Width);
        m.Height.Should().Be(ro.Height);
        m.Top.Should().Be(ro.Top);
        m.Bottom.Should().Be(ro.Bottom);
    }

    /// <summary>
    /// Round-trip: DomRect.ToReadOnly() produces a DomRectReadOnly snapshot.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#domrect")]
    [SpecFact]
    public void Mutable_ToReadOnly_SnapshotIsIndependent()
    {
        var r = new DomRect(x: 1, y: 2, width: 10, height: 20);
        var snap = r.ToReadOnly();

        // Mutate the source.
        r.Width = 999;

        // Snapshot is unchanged.
        snap.Width.Should().Be(10);
        snap.Right.Should().Be(11);
    }

    // ------------------------------------------------------------------
    //  Fractional / floating-point values
    // ------------------------------------------------------------------

    /// <summary>
    /// §DOMRectReadOnly: fractional px values round-trip without loss and
    /// derived edges compute with full double precision.
    /// </summary>
    [Spec("cssom-view", "https://drafts.csswg.org/cssom-view/#dom-domrectreadonly-top")]
    [SpecFact]
    public void ReadOnly_FractionalValues_PrecisionPreserved()
    {
        var r = new DomRectReadOnly(x: 1.5, y: 2.25, width: 10.75, height: 5.5);

        r.Right.Should().Be(12.25);
        r.Bottom.Should().Be(7.75);
    }
}
