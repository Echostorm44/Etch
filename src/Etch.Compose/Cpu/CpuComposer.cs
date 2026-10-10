using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Gpu;
using Etch.Text.Atlas;

namespace Etch.Compose.Cpu;

/// <summary>
/// Executes a <see cref="DrawList"/> on the CPU into a <see cref="CpuFramebuffer"/>, producing the
/// image the <see cref="GpuComposer"/> produces: the same batches in the same order, the same
/// per-pixel arithmetic (<see cref="CpuShading"/> ports the shaders), the same sRGB target model.
/// </summary>
/// <remarks>
/// <para>
/// The target is cut into 64×64 tiles. Every draw is binned to the tiles its clipped extent
/// touches, and tiles render independently in parallel, each running its draws in paint order.
/// That is exact because a draw only reads the pixel it writes, with two exceptions the tiles
/// reproduce: mono glyph batches read the background as it stood before the batch (each tile
/// copies itself when it enters such a batch, as the GPU copies the framebuffer), and backdrop
/// blurs read neighbouring pixels, so each blur batch starts a phase after a whole-frame copy.
/// The result does not depend on the thread count or on scheduling.
/// </para>
/// <para>
/// Interior spans (<see cref="CpuSpans"/>) skip coverage evaluation where it is exactly 1 or 0, and
/// opaque solid runs are stored directly; both give the same pixels as the per-pixel path.
/// </para>
/// <para>
/// The composer owns memory-backed glyph and mask atlases; build the frame's draw list against them.
/// Steady-state frames allocate nothing: bins, snapshots and per-worker scratch are reused.
/// </para>
/// </remarks>
public sealed class CpuComposer : IDisposable
{
    /// <summary>Monochrome glyph atlas page size at its largest (one page; resets when full at this size).</summary>
    public const int GlyphAtlasSize = GpuComposer.GlyphAtlasSize;

    /// <summary>Monochrome glyph atlas page size a composer starts with; it doubles as glyphs arrive.</summary>
    public const int InitialGlyphAtlasSize = GpuComposer.InitialGlyphAtlasSize;

    /// <summary>Colour glyph atlas page size at its largest (one page; resets when full at this size).</summary>
    public const int ColorGlyphAtlasSize = GpuComposer.ColorGlyphAtlasSize;

    /// <summary>Colour glyph atlas page size a composer starts with; it doubles as colour glyphs arrive.</summary>
    public const int InitialColorGlyphAtlasSize = GpuComposer.InitialColorGlyphAtlasSize;

    /// <summary>Tile edge, pixels.</summary>
    public const int TileSize = 64;

    private const int TileShift = 6;

    private readonly GlyphAtlas monoAtlas;
    private readonly GlyphAtlas colorAtlas;
    private readonly MaskAtlas maskAtlas;
    private readonly TileWorkers workers;
    private bool disposed;

    // Per-frame state, reused across frames.
    private DrawList? frameList;
    private CpuFramebuffer? frameTarget;
    private uint[] snapshot = Array.Empty<uint>();
    private int tilesX;
    private int tilesY;
    private int[] tileStart = Array.Empty<int>();
    private int[] tileFill = Array.Empty<int>();
    private BinEntry[] entries = Array.Empty<BinEntry>();
    private TileRange[] ranges = Array.Empty<TileRange>();
    private int phaseEnd;
    private int phaseCounter;
    private TileScratch[] scratch = Array.Empty<TileScratch>();

    // Damage tracking (RenderIncremental): a hash per tile of everything drawn there, kept from the
    // last incremental frame into the same framebuffer.
    private bool trackDamage;
    private ulong monoGeneration;
    private ulong colorGeneration;
    private ulong maskGeneration;
    private ulong[] itemHashes = Array.Empty<ulong>();
    private ulong[] clipHashes = Array.Empty<ulong>();
    private ulong[] tileHashes = Array.Empty<ulong>();
    private ulong[] previousTileHashes = Array.Empty<ulong>();
    private bool[] tileDirty = Array.Empty<bool>();
    // CollectDirtyRects: per tile column, the rect whose run starts there and ends at the row above.
    private int[] openAbove = Array.Empty<int>();
    private int[] openHere = Array.Empty<int>();
    private bool renderDirtyOnly;
    private CpuFramebuffer? historyTarget;
    private int historyWidth;
    private int historyHeight;
    private readonly List<CpuDirtyRect> dirtyRects = new();

    /// <summary>Creates a CPU composer with empty atlases.</summary>
    public CpuComposer()
    {
        monoAtlas = new GlyphAtlas(GlyphAtlasSize, TextureFormat.R8Unorm, 128, 1, InitialGlyphAtlasSize);
        colorAtlas = new GlyphAtlas(ColorGlyphAtlasSize, TextureFormat.Rgba8UnormSrgb, 128, 1, InitialColorGlyphAtlasSize);
        maskAtlas = new MaskAtlas();
        workers = new TileWorkers(RunTiles);
    }

    /// <summary>The monochrome glyph atlas glyph instances' UVs refer to.</summary>
    public GlyphAtlas MonoAtlas => monoAtlas;

    /// <summary>The colour glyph atlas colour-glyph instances' UVs refer to.</summary>
    public GlyphAtlas ColorAtlas => colorAtlas;

    /// <summary>The coverage-mask atlas mask shapes and mask clips refer to.</summary>
    public MaskAtlas Masks => maskAtlas;

    /// <summary>When set, glyph batches are skipped (bisect text vs geometry).</summary>
    public bool SkipGlyphs { get; set; }

    /// <summary>Worker threads a frame may use; -1 (the default) uses every core, 1 renders on the caller.</summary>
    public int MaxDegreeOfParallelism { get; set; } = -1;

    /// <summary>
    /// When false, every pixel takes the per-pixel path (no interior spans, row reuse or lookup
    /// tables) — the reference the fast paths are tested to reproduce exactly.
    /// </summary>
    internal bool FastPaths { get; set; } = true;

    /// <summary>
    /// Starts a frame: clears every glyph atlas that filled up last frame (or all, when <paramref name="force"/>),
    /// protects the glyphs the new frame uses from eviction (<see cref="GlyphAtlas.BeginFrame"/>) and ends
    /// the mask atlas's frame (eviction, page budget: <see cref="MaskAtlas.EndFrame"/>). Call between frames.
    /// </summary>
    public void ResetAtlasesIfExhausted(bool force)
    {
        if (force || monoAtlas.WasExhausted)
        {
            monoAtlas.Reset();
        }
        if (force || colorAtlas.WasExhausted)
        {
            colorAtlas.Reset();
        }
        // A new frame: the glyphs it uses are protected from eviction until the next one.
        monoAtlas.BeginFrame();
        colorAtlas.BeginFrame();
        maskAtlas.EndFrame(force);
    }

