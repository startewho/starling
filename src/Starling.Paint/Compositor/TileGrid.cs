using Starling.Common.Image;
using Starling.Dom;
using Starling.Layout.Box;

namespace Starling.Paint.Compositor;

/// <summary>
/// Represents a unique identifier for a rasterized tile within a layer grid.
/// </summary>
/// <param name="LayerId">The unique identifier of the layer to which the tile belongs.</param>
/// <param name="Col">The column index of the tile in the grid.</param>
/// <param name="Row">The row index of the tile in the grid.</param>
/// <param name="Scale">The scale factor applied to the tile.</param>
internal readonly record struct TileKey(long LayerId, int Col, int Row, float Scale);

/// <summary>
/// A session-scoped, byte-budgeted LRU of rasterized layer tiles.
/// </summary>
internal sealed class TileGrid(long? maxBytes = null)
{
    internal const int TileWidthDevice = 2048;
    internal const int TileHeightDevice = 512;

    private readonly long _maxBytes = maxBytes ?? 256L * 1024 * 1024; // 256 MB
    private readonly Dictionary<TileKey, LinkedListNode<Entry>> _map = [];
    private readonly LinkedList<Entry> _lru = []; // First = MRU, Last = LRU
    private long _bytes;

    /// <summary>
    /// Stable per-layer identity for the tile key. Keyed by the layer-root DOM element.
    /// Cleared with the tiles on navigation.
    /// </summary>
    private readonly Dictionary<Element, long> _layerIds = new();

    private long _nextLayerId = 1;

    /// <summary>
    /// A dictionary that associates content hashes with their corresponding prepared slices,
    /// facilitating the reuse of processed display list data to optimize rendering performance.
    /// Cleared during a tile grid reset.
    /// </summary>
    private readonly Dictionary<long, DisplayList.DisplayListContentHash.PreparedSlice> _preparedByContentHash = new();

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

    /// <summary>
    /// Stable id for <paramref name="layerBox"/>'s layer across frames, keyed by its
    /// DOM element. Returns 0 for a layer whose root box has no element (no stable
    /// cross-frame identity — its tiles simply don't reuse across frames, the same
    /// degradation the old per-call cache had). Called at layer-tree build time.
    /// </summary>
    public long LayerIdFor(Box layerBox)
    {
        if (layerBox.Element is not { } element)
        {
            return 0;
        }

        if (!_layerIds.TryGetValue(element, out var id))
        {
            id = _nextLayerId++;
            _layerIds[element] = id;
        }

        return id;
    }

    /// <summary>
    /// Returns the prepared content-hash index for a layer slice, reusing a cached
    /// one when this slice content (<paramref name="contentHash"/>) was prepared
    /// before. A <paramref name="contentHash"/> of 0 (no stable identity) is always
    /// prepared fresh and not cached.
    /// </summary>
    public DisplayList.DisplayListContentHash.PreparedSlice GetOrPrepare(
        long contentHash, DisplayList.DisplayList items)
    {
        if (contentHash == 0)
        {
            return DisplayList.DisplayListContentHash.Prepare(items);
        }

        if (_preparedByContentHash.TryGetValue(contentHash, out var cached))
        {
            return cached;
        }

        var prepared = DisplayList.DisplayListContentHash.Prepare(items);
        _preparedByContentHash[contentHash] = prepared;
        return prepared;
    }

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
        _preparedByContentHash.Clear();
    }
}
