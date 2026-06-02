using AwesomeAssertions;
using Starling.Css.Values;
using Starling.Paint.DisplayList;
using Rect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// The per-tile content hash (wp:M12-05 fix) is what stops a localized page
/// change from invalidating every tile. These pin the property the whole-layer
/// hash got wrong: a change confined to one region only changes the hash of the
/// tiles it overlaps, so the rest stay cache hits.
/// </summary>
[TestClass]
public sealed class DisplayListContentHashTests
{
    private static readonly Rect TileTop = new(0, 0, 200, 200);
    private static readonly Rect TileBottom = new(0, 1000, 200, 200);

    private static PaintList TwoRects(CssColor topColor, CssColor bottomColor)
    {
        var list = new PaintList();
        list.Add(new FillRect(new Rect(0, 0, 100, 100), topColor, FillRectPixelAlignment.Preserve));
        list.Add(new FillRect(new Rect(0, 1000, 100, 100), bottomColor, FillRectPixelAlignment.Preserve));
        return list;
    }

    [TestMethod]
    public void ChangeInOneRegion_LeavesTheOtherTilesHashUnchanged()
    {
        var red = new CssColor(255, 0, 0, 255);
        var blue = new CssColor(0, 0, 255, 255);

        var before = DisplayListContentHash.Prepare(TwoRects(red, red));
        var after = DisplayListContentHash.Prepare(TwoRects(blue, red)); // only the TOP rect changed

        // The top tile's hash moves (its content changed)...
        after.HashForTile(TileTop).Should().NotBe(before.HashForTile(TileTop));
        // ...but the bottom tile, untouched by the change, hashes identically and
        // therefore stays a cache hit. The old whole-layer hash failed this.
        after.HashForTile(TileBottom).Should().Be(before.HashForTile(TileBottom));
    }

    [TestMethod]
    public void IdenticalSlices_ProduceIdenticalPerTileHashes()
    {
        var red = new CssColor(255, 0, 0, 255);
        var a = DisplayListContentHash.Prepare(TwoRects(red, red));
        var b = DisplayListContentHash.Prepare(TwoRects(red, red));

        a.HashForTile(TileTop).Should().Be(b.HashForTile(TileTop));
        a.HashForTile(TileBottom).Should().Be(b.HashForTile(TileBottom));
    }

    [TestMethod]
    public void ItemOutsideTile_DoesNotContributeToItsHash()
    {
        var red = new CssColor(255, 0, 0, 255);
        // A list with ONLY the bottom rect vs. both rects: the top tile sees only
        // the top rect, so its hash must match a list that has just the top rect.
        var both = DisplayListContentHash.Prepare(TwoRects(red, red));
        var topOnly = new PaintList();
        topOnly.Add(new FillRect(new Rect(0, 0, 100, 100), red, FillRectPixelAlignment.Preserve));
        var prepared = DisplayListContentHash.Prepare(topOnly);

        prepared.HashForTile(TileTop).Should().Be(both.HashForTile(TileTop),
            "the bottom rect is outside the top tile and must not affect its hash");
    }

    [TestMethod]
    public void TextChange_IsDetectedInTheDescenderTile_BelowTheNominalBox()
    {
        var red = new CssColor(255, 0, 0, 255);
        string[] family = ["sans-serif"];
        PaintList WithText(string text)
        {
            var l = new PaintList();
            l.Add(new DrawText(text, 0, 100, 20, red, family, Bold: false, Italic: false));
            return l;
        }

        // The glyph baseline sits well below DrawText's (X, Y=line-box-top) box;
        // a tile at y∈[110,114] is past the nominal box bottom (~106) but within
        // the painted descender band. Editing the text must re-hash it — without
        // the per-item descender margin this tile would be a stale hit.
        var descenderTile = new Rect(0, 110, 80, 4);
        var a = DisplayListContentHash.Prepare(WithText("Apygj"));
        var b = DisplayListContentHash.Prepare(WithText("Bxqip"));

        b.HashForTile(descenderTile).Should().NotBe(a.HashForTile(descenderTile),
            "painted glyphs reach this tile, so a text change must invalidate it");
    }

    [TestMethod]
    public void BracketItems_FoldIntoEveryTile_Conservatively()
    {
        // A transform bracket must invalidate every tile (it can move any enclosed
        // content) — the safe, conservative behavior that prevents a stale hit.
        var red = new CssColor(255, 0, 0, 255);
        var plain = DisplayListContentHash.Prepare(TwoRects(red, red));

        var withTransform = new PaintList();
        withTransform.Add(new PushTransform(Matrix2D.Translate(5, 0)));
        withTransform.Add(new FillRect(new Rect(0, 0, 100, 100), red, FillRectPixelAlignment.Preserve));
        withTransform.Add(new PopTransform());
        withTransform.Add(new FillRect(new Rect(0, 1000, 100, 100), red, FillRectPixelAlignment.Preserve));
        var bracketed = DisplayListContentHash.Prepare(withTransform);

        // Even the bottom tile (which contains no transformed content) sees the
        // bracket folded in, so it differs from the bracket-free slice.
        bracketed.HashForTile(TileBottom).Should().NotBe(plain.HashForTile(TileBottom));
    }
}
