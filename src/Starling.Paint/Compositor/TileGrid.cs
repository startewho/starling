using Starling.Common.Image;
using Starling.Dom;
using Starling.Layout.Box;

namespace Starling.Paint.Compositor;

/// <summary>
/// Identity of one cached tile: the layer it belongs to, its grid position, and the
/// device scale it was rastered at. The slice content hash is NOT part of the key —
/// it is validated on lookup so a content change overwrites the tile in place
/// (bounding the cache to one entry per position, like <see cref="Cache.PictureCache"/>
/// validates its <c>pageVersion</c>) rather than leaking a stale entry per version.
/// </summary>
internal readonly record struct TileKey(long LayerId, int Col, int Row, float Scale);

/// <summary>
/// A session-scoped, byte-budgeted LRU of rasterized layer tiles. Each compositor layer is tiled into a grid of fixed-size
/// (<see cref="TileWidthDevice"/>×<see cref="TileHeightDevice"/> device px) tiles;
/// only tiles intersecting the viewport are ever rastered, so per-frame raster is
/// bounded by the viewport rather than the layer's full height, and — because every
/// tile is well under the wgpu max-texture-dimension — the oversize-texture class of
/// bug is structurally impossible. Tiles persist across frames keyed by
/// <see cref="TileKey"/>; a scroll re-blits cached tiles and only rasters the
/// newly-exposed row/column.
/// </summary>
/// <remarks>
/// Tile geometry is WebRender-style: wide (2048) to span a typical viewport in ~one
/// column, short (512) for fine vertical-scroll re-raster granularity. The grid is
/// per-layer because Starling's compositor layer already plays WebRender's "slice"
/// role. Recency is the linked-list order (front = most-recently-used); on a byte
/// overflow the least-recently-used (back) tiles are dropped first. This is the
/// CPU-side cache; the GPU keeps its own texture cache in <see cref="GpuBlendEngine"/>
/// keyed by each tile op's content hash, evicted independently by frame age.
/// </remarks>
internal sealed class TileGrid
{
    internal const int TileWidthDevice = 2048;
    internal const int TileHeightDevice = 512;

    private const long DefaultBudgetBytes = 256L * 1024 * 1024; // 256 MB

    private readonly long _maxBytes;
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new(); // First = MRU, Last = LRU
    private long _bytes;

    // Stable per-layer identity for the tile key. Keyed by the layer-root DOM element
    // (stable across the per-frame layer-tree rebuild, like the old LayerCacheStore),
    // so a layer's tiles persist across frames. Two distinct layers never collide even
    // if their slices hash identically. Cleared with the tiles on navigation.
    private readonly Dictionary<Element, long> _layerIds = new();
    private long _nextLayerId = 1;

    private sealed class Entry
    {
        public TileKey Key;
        public long ContentHash;
        public RenderedBitmap? Bitmap;
        public int Width;
        public int Height;
        public int Bytes;
    }

    internal readonly record struct ResidentTile(int Width, int Height);

    public TileGrid(long? maxBytes = null)
    {
        _maxBytes = maxBytes ?? ReadBudgetEnv();
    }

    // Configurable like STARLING_PAINT_BACKEND — there is no paint-options type, so an
    // env var keeps the seam consistent. A bad/absent value falls back to the default.
    private static long ReadBudgetEnv()
    {
        var raw = Environment.GetEnvironmentVariable("STARLING_TILE_BUDGET_BYTES");
        return long.TryParse(raw, out var v) && v > 0 ? v : DefaultBudgetBytes;
    }

    /// <summary>
    /// Stable id for <paramref name="layerBox"/>'s layer across frames, keyed by its
    /// DOM element. Returns 0 for a layer whose root box has no element (no stable
    /// cross-frame identity — its tiles simply don't reuse across frames, the same
    /// degradation the old per-call cache had). Called at layer-tree build time.
    /// </summary>
    public long LayerIdFor(Box layerBox)
    {
        if (layerBox.Element is not { } element)
            return 0;
        if (!_layerIds.TryGetValue(element, out var id))
        {
            id = _nextLayerId++;
            _layerIds[element] = id;
        }
        return id;
    }

    /// <summary>Current resident byte total — for tests/metrics.</summary>
    internal long Bytes => _bytes;

    /// <summary>Number of resident tiles — for tests.</summary>
    internal int Count => _map.Count;

    /// <summary>
    /// Serves the tile at <paramref name="key"/> when it is resident AND was rastered
    /// for <paramref name="contentHash"/>; promotes it to most-recently-used. A
    /// content-hash mismatch (stale tile) counts as a miss so the caller re-rasters
    /// and overwrites via <see cref="PutTile"/>.
    /// </summary>
    public bool TryGetTile(in TileKey key, long contentHash, out RenderedBitmap bitmap)
    {
        if (_map.TryGetValue(key, out var node)
            && node.Value.ContentHash == contentHash
            && node.Value.Bitmap is { } residentBitmap)
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            bitmap = residentBitmap;
            return true;
        }
        bitmap = null!;
        return false;
    }

    public bool TryGetResidentTile(in TileKey key, long contentHash, out ResidentTile tile)
    {
        if (_map.TryGetValue(key, out var node) && node.Value.ContentHash == contentHash)
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            tile = new ResidentTile(node.Value.Width, node.Value.Height);
            return true;
        }

        tile = default;
        return false;
    }

    /// <summary>
    /// Stores (or overwrites) the tile at <paramref name="key"/> as most-recently-used,
    /// then evicts least-recently-used tiles until the byte budget is satisfied.
    /// </summary>
    public void PutTile(in TileKey key, long contentHash, RenderedBitmap bitmap)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _bytes -= existing.Value.Bytes;
            _lru.Remove(existing);
            _map.Remove(key);
        }

        var bytes = bitmap.Rgba.Length;
        var node = _lru.AddFirst(new Entry
        {
            Key = key,
            ContentHash = contentHash,
            Bitmap = bitmap,
            Width = bitmap.Width,
            Height = bitmap.Height,
            Bytes = bytes,
        });
        _map[key] = node;
        _bytes += bytes;

        EvictToBudget();
    }

    public void PutResidentTile(in TileKey key, long contentHash, int width, int height)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _bytes -= existing.Value.Bytes;
            _lru.Remove(existing);
            _map.Remove(key);
        }

        var bytes = checked(width * height * 4);
        var node = _lru.AddFirst(new Entry
        {
            Key = key,
            ContentHash = contentHash,
            Width = width,
            Height = height,
            Bytes = bytes,
        });
        _map[key] = node;
        _bytes += bytes;

        EvictToBudget();
    }

    private void EvictToBudget()
    {
        // Never evict below the just-inserted MRU tile: the visible set for one frame
        // is a few tens of MB, far under any sane budget, so this only ever drops
        // off-screen tiles scrolled away on earlier frames.
        while (_bytes > _maxBytes && _lru.Count > 1 && _lru.Last is { } last)
        {
            _bytes -= last.Value.Bytes;
            _map.Remove(last.Value.Key);
            _lru.RemoveLast();
        }
    }

    /// <summary>Drops every tile and layer-id mapping — called on navigation to a new
    /// document (also releases the retained element references in the id map).</summary>
    public void Clear()
    {
        _map.Clear();
        _lru.Clear();
        _bytes = 0;
        _layerIds.Clear();
    }
}
