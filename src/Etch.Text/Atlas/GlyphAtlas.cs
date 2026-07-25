// Licensed under the MIT license.
// Copyright (c) CascadeUI project authors.

namespace Etch.Text.Atlas;

using System;
using System.Buffers;
using System.Collections.Generic;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

/// <summary>
/// Multi-page glyph atlas. Each page is an independent texture + <see cref="LruCache"/>.
/// Pages manage their own LRU eviction; there is no global LRU layer.
/// </summary>
public sealed class GlyphAtlas : IDisposable
{
    private readonly Device device;
    private readonly int dim;
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
    public int Dimension => dim;

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
    /// instances are rebuilt from draw commands every frame.
    /// </summary>
    public int Generation
    {
        get { lock (_lock) { return generation; } }
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

    public GlyphAtlas(
        Device device,
        int pageDimension,
        TextureFormat format = TextureFormat.R8Unorm,
        int rowHeight = 256,
        int maxPages = 4)
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

        this.device = device;
        this.dim = pageDimension;
        this.format = format;
        this.rowHeight = rowHeight;
        this.bytesPerPixel = (format == TextureFormat.Rgba8Unorm || format == TextureFormat.Rgba8UnormSrgb) ? 4 : 1;
        this.maxPages = maxPages;

        // Pre-create the first page so GetPage(0) is always valid.
        pages.Add(new GlyphAtlasPage(device, dim, format, rowHeight, bytesPerPixel));
    }

    public bool TryInsert(GlyphCacheKey key, ReadOnlySpan<byte> bitmap, int w, int h, out AtlasRegion region, out int pageIndex, short offsetX, short offsetY)
    {
        lock (_lock)
        {
            if (TryLookup(key, out region, out pageIndex))
            {
                return true;
            }

            // Try existing pages
            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i].Cache.TryInsert(key, w, h, 0, 0, offsetX, offsetY, out region))
                {
                    pageIndex = i;
                    UploadToPage(i, region, bitmap);
                    return true;
                }
            }

            // Create a new page if under limit
            if (pages.Count < maxPages)
            {
                var page = new GlyphAtlasPage(device, dim, format, rowHeight, bytesPerPixel);
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
            bool couldFitEmptyPacker = h + 1 <= rowHeight && w + 1 <= dim;
            if (couldFitEmptyPacker)
            {
                exhausted = true;
            }

            return false;
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
        var texture = pages[pageIndex].Texture;
        var origin = new WGPUOrigin3D { X = region.U, Y = region.V, Z = 0 };
        var writeSize = new Extent3D { Width = (uint)region.W, Height = (uint)region.H, DepthOrArrayLayers = 1 };
        int rawBytesPerRow = region.W * bytesPerPixel;

        uint bytesPerRow = (uint)(((rawBytesPerRow + 255) / 256) * 256);
        if (bytesPerRow == 0)
        {
            bytesPerRow = 256;
        }

        int paddedLength = (int)(bytesPerRow * region.H);
        byte[]? rented = ArrayPool<byte>.Shared.Rent(paddedLength);
        for (int row = 0; row < region.H; row++)
        {
            int srcOffset = row * rawBytesPerRow;
            int dstOffset = row * (int)bytesPerRow;
            for (int col = 0; col < rawBytesPerRow; col++)
            {
                rented[dstOffset + col] = bitmap[srcOffset + col];
            }
            for (int col = rawBytesPerRow; col < bytesPerRow; col++)
            {
                rented[dstOffset + col] = 0;
            }
        }
        device.Queue.WriteTexture(texture, mipLevel: 0, origin, rented.AsSpan(0, paddedLength), bytesPerRow: bytesPerRow, rowsPerImage: (uint)region.H, writeSize);
        ArrayPool<byte>.Shared.Return(rented);
    }
}
