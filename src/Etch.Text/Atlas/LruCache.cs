using System;
using System.Collections.Generic;

namespace Etch.Text.Atlas;

/// <summary>
/// Shelf packing with shelves as tall as their glyphs: a glyph goes on the shortest shelf that is
/// tall enough and has room, else on a new shelf of its own height rounded up to
/// <see cref="ShelfQuantum"/> (similar glyphs share it). <c>maxShelfHeight</c> caps a shelf and so
/// the tallest glyph the page accepts. The packed area can grow (<see cref="Grow"/>): existing shelves
/// extend to the new width and new shelves start below them, so placed glyphs keep their texels.
/// </summary>
internal sealed class ShelfPack
{
    /// <summary>Shelf heights are multiples of this many texels.</summary>
    public const int ShelfQuantum = 8;

    // One texel right of and below every glyph, so neighbours never touch.
    private const int Padding = 1;

    private readonly int maxShelfHeight;
    private readonly List<PackShelf> shelves = new();
    private int width;
    private int height;
    private int nextShelfY;

    private struct PackShelf
    {
        public int Y;
        public int Height;
        public int NextX;
    }

    public ShelfPack(int width, int height, int maxShelfHeight)
    {
        this.width = width;
        this.height = height;
        this.maxShelfHeight = maxShelfHeight;
    }

    public bool Allocate(int w, int h, out int outX, out int outY)
    {
        outX = 0;
        outY = 0;
        int paddedW = w + Padding;
        int paddedH = h + Padding;

        // A glyph too tall for any shelf or wider than the page can never be placed.
        if (paddedH > maxShelfHeight || paddedW > width)
        {
            return false;
        }

        int best = -1;
        for (int i = 0; i < shelves.Count; i++)
        {
            var shelf = shelves[i];
            if (shelf.Height >= paddedH && shelf.NextX + paddedW <= width && (best < 0 || shelf.Height < shelves[best].Height))
            {
                best = i;
            }
        }
        if (best < 0)
        {
            int shelfHeight = Math.Min(maxShelfHeight, (paddedH + ShelfQuantum - 1) / ShelfQuantum * ShelfQuantum);
            if (nextShelfY + shelfHeight > height)
            {
                return false;
            }
            shelves.Add(new PackShelf { Y = nextShelfY, Height = shelfHeight, NextX = 0 });
            nextShelfY += shelfHeight;
            best = shelves.Count - 1;
        }

        var chosen = shelves[best];
        outX = chosen.NextX;
        outY = chosen.Y;
        chosen.NextX += paddedW;
        shelves[best] = chosen;
        return true;
    }

    /// <summary>
    /// True when a glyph of height <paramref name="h"/> can never be placed
    /// because, padded, it exceeds the tallest shelf. Mirrors the height
    /// check in <see cref="Allocate"/>.
    /// </summary>
    public bool IsTallerThanShelf(int h) => h + Padding > maxShelfHeight;

    /// <summary>Enlarges the packed area to <paramref name="newWidth"/> × <paramref name="newHeight"/>; placements are kept.</summary>
    public void Grow(int newWidth, int newHeight)
    {
        width = Math.Max(width, newWidth);
        height = Math.Max(height, newHeight);
    }

    /// <summary>Resizes the packed area and forgets every placement.</summary>
    public void Reset(int newWidth, int newHeight)
    {
        width = newWidth;
        height = newHeight;
        Reset();
    }

    public void Reset()
    {
        shelves.Clear();
        nextShelfY = 0;
    }
}

internal sealed class LruCache
{
    private readonly Dictionary<GlyphCacheKey, LruCacheEntry> _map;
    private LruCacheEntry? _head;
    private LruCacheEntry? _tail;
    private int _totalSize;
    private int _capacity;
    private readonly ShelfPack _packer;
    private readonly List<Slot> _evictedSlots;

    // The frame being built (see BeginFrame); 0 until a caller marks frames, which keeps plain LRU.
    private int _frame;

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

