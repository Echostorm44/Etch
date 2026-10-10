// Licensed under the MIT license.
// Copyright (c) CascadeUI project authors.

namespace Etch.Text.Atlas;

using System;
using System.Collections.Generic;
using Etch.Gpu;
using Etch.Gpu.Descriptors;

/// <summary>
/// Multi-page glyph atlas. Each page is an independent texture (or, for the CPU composer, a block
/// of memory) + <see cref="LruCache"/>. Pages manage their own LRU eviction; there is no global LRU
/// layer. Both kinds pack identically, so a glyph lands at the same texels either way.
/// </summary>
/// <remarks>
/// <para>
/// Pages start at <see cref="InitialDimension"/> and double, up to <see cref="MaxDimension"/>, when a
/// glyph does not fit: a small UI holds a small atlas. Growing keeps every glyph at its texel
/// coordinates (the old page is the top-left of the new one), so it may happen mid-frame; texel
/// coordinates rather than normalized UVs are what callers must keep. Only at
/// <see cref="MaxDimension"/> are older glyphs evicted, more pages added (up to <c>maxPages</c>), and
/// finally insertion refused (<see cref="WasExhausted"/>). <see cref="Trim"/> returns to the initial size.
/// </para>
/// <para>
/// A GPU atlas queues each new glyph's bitmap and writes the frame's glyphs together in
/// <see cref="FlushUploads"/> (one <see cref="TextureUploadBatch"/> per page) rather than one
/// texture write per glyph, each of which staged its bytes in an upload buffer of its own.
/// </para>
/// </remarks>
public sealed class GlyphAtlas : IDisposable
{
    private readonly Device device;
    private readonly bool gpu;
    private int dim;
    private readonly int initialDim;
    private readonly int maxDim;
    private readonly TextureFormat format;
    private readonly int rowHeight;
    private readonly int bytesPerPixel;
    private readonly int maxPages;

    private readonly List<GlyphAtlasPage> pages = new();
    private bool disposed;
    private readonly object _lock = new();

    // WP-3509: set when an insert fails because every page's packer is full
    // (recoverable by Reset), NOT when a glyph is simply too tall for a shelf
    // (which a reset cannot help). The presenter polls this between frames.
    private bool exhausted;
    private int generation;
    private int textureGeneration;

    /// <summary>Number of active pages.</summary>
    public int PageCount => pages.Count;

    /// <summary>Total glyph count across all pages.</summary>
    public int GlyphCount
    {
        get
        {
            int count = 0;
            foreach (var page in pages)
            {
                count += page.Cache.Count;
            }
            return count;
        }
    }

    public TextureFormat Format => format;

    /// <summary>The pages' current edge length in texels (every page has the same).</summary>
    public int Dimension => dim;

    /// <summary>The edge length pages start at (and return to on <see cref="Trim"/>).</summary>
    public int InitialDimension => initialDim;

    /// <summary>The edge length pages grow to at most.</summary>
    public int MaxDimension => maxDim;

    /// <summary>Bytes the pages' texels occupy (GPU textures or memory).</summary>
    public long ResidentBytes
    {
        get
        {
            long bytes = 0;
            foreach (var page in pages)
            {
                bytes += page.Bytes;
            }
            return bytes;
        }
    }

    /// <summary>
    /// True when, since the last <see cref="Reset"/>, an insert was refused
    /// because the packer was full of other glyphs (recoverable). A glyph
    /// refused only for being taller than a shelf does NOT set this — resetting
    /// would not make room for it. The presenter checks this between frames and
    /// resets, so a churn-exhausted atlas recovers on the next frame instead of
    /// silently dropping glyphs forever (WP-3509).
    /// </summary>
    public bool WasExhausted
    {
        get { lock (_lock) { return exhausted; } }
    }

    /// <summary>
    /// Increments on every <see cref="Reset"/>. Lets tests observe that a reset
    /// actually happened; not otherwise needed for correctness because glyph
    /// instances are rebuilt from draw commands every frame. Growing the pages does
    /// not change it: every glyph keeps its texels.
    /// </summary>
    public int Generation
    {
        get { lock (_lock) { return generation; } }
    }

    /// <summary>
    /// Changes whenever a page's texture (and view) is replaced — it grew, or <see cref="Trim"/>
    /// shrank it — so bind groups referencing the old view must be rebuilt.
    /// </summary>
    public int TextureGeneration
    {
        get { lock (_lock) { return textureGeneration; } }
    }