    /// <summary>
    /// Renders the whole of <paramref name="list"/> (finished) into <paramref name="target"/>. Forgets
    /// the damage history: the next <see cref="RenderIncremental"/> renders everything.
    /// </summary>
    public void Render(DrawList list, CpuFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(disposed, this);
        historyTarget = null;
        RenderFrame(list, target, incremental: false);
    }

    /// <summary>
    /// Renders <paramref name="list"/> into <paramref name="target"/>, which must hold the previous
    /// frame this method rendered into it: only the 64×64 tiles whose draws changed are rendered
    /// again (a tile's hash covers every draw binned to it, the tables those draws index, the atlas
    /// generations, and backdrop blurs reaching into it). Returns the changed rects, valid until the
    /// next call; the whole target when there is no usable history (first frame, other target,
    /// resize, <see cref="Render"/> in between).
    /// </summary>
    /// <remarks>
    /// Images are identified by handle and instance: an image's pixels must not change once drawn
    /// (the GPU composer caches its texture by handle on the same assumption).
    /// </remarks>
    public ReadOnlySpan<CpuDirtyRect> RenderIncremental(DrawList list, CpuFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(disposed, this);
        RenderFrame(list, target, incremental: true);
        return CollectionsMarshal.AsSpan(dirtyRects);
    }

    private void RenderFrame(DrawList list, CpuFramebuffer target, bool incremental)
    {
        int width = (int)list.Width;
        int height = (int)list.Height;
        dirtyRects.Clear();
        if (width <= 0 || height <= 0)
        {
            // Nothing to draw into (a zero-height client area): no frame, no history.
            historyTarget = null;
            return;
        }
        bool history = incremental && ReferenceEquals(historyTarget, target)
            && historyWidth == width && historyHeight == height && target.Width == width && target.Height == height;
        target.Resize(width, height);
        try
        {
            RenderTiles(list, target, incremental, history, width, height);
        }
        catch
        {
            // A frame that did not finish leaves the target and the hashes out of step: the next
            // incremental frame starts over.
            historyTarget = null;
            dirtyRects.Clear();
            throw;
        }
        finally
        {
            frameList = null;
            frameTarget = null;
        }
    }

    private void RenderTiles(DrawList list, CpuFramebuffer target, bool incremental, bool history, int width, int height)
    {

        trackDamage = incremental;
        Bin(list, width, height);
        int tileCount = tilesX * tilesY;
        renderDirtyOnly = false;
        if (incremental)
        {
            HashTiles(list);
            if (history)
            {
                for (int t = 0; t < tileCount; t++)
                {
                    tileDirty[t] = tileHashes[t] != previousTileHashes[t];
                }
                DilateForBlurs(list);
                renderDirtyOnly = true;
            }
            else
            {
                Array.Fill(tileDirty, true, 0, tileCount);
            }
            (tileHashes, previousTileHashes) = (previousTileHashes, tileHashes);
            historyTarget = target;
            historyWidth = width;
            historyHeight = height;
            CollectDirtyRects(width, height);
            if (dirtyRects.Count == 0)
            {
                return;
            }
        }

        if (renderDirtyOnly)
        {
            ClearDirtyTiles(target);
        }
        else
        {
            target.Pixels.Fill(0xFF000000u);
        }

        frameList = list;
        frameTarget = target;
        int threads = MaxDegreeOfParallelism <= 0 ? Environment.ProcessorCount : MaxDegreeOfParallelism;
        // At least four tiles per worker: waking a thread for less costs more than it saves.
        int workTiles = renderDirtyOnly ? DirtyTileCount(tileCount) : tileCount;
        threads = Math.Clamp(threads, 1, Math.Max(1, workTiles / 4));
        EnsureScratch(threads);

        // Phases: a backdrop-blur batch reads beyond its tile, so it starts a phase after a
        // whole-frame copy of the pixels its batch covers.
        var batches = list.Batches;
        int phaseStart = 0;
        while (phaseStart < batches.Length)
        {
            int end = phaseStart + 1;
            while (end < batches.Length && batches[end].Kind != DrawKind.Blur)
            {
                end++;
            }
            if (batches[phaseStart].Kind == DrawKind.Blur && batches[phaseStart].Count > 0)
            {
                // Only backdrop blurs read the whole-frame copy: a frame without one never allocates it.
                if (snapshot.Length < target.Width * target.Height)
                {
                    snapshot = new uint[target.Width * target.Height];
                }
                CopyRect(target, batches[phaseStart], new Region(0, 0, target.Width, target.Height), snapshot, 0, 0, target.Width);
            }
            phaseEnd = end;
            phaseCounter = 0;
            workers.Run(threads);
            phaseStart = end;
        }
        frameList = null;
        frameTarget = null;
    }

    // ── Damage ──────────────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong h, ulong value) => BitOperations.RotateLeft(h ^ value, 27) * 0x9E3779B97F4A7C15UL + 0x165667B19E3779F9UL;