    /// <summary>
    /// Starts a frame. From then on, a glyph the frame has looked up or inserted is never evicted
    /// for another during the same frame: its texels are referenced by the frame's instances, so
    /// reusing its slot would draw the newcomer in its place. An insert that would need such an
    /// eviction fails instead, which flags the atlas exhausted and resets it between frames.
    /// </summary>
    public void BeginFrame() => _frame++;

    public bool TryLookup(GlyphCacheKey key, out AtlasRegion region)
    {
        if (_map.TryGetValue(key, out var entry))
        {
            entry.LastUsedFrame = _frame;
            MoveToHead(entry);
            region = new AtlasRegion((ushort)entry.U, (ushort)entry.V, (ushort)entry.W, (ushort)entry.H, entry.OffsetX, entry.OffsetY);
            return true;
        }
        region = default;
        return false;
    }

    public bool TryInsert(GlyphCacheKey key, int w, int h, int u, int v, short offsetX, short offsetY, out AtlasRegion region)
        => TryInsert(key, w, h, offsetX, offsetY, allowEviction: true, out region);

    /// <summary>
    /// Places a glyph. Without <paramref name="allowEviction"/> only free space is used (the caller
    /// would rather grow the page than evict); with it, glyphs no frame has used recently may be
    /// evicted for room.
    /// </summary>
    public bool TryInsert(GlyphCacheKey key, int w, int h, short offsetX, short offsetY, bool allowEviction, out AtlasRegion region)
    {
        int u;
        int v;
        if (_map.TryGetValue(key, out var existing))
        {
            existing.LastUsedFrame = _frame;
            MoveToHead(existing);
            region = new AtlasRegion((ushort)existing.U, (ushort)existing.V, (ushort)existing.W, (ushort)existing.H, existing.OffsetX, existing.OffsetY);
            return true;
        }

        int approxSize = w * h;

        if (TryTakeEvictedSlot(w, h, out u, out v))
        {
            goto placed;
        }

        while (allowEviction && _totalSize + approxSize > _capacity && _tail != null)
        {
            var evictEntry = _tail!;
            if (_frame != 0 && evictEntry.LastUsedFrame == _frame)
            {
                // Everything left was drawn this frame (the list is in recency order).
                region = default;
                return false;
            }
            _evictedSlots.Add(new Slot { X = evictEntry.U, Y = evictEntry.V, W = evictEntry.W, H = evictEntry.H });
            EvictTail();
        }

        if (approxSize > _capacity)
        {
            region = default;
            return false;
        }

        // A slot the evictions just freed.
        if (TryTakeEvictedSlot(w, h, out u, out v))
        {
            goto placed;
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
        var entry = new LruCacheEntry(key, w, h, u, v, offsetX, offsetY) { LastUsedFrame = _frame };
        AddToHead(entry);
        _map.Add(key, entry);
        _totalSize += approxSize;

        region = new AtlasRegion((ushort)u, (ushort)v, (ushort)w, (ushort)h, offsetX, offsetY);
        return true;
    }

    private bool TryTakeEvictedSlot(int w, int h, out int u, out int v)
    {
        for (int i = _evictedSlots.Count - 1; i >= 0; i--)
        {
            var slot = _evictedSlots[i];
            if (slot.W >= w && slot.H >= h)
            {
                u = slot.X;
                v = slot.Y;
                _evictedSlots.RemoveAt(i);
                return true;
            }
        }
        u = 0;
        v = 0;
        return false;
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
    /// The page grew to <paramref name="dimension"/>² texels: the budget and the packed area follow,
    /// and every cached glyph keeps its place.
    /// </summary>
    public void Grow(int dimension)
    {
        _capacity = dimension * dimension;
        _packer.Grow(dimension, dimension);
    }

    /// <summary>Forgets every glyph and resizes the page to <paramref name="dimension"/>² texels.</summary>
    public void Reset(int dimension)
    {
        Reset();
        _capacity = dimension * dimension;
        _packer.Reset(dimension, dimension);
    }

    /// <summary>
    /// True when a glyph of this height can never fit a shelf (taller than the
    /// packer's row height, padding included), so a <see cref="Reset()"/> would
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

    /// <summary>The last frame (<see cref="LruCache.BeginFrame"/>) that looked this glyph up or inserted it.</summary>
    public int LastUsedFrame { get; set; }
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