    /// <summary>
    /// Clears every page's cache and shelf packer (textures retained), bumps
    /// <see cref="Generation"/>, and clears <see cref="WasExhausted"/>. Must be
    /// called between frames — never mid-build — so a frame's atlas uploads
    /// stay coherent. Resets all pages together: a partial reset would leave
    /// the multi-page lookup inconsistent, and after a reset every visible
    /// glyph re-rasterizes into whichever page now has room.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            foreach (var page in pages)
            {
                page.Cache.Reset();
            }
            exhausted = false;
            generation++;
        }
    }

    /// <summary>
    /// Forgets every glyph and returns to one page of <see cref="InitialDimension"/>, releasing the
    /// rest of the memory (the window is hidden, or the atlas outgrew what is drawn). Like
    /// <see cref="Reset"/>, call between frames.
    /// </summary>
    public void Trim()
    {
        lock (_lock)
        {
            if (gpu && (pages.Count > 1 || (pages.Count == 1 && pages[0].Dimension != initialDim)))
            {
                textureGeneration++;
            }
            for (int i = pages.Count - 1; i >= 1; i--)
            {
                pages[i].Dispose();
                pages.RemoveAt(i);
            }
            dim = initialDim;
            if (pages.Count == 1)
            {
                pages[0].Recreate(initialDim);
            }
            exhausted = false;
            generation++;
        }
    }

    /// <summary>
    /// Starts a frame: glyphs the frame uses are protected from LRU eviction until the next call
    /// (see <see cref="LruCache.BeginFrame"/>). Call between frames, after any <see cref="Reset"/>.
    /// </summary>
    public void BeginFrame()
    {
        lock (_lock)
        {
            foreach (var page in pages)
            {
                page.Cache.BeginFrame();
            }
        }
    }

    /// <summary>
    /// Creates a GPU atlas of pages up to <paramref name="pageDimension"/>² texels, starting at
    /// <paramref name="initialDimension"/>² (0: start at full size).
    /// </summary>
    public GlyphAtlas(
        Device device,
        int pageDimension,
        TextureFormat format = TextureFormat.R8Unorm,
        int rowHeight = 256,
        int maxPages = 4,
        int initialDimension = 0)
    {
        Validate(pageDimension, format, rowHeight, ref initialDimension);
        this.device = device;
        this.gpu = true;
        this.maxDim = pageDimension;
        this.initialDim = initialDimension;
        this.dim = initialDimension;
        this.format = format;
        this.rowHeight = rowHeight;
        this.bytesPerPixel = (format == TextureFormat.Rgba8Unorm || format == TextureFormat.Rgba8UnormSrgb) ? 4 : 1;
        this.maxPages = maxPages;

        // Pre-create the first page so GetPage(0) is always valid.
        pages.Add(CreatePage());
    }

    /// <summary>
    /// Creates a memory-backed atlas (no GPU): pages are byte arrays the CPU composer samples.
    /// <paramref name="format"/> selects 1 (R8) or 4 (RGBA8) bytes per texel. Pages grow from
    /// <paramref name="initialDimension"/>² (0: start at full size) to <paramref name="pageDimension"/>².
    /// </summary>
    public GlyphAtlas(
        int pageDimension,
        TextureFormat format,
        int rowHeight,
        int maxPages,
        int initialDimension = 0)
    {
        Validate(pageDimension, format, rowHeight, ref initialDimension);
        this.device = default;
        this.gpu = false;
        this.maxDim = pageDimension;
        this.initialDim = initialDimension;
        this.dim = initialDimension;
        this.format = format;
        this.rowHeight = rowHeight;
        this.bytesPerPixel = (format == TextureFormat.Rgba8Unorm || format == TextureFormat.Rgba8UnormSrgb) ? 4 : 1;
        this.maxPages = maxPages;
        pages.Add(CreatePage());
    }

    private static void Validate(int pageDimension, TextureFormat format, int rowHeight, ref int initialDimension)
    {
        if (pageDimension is not 512 and not 1024 and not 2048 and not 4096)
        {
            Panic.Invariant(PanicCodes.InvalidAtlasDimension, $"Atlas dimension must be 512, 1024, 2048 or 4096, got {pageDimension}");
        }

        if (format != TextureFormat.R8Unorm && format != TextureFormat.Rgba8Unorm && format != TextureFormat.Rgba8UnormSrgb)
        {
            Panic.Invariant(PanicCodes.InvalidAtlasDimension, $"Atlas format must be R8Unorm, Rgba8Unorm, or Rgba8UnormSrgb, got {format}");
        }

        if (rowHeight <= 0 || rowHeight > pageDimension)
        {
            Panic.Invariant(PanicCodes.InvalidAtlasDimension, $"Row height must be > 0 and <= dim, got {rowHeight}");
        }

        if (initialDimension == 0)
        {
            initialDimension = pageDimension;
        }
        if (initialDimension < 64 || initialDimension > pageDimension || (initialDimension & (initialDimension - 1)) != 0)
        {
            Panic.Invariant(PanicCodes.InvalidAtlasDimension, $"Initial atlas dimension must be a power of two in 64..{pageDimension}, got {initialDimension}");
        }
    }

    /// <summary>
    /// True when a <paramref name="width"/> × <paramref name="height"/> bitmap fits an empty page's
    /// shelf (padding included) once the page has grown to <see cref="MaxDimension"/>. A larger
    /// glyph can never be cached and must be drawn another way.
    /// </summary>
    public bool CanEverHold(int width, int height) => height + 1 <= rowHeight && width + 1 <= maxDim;

    /// <summary>Bytes per texel of the pages (1 for R8, 4 for RGBA8).</summary>
    public int BytesPerPixel => bytesPerPixel;

    private GlyphAtlasPage CreatePage()
        => gpu ? new GlyphAtlasPage(device, dim, format, rowHeight, bytesPerPixel) : new GlyphAtlasPage(dim, rowHeight, bytesPerPixel);

    /// <summary>
    /// Places <paramref name="bitmap"/> (<paramref name="w"/> × <paramref name="h"/>, row-major) under
    /// <paramref name="key"/>, growing the pages first and evicting glyphs no recent frame used only
    /// at full size. The region is in texels of page <paramref name="pageIndex"/>.
    /// </summary>
    public bool TryInsert(GlyphCacheKey key, ReadOnlySpan<byte> bitmap, int w, int h, out AtlasRegion region, out int pageIndex, short offsetX, short offsetY)
    {
        lock (_lock)
        {
            if (TryLookup(key, out region, out pageIndex))
            {
                return true;
            }

            // Below full size the page grows rather than evict: eviction is for a full atlas.
            bool canEverFit = CanEverHold(w, h);
            while (true)
            {
                bool evict = dim == maxDim;
                for (int i = 0; i < pages.Count; i++)
                {
                    if (pages[i].Cache.TryInsert(key, w, h, offsetX, offsetY, evict, out region))
                    {
                        pageIndex = i;
                        UploadToPage(i, region, bitmap);
                        return true;
                    }
                }
                if (!canEverFit || dim >= maxDim)
                {
                    break;
                }
                Grow(Math.Min(maxDim, dim * 2));
            }

            // Create a new page if under limit
            if (canEverFit && pages.Count < maxPages)
            {
                var page = CreatePage();
                pages.Add(page);
                int newPageIndex = pages.Count - 1;
                if (page.Cache.TryInsert(key, w, h, 0, 0, offsetX, offsetY, out region))
                {
                    pageIndex = newPageIndex;
                    UploadToPage(newPageIndex, region, bitmap);
                    return true;
                }
            }

            region = default;
            pageIndex = -1;

            // Signal exhaustion only for a glyph that could fit an empty packer
            // — one that fits both a shelf's height and the page width (each
            // padded by 1, matching ShelfPack). A glyph too tall or too wide to
            // ever be placed must NOT flag exhaustion, or the presenter would
            // reset every frame forever (WP-3509). Such oversized glyphs are a
            // separate concern (large-glyph path, text program).
            if (canEverFit)
            {
                exhausted = true;
            }

            return false;
        }
    }

    // Grows every page (more than one exists only at full size, so in practice page 0).
    private void Grow(int dimension)
    {
        foreach (var page in pages)
        {
            page.Grow(dimension);
        }
        dim = dimension;
        if (gpu)
        {
            textureGeneration++;
        }
    }

    public bool TryLookup(GlyphCacheKey key, out AtlasRegion region, out int pageIndex)
    {
        lock (_lock)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i].Cache.TryLookup(key, out region))
                {
                    pageIndex = i;
                    return true;
                }
            }
            region = default;
            pageIndex = -1;
            return false;
        }
    }

    public GlyphAtlasPage GetPage(int pageIndex)
    {
        return pages[pageIndex];
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        foreach (var page in pages)
        {
            page.Dispose();
        }
        pages.Clear();
    }

    private void UploadToPage(int pageIndex, AtlasRegion region, ReadOnlySpan<byte> bitmap)
    {
        if (!gpu)
        {
            var pixels = pages[pageIndex].Pixels!;
            int rowBytes = region.W * bytesPerPixel;
            for (int row = 0; row < region.H; row++)
            {
                bitmap.Slice(row * rowBytes, rowBytes).CopyTo(pixels.AsSpan(((region.V + row) * dim + region.U) * bytesPerPixel, rowBytes));
            }
            return;
        }

        // GPU pages queue the bitmap; FlushUploads writes every queued glyph in one batch.
        pages[pageIndex].QueueUpload(region, bitmap);
    }

    /// <summary>
    /// Records the glyph uploads queued since the last call into <paramref name="encoder"/> (GPU
    /// atlases; a memory atlas writes its texels on insertion). Call before the frame that draws
    /// the glyphs is submitted; <c>GpuComposer.Encode</c> does.
    /// </summary>
    public void FlushUploads(CommandEncoder encoder)
    {
        lock (_lock)
        {
            foreach (var page in pages)
            {
                page.FlushUploads(encoder);
            }
        }
    }

    /// <summary>Glyph uploads waiting for <see cref="FlushUploads"/>, across pages (GPU atlases).</summary>
    public int PendingUploads
    {
        get
        {
            lock (_lock)
            {
                int count = 0;
                foreach (var page in pages)
                {
                    count += page.PendingUploads;
                }
                return count;
            }
        }
    }
}