    private static ulong HashOf<T>(in T value)
        where T : unmanaged
        => XxHash3.HashToUInt64(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    // Hashes of every clip-table entry, with its mask tiles and the atlas content they point at.
    private void HashClips(DrawList list)
    {
        var clips = list.Clips;
        if (clipHashes.Length < clips.Length)
        {
            clipHashes = new ulong[Math.Max(clips.Length, clipHashes.Length * 2)];
        }
        var tiles = list.MaskTiles;
        for (int i = 0; i < clips.Length; i++)
        {
            ref readonly var clip = ref clips[i];
            ulong h = HashOf(clip);
            if (clip.HasMask != 0)
            {
                int columns = Math.Max(clip.MaskTileColumns, 1);
                int rows = (clip.MaskHeight + MaskAtlas.ClipMaskTile - 1) / MaskAtlas.ClipMaskTile;
                int count = Math.Min(columns * rows, tiles.Length - clip.MaskTileStart);
                if (count > 0)
                {
                    h = Mix(h, XxHash3.HashToUInt64(MemoryMarshal.AsBytes(tiles.Slice(clip.MaskTileStart, count))));
                }
                h = Mix(h, maskGeneration);
            }
            clipHashes[i] = h;
        }
    }

    // One draw's hash: its instance, the clip and paint it indexes, and what its texels come from.
    private ulong ItemHash(DrawList list, DrawKind kind, int index)
    {
        switch (kind)
        {
            case DrawKind.Shape:
                {
                    ref readonly var s = ref CollectionsMarshal.AsSpan(list.OrderedShapes)[index];
                    ulong h = Mix(HashOf(s), clipHashes[(int)s.ClipIndex]);
                    if (s.PaintIndex != 0)
                    {
                        ref readonly var g = ref list.Gradients[(int)s.PaintIndex];
                        h = Mix(h, HashOf(g));
                        h = Mix(h, XxHash3.HashToUInt64(MemoryMarshal.AsBytes(list.GradientStops.Slice((int)g.StopStart, (int)g.StopCount))));
                    }
                    if (s.Type == ShapeType.Mask)
                    {
                        h = Mix(h, maskGeneration);
                    }
                    return h;
                }
            case DrawKind.Glyph:
                {
                    ref readonly var g = ref CollectionsMarshal.AsSpan(list.OrderedGlyphs)[index];
                    return Mix(Mix(HashOf(g), clipHashes[(int)g.ClipIndex]), monoGeneration);
                }
            case DrawKind.ColorGlyph:
                {
                    ref readonly var g = ref CollectionsMarshal.AsSpan(list.OrderedColorGlyphs)[index];
                    return Mix(Mix(HashOf(g), clipHashes[(int)g.ClipIndex]), colorGeneration);
                }
            case DrawKind.Image:
                {
                    ref readonly var m = ref CollectionsMarshal.AsSpan(list.OrderedImages)[index];
                    ulong h = Mix(HashOf(m), clipHashes[(int)m.ClipIndex]);
                    ulong identity = list.Images.TryGetValue(m.Handle, out var image) ? (ulong)RuntimeHelpers.GetHashCode(image) : 0;
                    return Mix(h, identity);
                }
            default:
                {
                    ref readonly var u = ref CollectionsMarshal.AsSpan(list.OrderedBlurs)[index];
                    return Mix(HashOf(u), clipHashes[(int)u.ClipIndex]);
                }
        }
    }

    // A tile's hash: the frame's parameters and size, then each draw binned to it in paint order,
    // with where batches begin (a glyph batch reads the tile as it stood at its start).
    private void HashTiles(DrawList list)
    {
        int tileCount = tilesX * tilesY;
        if (tileHashes.Length < tileCount)
        {
            tileHashes = new ulong[tileCount];
            previousTileHashes = new ulong[tileCount];
            tileDirty = new bool[tileCount];
        }
        var p = list.Parameters;
        ulong seed = Mix(Mix(HashOf(p), list.Width), list.Height);
        var batches = list.Batches;
        for (int t = 0; t < tileCount; t++)
        {
            ulong h = seed;
            int previousBatch = -1;
            for (int i = tileStart[t]; i < tileStart[t + 1]; i++)
            {
                var entry = entries[i];
                ulong kind = (ulong)batches[entry.Batch].Kind;
                if (entry.Batch != previousBatch)
                {
                    kind |= 0x100;
                    previousBatch = entry.Batch;
                }
                h = Mix(Mix(h, kind), itemHashes[entry.Item]);
            }
            tileHashes[t] = h;
        }
    }

    // A backdrop blur reads around itself: when anything it reads or covers changed, every tile in
    // its reach renders again (so the tiles it reads hold their state at the blur's phase). Marking
    // one blur's reach can put tiles in another blur's reach — one drawn earlier, say — so this
    // repeats until no blur adds a tile (each pass only adds tiles: it ends within tile-count passes).
    private void DilateForBlurs(DrawList list)
    {
        var blurs = CollectionsMarshal.AsSpan(list.OrderedBlurs);
        if (blurs.IsEmpty)
        {
            return;
        }
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (ref readonly var blur in blurs)
            {
                float reach = DrawList.BlurReach(blur.Sigma);
                int x0 = Math.Clamp((int)MathF.Floor(blur.MinX - reach), 0, historyWidth - 1) >> TileShift;
                int y0 = Math.Clamp((int)MathF.Floor(blur.MinY - reach), 0, historyHeight - 1) >> TileShift;
                int x1 = Math.Clamp((int)MathF.Ceiling(blur.MaxX + reach), 0, historyWidth - 1) >> TileShift;
                int y1 = Math.Clamp((int)MathF.Ceiling(blur.MaxY + reach), 0, historyHeight - 1) >> TileShift;
                bool any = false;
                bool all = true;
                for (int ty = y0; ty <= y1; ty++)
                {
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        bool dirty = tileDirty[ty * tilesX + tx];
                        any |= dirty;
                        all &= dirty;
                    }
                }
                if (!any || all)
                {
                    continue;
                }
                for (int ty = y0; ty <= y1; ty++)
                {
                    for (int tx = x0; tx <= x1; tx++)
                    {
                        tileDirty[ty * tilesX + tx] = true;
                    }
                }
                changed = true;
            }
        }
    }
    // Dirty tiles as rects: runs of dirty tiles along each tile row, merged with the run above when
    // it spans the same columns. Linear in the tile count: a run looks up the one rect that could
    // extend (the run above starting at the same column), not every rect so far.
    private void CollectDirtyRects(int width, int height)
    {
        if (openAbove.Length < tilesX)
        {
            openAbove = new int[tilesX];
            openHere = new int[tilesX];
        }
        Array.Fill(openAbove, -1, 0, tilesX);
        for (int ty = 0; ty < tilesY; ty++)
        {
            int y = ty << TileShift;
            int h = Math.Min(TileSize, height - y);
            Array.Fill(openHere, -1, 0, tilesX);
            int tx = 0;
            while (tx < tilesX)
            {
                if (!tileDirty[ty * tilesX + tx])
                {
                    tx++;
                    continue;
                }
                int first = tx;
                while (tx < tilesX && tileDirty[ty * tilesX + tx])
                {
                    tx++;
                }
                int x = first << TileShift;
                int w = Math.Min(tx << TileShift, width) - x;
                int r = openAbove[first];
                if (r >= 0 && dirtyRects[r].Width == w)
                {
                    dirtyRects[r] = dirtyRects[r] with { Height = dirtyRects[r].Height + h };
                }
                else
                {
                    r = dirtyRects.Count;
                    dirtyRects.Add(new CpuDirtyRect(x, y, w, h));
                }
                openHere[first] = r;
            }
            (openAbove, openHere) = (openHere, openAbove);
        }
    }

    private int DirtyTileCount(int tileCount)
    {
        int n = 0;
        for (int t = 0; t < tileCount; t++)
        {
            n += tileDirty[t] ? 1 : 0;
        }
        return n;
    }

    private void ClearDirtyTiles(CpuFramebuffer target)
    {
        var pixels = target.Pixels;
        int width = target.Width;
        foreach (var rect in dirtyRects)
        {
            for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            {
                pixels.Slice(y * width + rect.X, rect.Width).Fill(0xFF000000u);
            }
        }
    }
    /// <summary>
    /// Bytes this composer's worker threads have allocated (as observed after each frame they ran).
    /// With the calling thread's own counter, what a frame allocated — other threads excluded.
    /// </summary>
    internal long WorkerAllocatedBytes => workers.WorkerAllocatedBytes;

    /// <summary>Releases the per-frame buffers (bins, snapshot, scratch); atlases are kept.</summary>
    public void Trim()
    {
        snapshot = Array.Empty<uint>();
        tileStart = Array.Empty<int>();
        tileFill = Array.Empty<int>();
        entries = Array.Empty<BinEntry>();
        ranges = Array.Empty<TileRange>();
        scratch = Array.Empty<TileScratch>();
        itemHashes = Array.Empty<ulong>();
        clipHashes = Array.Empty<ulong>();
        tileHashes = Array.Empty<ulong>();
        previousTileHashes = Array.Empty<ulong>();
        tileDirty = Array.Empty<bool>();
        openAbove = Array.Empty<int>();
        openHere = Array.Empty<int>();
        dirtyRects.Clear();
        dirtyRects.TrimExcess();
        historyTarget = null;
    }

    /// <summary>
    /// Forgets every cached glyph and shrinks both glyph atlases back to their initial size
    /// (<see cref="InitialGlyphAtlasSize"/>, <see cref="InitialColorGlyphAtlasSize"/>): the memory a
    /// peak left behind is released, and the next frame rasterizes its glyphs again. Call between frames.
    /// </summary>
    public void TrimGlyphAtlases()
    {
        monoAtlas.Trim();
        colorAtlas.Trim();
    }

    /// <summary>The memory the composer holds right now, by owner (atlases, per-frame buffers).</summary>
    public CpuMemoryUsage MemoryUsage()
    {
        long frameBytes = (long)snapshot.Length * sizeof(uint)
            + (long)(tileStart.Length + tileFill.Length + openAbove.Length + openHere.Length) * sizeof(int)
            + (long)entries.Length * Unsafe.SizeOf<BinEntry>()
            + (long)ranges.Length * Unsafe.SizeOf<TileRange>()
            + (long)(itemHashes.Length + clipHashes.Length + tileHashes.Length + previousTileHashes.Length) * sizeof(ulong)
            + tileDirty.Length
            + (long)scratch.Length * TileScratch.Bytes;
        return new CpuMemoryUsage(monoAtlas.ResidentBytes, colorAtlas.ResidentBytes, maskAtlas.ResidentBytes, frameBytes);
    }

    /// <summary>A rectangle of target pixels [X0, X1) × [Y0, Y1).</summary>
    internal readonly record struct Region(int X0, int Y0, int X1, int Y1);

    /// <summary>A draw binned to a tile: its batch and its index in the batch kind's ordered list.</summary>
    private readonly record struct BinEntry(int Batch, int Index, int Item);

    /// <summary>The tiles a draw touches, inclusive; empty when <see cref="X1"/> &lt; <see cref="X0"/>.</summary>
    private readonly record struct TileRange(int X0, int Y0, int X1, int Y1);

    /// <summary>Per-worker scratch: the tile's background copy and lookup tables.</summary>
    private sealed class TileScratch
    {
        public const long Bytes = (TileSize * TileSize + TileSize + 256) * 4;

        public readonly uint[] Background = new uint[TileSize * TileSize];
        public readonly float[] RowCoverage = new float[TileSize];
        public readonly float[] DarkWeight = new float[256];
        public float DarkKeyGamma = float.NaN;
    }

    // ── Binning ─────────────────────────────────────────────────────────

    private void Bin(DrawList list, int width, int height)
    {
        tilesX = (width + TileSize - 1) >> TileShift;
        tilesY = (height + TileSize - 1) >> TileShift;
        int tileCount = tilesX * tilesY;
        if (tileStart.Length < tileCount + 1)
        {
            tileStart = new int[tileCount + 1];
            tileFill = new int[tileCount];
        }

        var batches = list.Batches;
        int items = 0;
        for (int b = 0; b < batches.Length; b++)
        {
            items += batches[b].Count;
        }
        if (ranges.Length < items)
        {
            ranges = new TileRange[Math.Max(items, ranges.Length * 2)];
        }

        Array.Clear(tileStart, 0, tileCount + 1);
        var clips = list.Clips;
        if (trackDamage)
        {
            // Atlas generations, read once per frame (each read takes the atlas's lock).
            monoGeneration = (ulong)(uint)monoAtlas.Generation;
            colorGeneration = (ulong)(uint)colorAtlas.Generation | (1UL << 40);
            maskGeneration = (ulong)(uint)maskAtlas.ContentGeneration;
            HashClips(list);
            if (itemHashes.Length < items)
            {
                itemHashes = new ulong[Math.Max(items, itemHashes.Length * 2)];
            }
        }
        int item = 0;
        for (int b = 0; b < batches.Length; b++)
        {
            ref readonly var batch = ref batches[b];
            for (int i = 0; i < batch.Count; i++)
            {
                var range = RangeOf(list, clips, batch.Kind, batch.Start + i, width, height);
                if (trackDamage)
                {
                    itemHashes[item] = ItemHash(list, batch.Kind, batch.Start + i);
                }
                ranges[item++] = range;
                for (int ty = range.Y0; ty <= range.Y1; ty++)
                {
                    for (int tx = range.X0; tx <= range.X1; tx++)
                    {
                        tileStart[ty * tilesX + tx + 1]++;
                    }
                }
            }
        }
        for (int t = 0; t < tileCount; t++)
        {
            tileStart[t + 1] += tileStart[t];
        }
        int total = tileStart[tileCount];
        if (entries.Length < total)
        {
            entries = new BinEntry[Math.Max(total, entries.Length * 2)];
        }
        Array.Copy(tileStart, tileFill, tileCount);
        item = 0;
        for (int b = 0; b < batches.Length; b++)
        {
            ref readonly var batch = ref batches[b];
            for (int i = 0; i < batch.Count; i++)
            {
                int itemNumber = item++;
                var range = ranges[itemNumber];
                for (int ty = range.Y0; ty <= range.Y1; ty++)
                {
                    for (int tx = range.X0; tx <= range.X1; tx++)
                    {
                        entries[tileFill[ty * tilesX + tx]++] = new BinEntry(b, batch.Start + i, itemNumber);
                    }
                }
            }
        }
        // tileFill becomes each tile's cursor: phases resume where the previous one stopped.
        Array.Copy(tileStart, tileFill, tileCount);
    }

    private static TileRange RangeOf(DrawList list, ReadOnlySpan<ClipEntry> clips, DrawKind kind, int index, int width, int height)
    {
        float minX, minY, maxX, maxY;
        uint clipIndex;
        switch (kind)
        {
            case DrawKind.Shape:
                {
                    ref readonly var s = ref CollectionsMarshal.AsSpan(list.OrderedShapes)[index];
                    (minX, minY, maxX, maxY, clipIndex) = (s.MinX, s.MinY, s.MaxX, s.MaxY, s.ClipIndex);
                    break;
                }
            case DrawKind.Glyph:
                {
                    ref readonly var g = ref CollectionsMarshal.AsSpan(list.OrderedGlyphs)[index];
                    (minX, minY, maxX, maxY, clipIndex) = (g.PosX, g.PosY, g.PosX + g.SizeX, g.PosY + g.SizeY, g.ClipIndex);
                    break;
                }
            case DrawKind.ColorGlyph:
                {
                    ref readonly var g = ref CollectionsMarshal.AsSpan(list.OrderedColorGlyphs)[index];
                    (minX, minY, maxX, maxY, clipIndex) = (g.PosX, g.PosY, g.PosX + g.SizeX, g.PosY + g.SizeY, g.ClipIndex);
                    break;
                }
            case DrawKind.Image:
                {
                    ref readonly var m = ref CollectionsMarshal.AsSpan(list.OrderedImages)[index];
                    (minX, minY, maxX, maxY, clipIndex) = (m.MinX, m.MinY, m.MaxX, m.MaxY, m.ClipIndex);
                    break;
                }
            default:
                {
                    ref readonly var u = ref CollectionsMarshal.AsSpan(list.OrderedBlurs)[index];
                    (minX, minY, maxX, maxY, clipIndex) = (u.MinX, u.MinY, u.MaxX, u.MaxY, u.ClipIndex);
                    break;
                }
        }
        ref readonly var clip = ref clips[(int)clipIndex];
        minX = MathF.Max(minX, clip.MinX);
        minY = MathF.Max(minY, clip.MinY);
        maxX = MathF.Min(maxX, clip.MaxX);
        maxY = MathF.Min(maxY, clip.MaxY);
        // Pixels whose centre lies in [min, max), clamped to the target.
        int x0 = Math.Max(0, (int)MathF.Ceiling(minX - 0.5f));
        int y0 = Math.Max(0, (int)MathF.Ceiling(minY - 0.5f));
        int x1 = Math.Min(width, (int)MathF.Ceiling(maxX - 0.5f));
        int y1 = Math.Min(height, (int)MathF.Ceiling(maxY - 0.5f));
        if (!(x1 > x0 && y1 > y0))
        {
            return new TileRange(0, 0, -1, -1);
        }
        return new TileRange(x0 >> TileShift, y0 >> TileShift, (x1 - 1) >> TileShift, (y1 - 1) >> TileShift);
    }

    // ── Tiles ───────────────────────────────────────────────────────────

    private void EnsureScratch(int threads)
    {
        if (scratch.Length >= threads)
        {
            return;
        }
        var grown = new TileScratch[threads];
        Array.Copy(scratch, grown, scratch.Length);
        for (int i = scratch.Length; i < threads; i++)
        {
            grown[i] = new TileScratch();
        }
        scratch = grown;
    }

    // Worker body: claims tiles until none are left, rendering each tile's draws of this phase.
    private void RunTiles(int worker)
    {
        var list = frameList!;
        var target = frameTarget!;
        var work = scratch[worker];
        int tileCount = tilesX * tilesY;
        int tile;
        while ((tile = Interlocked.Increment(ref phaseCounter) - 1) < tileCount)
        {
            if (renderDirtyOnly && !tileDirty[tile])
            {
                continue;
            }
            RenderTile(list, target, tile, work);
        }
    }

    private void RenderTile(DrawList list, CpuFramebuffer target, int tile, TileScratch work)
    {
        int tx = tile % tilesX;
        int ty = tile / tilesX;
        var region = new Region(tx << TileShift, ty << TileShift,
            Math.Min(target.Width, (tx + 1) << TileShift), Math.Min(target.Height, (ty + 1) << TileShift));
        var batches = list.Batches;
        int end = tileStart[tile + 1];
        // Each tile resumes where the previous phase stopped: its entries are in paint order.
        ref int cursor = ref tileFill[tile];
        if (FastPaths)
        {
            cursor = LastCover(list, region, cursor, end);
        }
        int currentBatch = -1;
        while (cursor < end)
        {
            var entry = entries[cursor];
            if (entry.Batch >= phaseEnd)
            {
                return;
            }
            cursor++;
            ref readonly var batch = ref batches[entry.Batch];
            if (entry.Batch != currentBatch)
            {
                currentBatch = entry.Batch;
                if (batch.Kind == DrawKind.Glyph)
                {
                    CopyRect(target, batch, region, work.Background, region.X0, region.Y0, TileSize);
                }
            }
            switch (batch.Kind)
            {
                case DrawKind.Shape:
                    DrawShape(list, target, CollectionsMarshal.AsSpan(list.OrderedShapes)[entry.Index], region, work);
                    break;
                case DrawKind.Glyph:
                    if (!SkipGlyphs)
                    {
                        DrawGlyph(list, target, CollectionsMarshal.AsSpan(list.OrderedGlyphs)[entry.Index], region, work);
                    }
                    break;
                case DrawKind.ColorGlyph:
                    if (!SkipGlyphs)
                    {
                        DrawColorGlyph(list, target, CollectionsMarshal.AsSpan(list.OrderedColorGlyphs)[entry.Index], region);
                    }
                    break;
                case DrawKind.Image:
                    DrawImage(list, target, CollectionsMarshal.AsSpan(list.OrderedImages)[entry.Index], region);
                    break;
                case DrawKind.Blur:
                    DrawBlur(list, target, CollectionsMarshal.AsSpan(list.OrderedBlurs)[entry.Index], region, snapshot);
                    break;
            }
        }
    }


    // Occlusion: the last draw of this phase that paints the whole tile with one opaque colour
    // makes everything before it in the phase invisible, so the tile starts there. (Not across
    // phases: a blur phase's snapshot must see the tile as painted up to that point.)
    private int LastCover(DrawList list, Region region, int from, int end)
    {
        var batches = list.Batches;
        var shapes = CollectionsMarshal.AsSpan(list.OrderedShapes);
        float dissolve = list.Parameters.Dissolve;
        int last = from;
        if (dissolve > 0f)
        {
            return last;
        }
        for (int i = from; i < end; i++)
        {
            var entry = entries[i];
            if (entry.Batch >= phaseEnd)
            {
                break;
            }
            if (batches[entry.Batch].Kind == DrawKind.Shape && CoversRegion(list, shapes[entry.Index], region))
            {
                last = i;
            }
        }
        return last;
    }

    private static bool CoversRegion(DrawList list, in ShapeInstance inst, Region region)
    {
        if (inst.PaintIndex != 0 || inst.A0 != 1f || !CpuSpans.IsAxisAligned(inst)
            || inst.Type is not (ShapeType.Rect or ShapeType.RoundedRect or ShapeType.Circle))
        {
            return false;
        }
        if (!PixelSpan(inst.MinX, inst.MaxX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(inst.MinY, inst.MaxY, region.Y0, region.Y1, out int y0, out int y1)
            || x0 != region.X0 || x1 != region.X1 || y0 != region.Y0 || y1 != region.Y1)
        {
            return false;
        }
        // The interiors are convex: if the first and last rows cover the tile's width, every row does.
        ref readonly var clip = ref list.Clips[(int)inst.ClipIndex];
        return RowCovers(inst, clip, region.Y0, region) && RowCovers(inst, clip, region.Y1 - 1, region);

        static bool RowCovers(in ShapeInstance inst, in ClipEntry clip, int y, Region region)
        {
            CpuSpans.Interior(inst, y, out int a, out int b);
            CpuSpans.ClipInterior(clip, y, out int ca, out int cb);
            return a <= region.X0 && b >= region.X1 && ca <= region.X0 && cb >= region.X1;
        }
    }

    // The GPU copies the batch's bounds (rounded out) before a glyph or blur batch; pixels outside
    // that rect keep their previous copy, which nothing in the batch reads. Copies the part inside
    // `region` into `destination` (origin, stride).
    private static void CopyRect(CpuFramebuffer target, in DrawBatch batch, Region region, uint[] destination, int originX, int originY, int stride)
    {
        int x0 = Math.Max(region.X0, (int)Math.Clamp(MathF.Floor(batch.MinX), 0f, target.Width));
        int y0 = Math.Max(region.Y0, (int)Math.Clamp(MathF.Floor(batch.MinY), 0f, target.Height));
        int x1 = Math.Min(region.X1, (int)Math.Clamp(MathF.Ceiling(batch.MaxX), 0f, target.Width));
        int y1 = Math.Min(region.Y1, (int)Math.Clamp(MathF.Ceiling(batch.MaxY), 0f, target.Height));
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }
        var pixels = target.Pixels;
        int width = target.Width;
        for (int y = y0; y < y1; y++)
        {
            pixels.Slice(y * width + x0, x1 - x0).CopyTo(destination.AsSpan((y - originY) * stride + x0 - originX, x1 - x0));
        }
    }

    // Pixels whose centre lies in [min, max) — the GPU rasterization rule for a quad.
    private static bool PixelSpan(float min, float max, int regionMin, int regionMax, out int first, out int end)
    {
        first = Math.Max(regionMin, (int)MathF.Ceiling(min - 0.5f));
        end = Math.Min(regionMax, (int)MathF.Ceiling(max - 0.5f));
        return end > first;
    }

    private MaskSource MasksOf(DrawList list) => new(maskAtlas.Pages, list.MaskTiles, maskAtlas.PageDimension);

    // ── Shapes ──────────────────────────────────────────────────────────

    private void DrawShape(DrawList list, CpuFramebuffer target, in ShapeInstance inst, Region region, TileScratch work)
    {
        if (!PixelSpan(inst.MinX, inst.MaxX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(inst.MinY, inst.MaxY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        ref readonly var clip = ref list.Clips[(int)inst.ClipIndex];
        float dissolve = list.Parameters.Dissolve;
        if (inst.Type == ShapeType.Shadow && dissolve <= 0f && FastPaths)
        {
            DrawShadow(list, target, inst, clip, x0, x1, y0, y1, work);
            return;
        }
        var gradients = list.Gradients;
        var stops = list.GradientStops;
        var mask = MasksOf(list);
        var pixels = target.Pixels;
        int width = target.Width;
        bool spans = FastPaths && dissolve <= 0f && CpuSpans.IsAxisAligned(inst);
        bool solid = inst.PaintIndex == 0;
        uint opaque = solid && inst.A0 == 1f ? CpuShading.Pack(inst.R0, inst.G0, inst.B0, 1f) : 0u;
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            int fastA = x0, fastB = x0;
            int holeA = x0, holeB = x0;
            if (spans)
            {
                CpuSpans.ClipInterior(clip, y, out int ca, out int cb);
                if (cb <= ca && clip.HasMask == 0 && clip.HasRound == 0)
                {
                    // Outside the hard clip on this row: nothing is drawn.
                    continue;
                }
                CpuSpans.Interior(inst, y, out int ia, out int ib);
                fastA = Math.Max(Math.Max(ia, ca), x0);
                fastB = Math.Min(Math.Min(ib, cb), x1);
                if (fastB < fastA)
                {
                    fastB = fastA;
                }
                CpuSpans.Hole(inst, y, out int ha, out int hb);
                holeA = Math.Max(ha, x0);
                holeB = Math.Min(hb, x1);
            }
            if (fastB > fastA)
            {
                ShadeSpan(inst, list, pixels, row, x0, fastA, py, clip, mask, gradients, stops, dissolve, holeA, holeB);
                FillInterior(inst, pixels.Slice(row + fastA, fastB - fastA), fastA, py, gradients, stops, opaque);
                ShadeSpan(inst, list, pixels, row, fastB, x1, py, clip, mask, gradients, stops, dissolve, holeA, holeB);
            }
            else
            {
                ShadeSpan(inst, list, pixels, row, x0, x1, py, clip, mask, gradients, stops, dissolve, holeA, holeB);
            }
        }
    }

    // The per-pixel path over [from, to), skipping [holeA, holeB) where coverage is known to be 0.
    private static void ShadeSpan(in ShapeInstance inst, DrawList list, Span<uint> pixels, int row, int from, int to, float py,
        in ClipEntry clip, in MaskSource mask, ReadOnlySpan<GradientEntry> gradients, ReadOnlySpan<GradientStopEntry> stops,
        float dissolve, int holeA, int holeB)
    {
        for (int x = from; x < to; x++)
        {
            if (x >= holeA && x < holeB)
            {
                x = holeB - 1;
                continue;
            }
            float px = x + 0.5f;
            if (CpuShading.Dissolved(px, py, dissolve))
            {
                continue;
            }
            float cov = CpuShading.ShapeCoverage(inst, px, py, mask) * CpuShading.ClipCoverage(px, py, clip, mask);
            if (!(cov > 0f))
            {
                continue;
            }
            var (r, g, b, a) = CpuShading.PaintColor(inst, px, py, gradients, stops);
            pixels[row + x] = CpuShading.BlendStraight(pixels[row + x], r, g, b, a * cov);
        }
    }

    // Coverage is exactly 1 (shape and clip) across the span: opaque solid paint stores directly,
    // translucent solid paint blends one colour (memoized on the destination value), gradients
    // evaluate their paint per pixel.
    private static void FillInterior(in ShapeInstance inst, Span<uint> span, int firstX, float py,
        ReadOnlySpan<GradientEntry> gradients, ReadOnlySpan<GradientStopEntry> stops, uint opaque)
    {
        if (inst.PaintIndex == 0)
        {
            if (inst.A0 == 1f)
            {
                span.Fill(opaque);
                return;
            }
            float a = inst.A0 * 1f;
            if (!(a > 0f))
            {
                return;
            }
            CpuBlend.BlendConstantStraight(span, inst.R0, inst.G0, inst.B0, a);
            return;
        }
        for (int i = 0; i < span.Length; i++)
        {
            float px = firstX + i + 0.5f;
            var (r, g, b, a) = CpuShading.PaintColor(inst, px, py, gradients, stops);
            float alpha = a * 1f;
            if (!(alpha > 0f))
            {
                continue;
            }
            span[i] = CpuShading.BlendStraight(span[i], r, g, b, alpha);
        }
    }

    // Box shadows, axis-aligned: coverage is computed a row at a time (CpuShadow, vectorized), and
    // rows far enough inside the box (|y − centre| ≤ halfY − corner − 3σ) have a coverage that
    // depends on x alone, so the first such row is reused. The values are those of the per-pixel
    // function (same operations), so the image is unchanged.
    private void DrawShadow(DrawList list, CpuFramebuffer target, in ShapeInstance inst, in ClipEntry clip,
        int x0, int x1, int y0, int y1, TileScratch work)
    {
        var mask = MasksOf(list);
        var gradients = list.Gradients;
        var stops = list.GradientStops;
        var pixels = target.Pixels;
        int width = target.Width;
        bool axis = CpuSpans.IsAxisAligned(inst);
        float cy = (inst.P1 + inst.P3) * 0.5f;
        float halfY = (inst.P3 - inst.P1) * 0.5f;
        float halfX = (inst.P2 - inst.P0) * 0.5f;
        float corner = MathF.Min(inst.Q0, MathF.Min(halfX, halfY));
        float sigma = inst.Q1;
        float interior = halfY - corner - 3f * sigma;
        var rowCoverage = work.RowCoverage;
        bool cached = false;
        // Most of a shadow's pixels repeat (coverage, destination) pairs: blend once per pair run.
        float lastCov = -1f;
        uint lastDst = 0, lastOut = 0;
        bool solid = inst.PaintIndex == 0;
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            float ly = inst.FrameC * (x0 + 0.5f) + inst.FrameD * py + inst.FrameTy;
            bool reusable = axis && MathF.Abs(ly - cy) <= interior - 1e-3f;
            if (axis && !(reusable && cached))
            {
                CpuShadow.Row(inst, y, x0, rowCoverage.AsSpan(0, x1 - x0));
            }
            CpuSpans.ClipInterior(clip, y, out int ca, out int cb);
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                float shape = axis ? rowCoverage[x - x0] : CpuShading.ShapeCoverage(inst, px, py, mask);
                float cov = shape * (x >= ca && x < cb ? 1f : CpuShading.ClipCoverage(px, py, clip, mask));
                if (!(cov > 0f))
                {
                    continue;
                }
                uint dst = pixels[row + x];
                if (solid && cov == lastCov && dst == lastDst)
                {
                    pixels[row + x] = lastOut;
                    continue;
                }
                var (r, g, b, a) = CpuShading.PaintColor(inst, px, py, gradients, stops);
                uint result = CpuShading.BlendStraight(dst, r, g, b, a * cov);
                pixels[row + x] = result;
                (lastCov, lastDst, lastOut) = (cov, dst, result);
            }
            if (reusable)
            {
                cached = true;
            }
        }
    }

    // ── Text ────────────────────────────────────────────────────────────

    private void DrawGlyph(DrawList list, CpuFramebuffer target, in GlyphInstance g, Region region, TileScratch work)
    {
        if (!PixelSpan(g.PosX, g.PosX + g.SizeX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(g.PosY, g.PosY + g.SizeY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        ref readonly var clip = ref list.Clips[(int)g.ClipIndex];
        var mask = MasksOf(list);
        var page = monoAtlas.GetPage(0).Pixels!;
        int dim = monoAtlas.Dimension;
        var p = list.Parameters;
        var pixels = target.Pixels;
        int width = target.Width;
        var background = work.Background;
        float du = (g.AtlasU1 - g.AtlasU0) / g.SizeX;
        float dv = (g.AtlasV1 - g.AtlasV0) / g.SizeY;
        var dark = DarkWeights(work, p.TextGamma);
        bool fast = FastPaths;
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            int bgRow = (y - region.Y0) * TileSize - region.X0;
            float v = g.AtlasV0 + (py - g.PosY) * dv;
            int ty = Math.Clamp((int)MathF.Floor(v), 0, dim - 1);
            int ca = 0, cb = 0;
            if (fast)
            {
                CpuSpans.ClipInterior(clip, y, out ca, out cb);
            }
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                float u = g.AtlasU0 + (px - g.PosX) * du;
                int tx = Math.Clamp((int)MathF.Floor(u), 0, dim - 1);
                byte texel = page[ty * dim + tx];
                // Zero coverage blends nothing: the pixel would re-encode to itself.
                if (texel == 0 || CpuShading.Dissolved(px, py, p.Dissolve))
                {
                    continue;
                }
                float clipCov = x >= ca && x < cb ? 1f : CpuShading.ClipCoverage(px, py, clip, mask);
                if (!(clipCov > 0f))
                {
                    continue;
                }
                float cov = texel * (1f / 255f);
                float bgLum = CpuShading.PixelLuminance(background[bgRow + x]);
                float weight = fast && p.TextGamma > 0f && CpuShading.SmoothStep(-0.1f, 0.1f, g.ForegroundLuminance - bgLum) == 0f
                    ? dark[texel]
                    : CpuShading.WeightedCoverage(cov, g.ForegroundLuminance, bgLum, p.TextGamma, p.LightWeight);
                float alpha = g.A * weight * clipCov;
                pixels[row + x] = CpuShading.BlendPremultiplied(pixels[row + x], g.R * alpha, g.G * alpha, g.B * alpha, alpha);
            }
        }
    }

    // Dark-on-light text (polarity 0) weights coverage by 1 − lin(1 − pc) alone, a function of the
    // coverage byte: tabulated once per text gamma with the per-pixel function's own arithmetic.
    private static float[] DarkWeights(TileScratch work, float textGamma)
    {
        var table = work.DarkWeight;
        if (work.DarkKeyGamma == textGamma)
        {
            return table;
        }
        for (int i = 0; i < 256; i++)
        {
            float cov = i * (1f / 255f);
            // Background luminance 1 and foreground 0 select the dark branch exactly.
            table[i] = CpuShading.WeightedCoverage(cov, 0f, 1f, textGamma, 0f);
        }
        work.DarkKeyGamma = textGamma;
        return table;
    }

    private void DrawColorGlyph(DrawList list, CpuFramebuffer target, in GlyphInstance g, Region region)
    {
        if (!PixelSpan(g.PosX, g.PosX + g.SizeX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(g.PosY, g.PosY + g.SizeY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        ref readonly var clip = ref list.Clips[(int)g.ClipIndex];
        var mask = MasksOf(list);
        var page = colorAtlas.GetPage(0).Pixels!;
        int dim = colorAtlas.Dimension;
        float dissolve = list.Parameters.Dissolve;
        var pixels = target.Pixels;
        int width = target.Width;
        float du = (g.AtlasU1 - g.AtlasU0) / g.SizeX;
        float dv = (g.AtlasV1 - g.AtlasV0) / g.SizeY;
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            float v = g.AtlasV0 + (py - g.PosY) * dv;
            int ty = Math.Clamp((int)MathF.Floor(v), 0, dim - 1);
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                if (CpuShading.Dissolved(px, py, dissolve))
                {
                    continue;
                }
                float clipCov = CpuShading.ClipCoverage(px, py, clip, mask);
                if (!(clipCov > 0f))
                {
                    continue;
                }
                float u = g.AtlasU0 + (px - g.PosX) * du;
                // Nearest texel, as the GPU samples colour glyphs (quads map 1:1 onto atlas texels).
                int tx = Math.Clamp((int)MathF.Floor(u), 0, dim - 1);
                int i = ((ty * dim) + tx) * 4;
                float r = CpuShading.Decode[page[i]], gr = CpuShading.Decode[page[i + 1]], b = CpuShading.Decode[page[i + 2]];
                float a = page[i + 3] * (1f / 255f);
                float alpha = a * g.A * clipCov;
                pixels[row + x] = CpuShading.BlendPremultiplied(pixels[row + x], r * alpha, gr * alpha, b * alpha, alpha);
            }
        }
    }

    // ── Images and blur ─────────────────────────────────────────────────

    private void DrawImage(DrawList list, CpuFramebuffer target, in ImageInstance inst, Region region)
    {
        if (!PixelSpan(inst.MinX, inst.MaxX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(inst.MinY, inst.MaxY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        if (!list.Images.TryGetValue(inst.Handle, out var image))
        {
            return;
        }
        ref readonly var clip = ref list.Clips[(int)inst.ClipIndex];
        var mask = MasksOf(list);
        var pixels = target.Pixels;
        int width = target.Width;
        float gu = MathF.Sqrt(inst.Ux * inst.Ux + inst.Uy * inst.Uy);
        float gv = MathF.Sqrt(inst.Vx * inst.Vx + inst.Vy * inst.Vy);
        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                float u = inst.Ux * px + inst.Uy * py + inst.U0;
                float v = inst.Vx * px + inst.Vy * py + inst.V0;
                float edge;
                if (inst.EdgeAntialiasing != 0f)
                {
                    float du = MathF.Min(u, 1f - u) / gu;
                    float dv = MathF.Min(v, 1f - v) / gv;
                    edge = Math.Clamp(du + 0.5f, 0f, 1f) * Math.Clamp(dv + 0.5f, 0f, 1f);
                }
                else
                {
                    edge = u >= 0f && u < 1f && v >= 0f && v < 1f ? 1f : 0f;
                }
                float cov = edge * CpuShading.ClipCoverage(px, py, clip, mask) * inst.Opacity;
                if (!(cov > 0f))
                {
                    continue;
                }
                var (r, g, b, a) = CpuShading.SampleBilinearSrgb(image.Pixels, image.Width, 0, 0, image.Width, image.Height, u, v);
                pixels[row + x] = CpuShading.BlendStraight(pixels[row + x], r, g, b, a * cov);
            }
        }
    }

    private void DrawBlur(DrawList list, CpuFramebuffer target, in BlurInstance inst, Region region, uint[] background)
    {
        if (!PixelSpan(inst.MinX, inst.MaxX, region.X0, region.X1, out int x0, out int x1)
            || !PixelSpan(inst.MinY, inst.MaxY, region.Y0, region.Y1, out int y0, out int y1))
        {
            return;
        }
        ref readonly var clip = ref list.Clips[(int)inst.ClipIndex];
        var mask = MasksOf(list);
        var pixels = target.Pixels;
        int width = target.Width;
        int height = target.Height;
        float sigma = MathF.Max(inst.Sigma, 0.5f);
        float step = sigma * 0.5f;
        float invW = 1f / width;
        float invH = 1f / height;
        Span<float> weights = stackalloc float[49];
        float wsum = 0f;
        for (int j = -3; j <= 3; j++)
        {
            for (int i = -3; i <= 3; i++)
            {
                float ox = i * step, oy = j * step;
                float w = MathF.Exp(-(ox * ox + oy * oy) / (2f * sigma * sigma));
                weights[(j + 3) * 7 + i + 3] = w;
                wsum += w;
            }
        }
        float norm = 1f / MathF.Max(wsum, 0.0001f);

        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f;
            int row = y * width;
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                float dist = CpuShading.SdfRoundRect(px, py, inst.MinX, inst.MinY, inst.MaxX, inst.MaxY, inst.Radius);
                if (dist > 1f)
                {
                    continue;
                }
                float coverage = (1f - CpuShading.SmoothStep(-1f, 1f, dist)) * CpuShading.ClipCoverage(px, py, clip, mask) * inst.Opacity;
                if (!(coverage > 0f))
                {
                    continue;
                }
                float ar = 0f, ag = 0f, ab = 0f;
                for (int j = -3; j <= 3; j++)
                {
                    for (int i = -3; i <= 3; i++)
                    {
                        float w = weights[(j + 3) * 7 + i + 3];
                        var (sr, sg, sb) = CpuShading.SampleBilinearTarget(background, width, height, (px + i * step) * invW, (py + j * step) * invH);
                        ar += sr * w;
                        ag += sg * w;
                        ab += sb * w;
                    }
                }
                float br = ar * norm, bg = ag * norm, bb = ab * norm;
                float or = br + (inst.TintR - br) * inst.TintA;
                float og = bg + (inst.TintG - bg) * inst.TintA;
                float ob = bb + (inst.TintB - bb) * inst.TintA;
                pixels[row + x] = CpuShading.BlendPremultiplied(pixels[row + x], or * coverage, og * coverage, ob * coverage, coverage);
            }
        }
    }

    /// <summary>Releases the atlases, the per-frame buffers and the worker threads.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        workers.Dispose();
        monoAtlas.Dispose();
        colorAtlas.Dispose();
        maskAtlas.Dispose();
        // A disposed composer may stay referenced (a closed window's renderer): drop what it held.
        Trim();
    }
}

/// <summary>A rect of framebuffer pixels a <see cref="CpuComposer.RenderIncremental"/> frame changed.</summary>
public readonly record struct CpuDirtyRect(int X, int Y, int Width, int Height);

/// <summary>
/// Bytes a <see cref="CpuComposer"/> holds: its glyph atlases (monochrome, colour), its mask atlas
/// pages, and its per-frame buffers (bins, damage hashes, blur snapshot, worker scratch).
/// </summary>
public readonly record struct CpuMemoryUsage(long MonoAtlasBytes, long ColorAtlasBytes, long MaskAtlasBytes, long FrameBytes)
{
    /// <summary>Everything together.</summary>
    public long TotalBytes => MonoAtlasBytes + ColorAtlasBytes + MaskAtlasBytes + FrameBytes;
}
