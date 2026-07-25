using System;
using System.Collections.Generic;

namespace Etch.Text.Atlas;

internal sealed class Shelf
{
    public int Y;
    public int X;
    public int Height;
    public Shelf? Next;

    public Shelf(int y, int height)
    {
        Y = y;
        X = 0;
        Height = height;
    }
}

internal sealed class ShelfPack
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _rowHeight;
    private Shelf? _head;

    public ShelfPack(int width, int height, int rowHeight)
    {
        _width = width;
        _height = height;
        _rowHeight = rowHeight;
        _head = new Shelf(0, rowHeight);
    }

    public bool Allocate(int w, int h, out int outX, out int outY)
    {
        outX = 0;
        outY = 0;

        // Add 1 pixel padding on all sides to prevent atlas bleeding with linear filtering
        const int pad = 1;
        int paddedW = w + pad;
        int paddedH = h + pad;

        // Reject glyphs that can never fit a shelf — too tall for the row, or
        // wider than the page. The new-shelf path below places at x=0 without a
        // width check, so without this guard a too-wide glyph would "succeed"
        // out of bounds and corrupt the texture upload.
        if (paddedH > _rowHeight || paddedW > _width)
        {
            return false;
        }

        for (var shelf = _head; shelf != null; shelf = shelf.Next)
        {
            if (paddedH > shelf.Height)
                continue;
            if (shelf.X + paddedW <= _width)
            {
                outX = shelf.X;
                outY = shelf.Y;
                shelf.X += paddedW;
                return true;
            }
        }

        int newY = GetLastShelfY() + _rowHeight;
        if (newY + _rowHeight > _height)
            return false;

        var newShelf = new Shelf(newY, _rowHeight);
        AddShelf(newShelf);
        outX = 0;
        outY = newY;
        newShelf.X = paddedW;
        return true;
    }

    public static void Free(int x, int w, int y)
    {
    }

    /// <summary>
    /// True when a glyph of height <paramref name="h"/> can never be placed
    /// because, padded, it exceeds the shelf row height. Mirrors the height
    /// check in <see cref="Allocate"/>.
    /// </summary>
    public bool IsTallerThanShelf(int h) => h + 1 > _rowHeight;

    private int GetLastShelfY()
    {
        var shelf = _head;
        while (shelf?.Next != null)
            shelf = shelf.Next;
        return shelf?.Y ?? 0;
    }

    private void AddShelf(Shelf newShelf)
    {
        var shelf = _head;
        while (shelf?.Next != null)
            shelf = shelf.Next;
        if (shelf != null)
            shelf.Next = newShelf;
        else
            _head = newShelf;
    }

    public void Reset()
    {
        _head = new Shelf(0, _rowHeight);
    }
}

internal sealed class LruCache
{
    private readonly Dictionary<GlyphCacheKey, LruCacheEntry> _map;
    private LruCacheEntry? _head;
    private LruCacheEntry? _tail;
    private int _totalSize;
    private readonly int _capacity;
    private readonly ShelfPack _packer;
    private readonly List<Slot> _evictedSlots;

    public LruCache(int capacity)
        : this(capacity, 4096, 4096, 256)
    {
    }

    public LruCache(int capacity, int packWidth, int packHeight, int rowHeight)
    {
        // capacity is the LRU budget in pixels (e.g. 2048*2048), which is far larger
        // than the number of glyphs the atlas can actually hold. Size the dictionary
        // by the likely glyph count to avoid pre-allocating multi-megabyte hash tables.
        int glyphCapacity = rowHeight > 0
            ? Math.Max(64, (packWidth / rowHeight) * (packHeight / rowHeight) * 4)
            : 64;
        _map = new Dictionary<GlyphCacheKey, LruCacheEntry>(glyphCapacity);
        _capacity = capacity;
        _packer = new ShelfPack(packWidth, packHeight, rowHeight);
        _evictedSlots = new List<Slot>();
    }

    public bool TryLookup(GlyphCacheKey key, out AtlasRegion region)
    {
        if (_map.TryGetValue(key, out var entry))
        {
            MoveToHead(entry);
            region = new AtlasRegion((ushort)entry.U, (ushort)entry.V, (ushort)entry.W, (ushort)entry.H, entry.OffsetX, entry.OffsetY);
            return true;
        }
        region = default;
        return false;
    }

