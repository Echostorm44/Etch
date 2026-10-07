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
    /// <summary>Monochrome glyph atlas page size (one page; resets when full).</summary>
    public const int GlyphAtlasSize = GpuComposer.GlyphAtlasSize;

    /// <summary>Colour glyph atlas page size (one page; resets when full).</summary>
    public const int ColorGlyphAtlasSize = GpuComposer.ColorGlyphAtlasSize;

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

    /// <summary>Creates a CPU composer with empty atlases.</summary>
    public CpuComposer()
    {
        monoAtlas = new GlyphAtlas(GlyphAtlasSize, TextureFormat.R8Unorm, 128, 1);
        colorAtlas = new GlyphAtlas(ColorGlyphAtlasSize, TextureFormat.Rgba8UnormSrgb, 128, 1);
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

    /// <summary>Clears every atlas that filled up last frame (or all, when <paramref name="force"/>). Call between frames.</summary>
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
        if (force || maskAtlas.WasExhausted)
        {
            maskAtlas.Reset();
        }
    }

    /// <summary>Renders the whole of <paramref name="list"/> (finished) into <paramref name="target"/>.</summary>
    public void Render(DrawList list, CpuFramebuffer target)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(target);
        ObjectDisposedException.ThrowIf(disposed, this);
        target.Resize((int)list.Width, (int)list.Height);
        target.Pixels.Fill(0xFF000000u);
        if (target.Width == 0 || target.Height == 0)
        {
            return;
        }

        frameList = list;
        frameTarget = target;
        Bin(list, target.Width, target.Height);

        int threads = MaxDegreeOfParallelism <= 0 ? Environment.ProcessorCount : MaxDegreeOfParallelism;
        threads = Math.Clamp(threads, 1, tilesX * tilesY);
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

    /// <summary>Releases the per-frame buffers (bins, snapshot, scratch); atlases are kept.</summary>
    public void Trim()
    {
        snapshot = Array.Empty<uint>();
        tileStart = Array.Empty<int>();
        tileFill = Array.Empty<int>();
        entries = Array.Empty<BinEntry>();
        ranges = Array.Empty<TileRange>();
        scratch = Array.Empty<TileScratch>();
    }

    /// <summary>A rectangle of target pixels [X0, X1) × [Y0, Y1).</summary>
    internal readonly record struct Region(int X0, int Y0, int X1, int Y1);

    /// <summary>A draw binned to a tile: its batch and its index in the batch kind's ordered list.</summary>
    private readonly record struct BinEntry(int Batch, int Index);

    /// <summary>The tiles a draw touches, inclusive; empty when <see cref="X1"/> &lt; <see cref="X0"/>.</summary>
    private readonly record struct TileRange(int X0, int Y0, int X1, int Y1);

    /// <summary>Per-worker scratch: the tile's background copy and lookup tables.</summary>
    private sealed class TileScratch
    {
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
        if (snapshot.Length < width * height)
        {
            snapshot = new uint[width * height];
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
        int item = 0;
        for (int b = 0; b < batches.Length; b++)
        {
            ref readonly var batch = ref batches[b];
            for (int i = 0; i < batch.Count; i++)
            {
                var range = RangeOf(list, clips, batch.Kind, batch.Start + i, width, height);
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
                var range = ranges[item++];
                for (int ty = range.Y0; ty <= range.Y1; ty++)
                {
                    for (int tx = range.X0; tx <= range.X1; tx++)
                    {
                        entries[tileFill[ty * tilesX + tx]++] = new BinEntry(b, batch.Start + i);
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

    private MaskSource MasksOf(DrawList list) => new(maskAtlas.Pages, list.MaskTiles);

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
            if (cov <= 0f)
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
            if (a <= 0f)
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
            if (alpha <= 0f)
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
                if (cov <= 0f)
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
            int ty = Math.Clamp((int)MathF.Floor(v * dim), 0, dim - 1);
            int ca = 0, cb = 0;
            if (fast)
            {
                CpuSpans.ClipInterior(clip, y, out ca, out cb);
            }
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                float u = g.AtlasU0 + (px - g.PosX) * du;
                int tx = Math.Clamp((int)MathF.Floor(u * dim), 0, dim - 1);
                byte texel = page[ty * dim + tx];
                // Zero coverage blends nothing: the pixel would re-encode to itself.
                if (texel == 0 || CpuShading.Dissolved(px, py, p.Dissolve))
                {
                    continue;
                }
                float clipCov = x >= ca && x < cb ? 1f : CpuShading.ClipCoverage(px, py, clip, mask);
                if (clipCov <= 0f)
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
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f;
                if (CpuShading.Dissolved(px, py, dissolve))
                {
                    continue;
                }
                float clipCov = CpuShading.ClipCoverage(px, py, clip, mask);
                if (clipCov <= 0f)
                {
                    continue;
                }
                float u = g.AtlasU0 + (px - g.PosX) * du;
                var (r, gr, b, a) = CpuShading.SampleBilinearSrgb(page, dim, 0, 0, dim, dim, u, v);
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
                if (cov <= 0f)
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
                if (coverage <= 0f)
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

    /// <summary>Releases the atlases and the worker items.</summary>
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
    }
}
