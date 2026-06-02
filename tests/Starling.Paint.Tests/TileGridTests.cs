using System.Collections.Concurrent;
using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Paint.Compositor;

namespace Starling.Paint.Tests;

/// <summary>
/// Unit tests for the per-layer tile LRU (wp:M12-05-tile-grid): content-hash
/// validation, LRU eviction under a byte budget, recency promotion, and byte
/// accounting.
/// </summary>
[TestClass]
public sealed class TileGridTests
{
    // 4×4 RGBA = 64 bytes per tile.
    private static RenderedBitmap Tile(byte fill = 0)
    {
        var px = new byte[4 * 4 * 4];
        Array.Fill(px, fill);
        return new RenderedBitmap(4, 4, px);
    }

    private static TileKey Key(int col, int row, long layer = 1) => new(layer, col, row, 2.0f);

    [TestMethod]
    public void Put_then_get_hits_and_counts()
    {
        var diag = new RecordingDiagnostics();
        var grid = new TileGrid(diag, maxBytes: 1_000_000);
        var k = Key(0, 0);

        grid.TryGetTile(k, contentHash: 7, out _).Should().BeFalse("nothing stored yet");
        grid.PutTile(k, contentHash: 7, Tile());

        grid.TryGetTile(k, contentHash: 7, out var got).Should().BeTrue();
        got.Should().NotBeNull();
        diag.CountOf("paint.tile.cache_hit").Should().Be(1);
        diag.CountOf("paint.tile.cache_miss").Should().Be(1);
        grid.Count.Should().Be(1);
        grid.Bytes.Should().Be(64);
    }

    [TestMethod]
    public void Stale_content_hash_is_a_miss()
    {
        var grid = new TileGrid(maxBytes: 1_000_000);
        var k = Key(0, 0);
        grid.PutTile(k, contentHash: 1, Tile());

        grid.TryGetTile(k, contentHash: 2, out _).Should().BeFalse("content changed → stale");
        // Overwriting in place keeps a single entry per position.
        grid.PutTile(k, contentHash: 2, Tile());
        grid.Count.Should().Be(1);
        grid.TryGetTile(k, contentHash: 2, out _).Should().BeTrue();
    }

    [TestMethod]
    public void Over_budget_evicts_lru_first_and_promotion_protects_mru()
    {
        var diag = new RecordingDiagnostics();
        // Budget holds 2 tiles (64 B each), not 3.
        var grid = new TileGrid(diag, maxBytes: 150);

        grid.PutTile(Key(0, 0), 1, Tile()); // A
        grid.PutTile(Key(1, 0), 1, Tile()); // B
        // Promote A so B becomes the least-recently-used.
        grid.TryGetTile(Key(0, 0), 1, out _).Should().BeTrue();
        grid.PutTile(Key(2, 0), 1, Tile()); // C → over budget → evicts LRU (B)

        grid.Count.Should().Be(2);
        diag.CountOf("paint.tile.evict").Should().BeGreaterThanOrEqualTo(1);
        grid.TryGetTile(Key(0, 0), 1, out _).Should().BeTrue("A was promoted, survives");
        grid.TryGetTile(Key(2, 0), 1, out _).Should().BeTrue("C is newest, survives");
        grid.TryGetTile(Key(1, 0), 1, out _).Should().BeFalse("B was LRU, evicted");
    }

    [TestMethod]
    public void Clear_drops_all_tiles()
    {
        var grid = new TileGrid(maxBytes: 1_000_000);
        grid.PutTile(Key(0, 0), 1, Tile());
        grid.PutTile(Key(1, 0), 1, Tile());
        grid.Bytes.Should().Be(128);

        grid.Clear();

        grid.Count.Should().Be(0);
        grid.Bytes.Should().Be(0);
        grid.TryGetTile(Key(0, 0), 1, out _).Should().BeFalse();
    }

    [TestMethod]
    public void Distinct_layers_and_positions_do_not_collide()
    {
        var grid = new TileGrid(maxBytes: 1_000_000);
        grid.PutTile(Key(0, 0, layer: 1), 1, Tile(10));
        grid.PutTile(Key(0, 0, layer: 2), 1, Tile(20));
        grid.PutTile(Key(0, 1, layer: 1), 1, Tile(30));

        grid.Count.Should().Be(3);
        grid.TryGetTile(Key(0, 0, layer: 1), 1, out _).Should().BeTrue();
        grid.TryGetTile(Key(0, 0, layer: 2), 1, out _).Should().BeTrue();
        grid.TryGetTile(Key(0, 1, layer: 1), 1, out _).Should().BeTrue();
    }

    private sealed class RecordingDiagnostics : IDiagnostics
    {
        private readonly ConcurrentDictionary<string, double> _counters = new();
        public double CountOf(string name) => _counters.TryGetValue(name, out var v) ? v : 0d;
        public void Counter(string name, double value) => _counters.AddOrUpdate(name, value, (_, c) => c + value);
        public void Gauge(string name, double value) { }
        public void Log(DiagLevel level, string area, string message) { }
        public IDisposable Span(string area, string operation) => new Noop();
        public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        public void LogException(string area, Exception ex, string? message = null) { }
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