    public bool TryInsert(GlyphCacheKey key, int w, int h, int u, int v, short offsetX, short offsetY, out AtlasRegion region)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            MoveToHead(existing);
            region = new AtlasRegion((ushort)existing.U, (ushort)existing.V, (ushort)existing.W, (ushort)existing.H, existing.OffsetX, existing.OffsetY);
            return true;
        }

        int approxSize = w * h;

        for (int i = _evictedSlots.Count - 1; i >= 0; i--)
        {
            var slot = _evictedSlots[i];
            if (slot.W >= w && slot.H >= h)
            {
                u = slot.X;
                v = slot.Y;
                _evictedSlots.RemoveAt(i);
                goto placed;
            }
        }

        while (_totalSize + approxSize > _capacity && _tail != null)
        {
            var evictEntry = _tail!;
            _evictedSlots.Add(new Slot { X = evictEntry.U, Y = evictEntry.V, W = evictEntry.W, H = evictEntry.H });
            EvictTail();
        }

        if (approxSize > _capacity)
        {
            region = default;
            return false;
        }

        if (!_packer.Allocate(w, h, out u, out v))
        {
            // Packer is full. ShelfPack doesn't reclaim freed space, and
            // evicted slots are our only reuse mechanism. If none fit,
            // the atlas is too fragmented to hold this glyph.
            // Returning false causes the glyph to be skipped this frame.
            // The caller (EtchGpuPresenter) uses a large enough atlas
            // that this should be rare.
            region = default;
            return false;
        }

    placed:
        var entry = new LruCacheEntry(key, w, h, u, v, offsetX, offsetY);
        AddToHead(entry);
        _map.Add(key, entry);
        _totalSize += approxSize;

        region = new AtlasRegion((ushort)u, (ushort)v, (ushort)w, (ushort)h, offsetX, offsetY);
        return true;
    }

    public int Count => _map.Count;

    public int TotalSize => _totalSize;

    public bool TryRemove(GlyphCacheKey key, out AtlasRegion region)
    {
        if (_map.TryGetValue(key, out var entry))
        {
            Remove(entry);
            _map.Remove(key);
            _totalSize -= entry.ApproxSize;
            _evictedSlots.Add(new Slot { X = entry.U, Y = entry.V, W = entry.W, H = entry.H });
            region = new AtlasRegion((ushort)entry.U, (ushort)entry.V, (ushort)entry.W, (ushort)entry.H, entry.OffsetX, entry.OffsetY);
            return true;
        }
        region = default;
        return false;
    }

    private void AddToHead(LruCacheEntry entry)
    {
        entry.Next = _head;
        entry.Prev = null;
        if (_head != null)
        {
            _head.Prev = entry;
        }
        _head = entry;
        if (_tail == null)
        {
            _tail = entry;
        }
    }

    private void Remove(LruCacheEntry entry)
    {
        if (entry.Prev != null)
        {
            entry.Prev.Next = entry.Next;
        }
        else
        {
            _head = entry.Next;
        }

        if (entry.Next != null)
        {
            entry.Next.Prev = entry.Prev;
        }
        else
        {
            _tail = entry.Prev;
        }
    }

    private void MoveToHead(LruCacheEntry entry)
    {
        if (entry == _head)
        {
            return;
        }
        Remove(entry);
        AddToHead(entry);
    }

    private void EvictTail()
    {
        if (_tail == null)
        {
            return;
        }

        var toEvict = _tail;
        Remove(toEvict);
        _map.Remove(toEvict.Key);
        _totalSize -= toEvict.ApproxSize;
    }

    /// <summary>
    /// Clears every cached glyph and resets the shelf packer to empty. The
    /// backing texture is left untouched — its stale pixels are overwritten as
    /// glyphs re-rasterize, and the 1px shelf padding prevents any sampling
    /// bleed in the meantime. Used for the generation reset (WP-3509) when the
    /// packer exhausts: cheap, and safe because glyph instances are rebuilt
    /// from draw commands every frame, so no UV outlives the reset.
    /// </summary>
    public void Reset()
    {
        _map.Clear();
        _head = null;
        _tail = null;
        _totalSize = 0;
        _evictedSlots.Clear();
        _packer.Reset();
    }

    /// <summary>
    /// True when a glyph of this height can never fit a shelf (taller than the
    /// packer's row height, padding included), so a <see cref="Reset"/> would
    /// not help. Distinguishes recoverable packer exhaustion from a glyph that
    /// is fundamentally too large for the atlas geometry.
    /// </summary>
    public bool IsTallerThanShelf(int h) => _packer.IsTallerThanShelf(h);

    private struct Slot
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }
}

internal sealed class LruCacheEntry
{
    public GlyphCacheKey Key { get; }
    public int ApproxSize { get; }
    public int U { get; set; }
    public int V { get; set; }
    public int W { get; set; }
    public int H { get; set; }
    public short OffsetX { get; set; }
    public short OffsetY { get; set; }
    public LruCacheEntry? Next { get; set; }
    public LruCacheEntry? Prev { get; set; }

    public LruCacheEntry(GlyphCacheKey key, int w, int h, int u, int v, short offsetX = 0, short offsetY = 0)
    {
        Key = key;
        ApproxSize = w * h;
        U = u;
        V = v;
        W = w;
        H = h;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
}

public readonly struct AtlasRegion
{
    public readonly ushort U;
    public readonly ushort V;
    public readonly ushort W;
    public readonly ushort H;
    public readonly short OffsetX;
    public readonly short OffsetY;

    public AtlasRegion(ushort u, ushort v, ushort w, ushort h, short offsetX = 0, short offsetY = 0)
    {
        U = u;
        V = v;
        W = w;
        H = h;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }
}