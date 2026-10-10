using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Compose;

/// <summary>Where a coverage mask lives: its texel rect in an atlas page (layer).</summary>
public readonly record struct MaskRegion(int U, int V, int Layer, int Width, int Height);

/// <summary>
/// 8-bit coverage masks (rasterized path fills, strokes and clip shapes) packed into square pages,
/// keyed by a 64-bit content key. The pages are layers of one GPU texture array when the atlas is
/// created with a device and plain memory otherwise; both composers read the same texels.
/// </summary>
/// <remarks>
/// <para>
/// Pages are created on demand: a UI that never fills an arbitrary path or clips to one pays
/// nothing. The first page starts at <see cref="InitialPageSize"/>² texels (64 KB) and doubles, keeping
/// every mask where it is, up to <see cref="PageSize"/>² (4 MB) as masks arrive; only then are more
/// pages added. Within a frame the atlas grows to <see cref="MaxPages"/> pages
/// (existing masks keep their place, the GPU array is copied into a larger one); only when that is
/// full does insertion fail for the rest of the frame, setting <see cref="WasExhausted"/>.
/// </para>
/// <para>
/// Between frames <see cref="EndFrame"/> keeps the atlas bounded: masks no frame has used recently
/// are evicted when they outweigh the masks the last frame used, and the pages shrink back to
/// <see cref="BudgetPages"/> unless a frame needs more. Masks are keyed relative to the pixel they
/// are anchored at, so scrolled content reuses its masks rather than adding new ones.
/// <see cref="Trim"/> releases everything (the window is hidden).
/// </para>
/// <para>
/// A mask larger than <see cref="MaxMaskSize"/> must be split by the caller: path fills are drawn
/// as tiles and clip masks are tiled (<see cref="ClipMaskTile"/>), with uniform tiles stored as a
/// value instead of texels, so masks of any size fit.
/// </para>
/// </remarks>
public sealed class MaskAtlas : IDisposable
{
    /// <summary>Page edge length in texels at its largest (every page beyond the first has this size).</summary>
    public const int PageSize = 2048;

    /// <summary>Edge length in texels the first page starts at (it doubles up to <see cref="PageSize"/> as masks arrive).</summary>
    public const int InitialPageSize = 256;

    /// <summary>Largest mask edge the atlas accepts.</summary>
    public const int MaxMaskSize = 1024;

    /// <summary>Most pages the atlas grows to within one frame (64 MB of masks).</summary>
    public const int MaxPages = 16;

    /// <summary>Pages the atlas keeps between frames unless the frames need more (8 MB).</summary>
    public const int BudgetPages = 2;

    private const long PageArea = (long)PageSize * PageSize;

    /// <summary>Edge of a clip-mask tile, texels (a power of two the shaders shift by).</summary>
    public const int ClipMaskTile = 256;

    private const int Padding = 1;

    private readonly Device device;
    private readonly bool gpu;
    private readonly TextureUploadBatch? uploads;
    private readonly Dictionary<ulong, Entry> entries = new();
    private readonly List<Shelf> shelves = new();
    private readonly List<int> nextShelfY = new();
    private int pageCount;
    private Texture texture;
    private TextureView view;
    private int textureLayers;
    private int textureDimension;
    private int pageDimension = InitialPageSize;
    private readonly List<byte[]> pages = new();
    private bool disposed;

    private struct Entry
    {
        public MaskRegion Region;
        public long LastUsed;
    }

    // Frame bookkeeping for eviction: texels allocated since the last reset, and texels the
    // current frame has used (each mask counted once per frame).
    private long frame;
    private long allocatedArea;
    private long frameArea;
    private long lastFrameArea;

    private struct Shelf
    {
        public int Layer;
        public int Y;
        public int Height;
        public int NextX;
    }

    /// <summary>Creates a GPU-backed atlas on <paramref name="device"/>.</summary>
    public MaskAtlas(Device device)
    {
        this.device = device;
        gpu = true;
        uploads = new TextureUploadBatch(device);
    }

    /// <summary>Creates a memory-backed atlas for the CPU composer.</summary>
    public MaskAtlas()
    {
        device = default;
        gpu = false;
    }

    /// <summary>True when an insertion failed for lack of space since the last <see cref="Reset"/>.</summary>
    public bool WasExhausted { get; private set; }

    /// <summary>Number of masks held.</summary>
    public int Count => entries.Count;

    /// <summary>True once a page exists (after the first insertion).</summary>
    public bool HasPage => pageCount > 0;

    /// <summary>Pages in use.</summary>
    public int PageCount => pageCount;

    /// <summary>Changes whenever the GPU texture array is replaced (bind groups must be rebuilt).</summary>
    public int TextureGeneration { get; private set; }

    /// <summary>Changes whenever the atlas forgets its masks (<see cref="Reset"/>, <see cref="Trim"/>).</summary>
    public int ContentGeneration { get; private set; }

    /// <summary>The page array's view, dimension <c>D2Array</c> (GPU atlas, after the first insertion).</summary>
    public TextureView PageView => view;

    /// <summary>Layers of the GPU page texture (0 without a device, or before the first mask).</summary>
    public int TextureLayers => textureLayers;

    /// <summary>
    /// The pages' current edge length in texels: <see cref="InitialPageSize"/> up to <see cref="PageSize"/>
    /// (every page has the same; a second page exists only at <see cref="PageSize"/>).
    /// </summary>
    public int PageDimension => pageDimension;

    /// <summary>Bytes the pages occupy (GPU texture array or memory).</summary>
    public long ResidentBytes => gpu
        ? (long)textureDimension * textureDimension * textureLayers
        : (long)pageDimension * pageDimension * pages.Count;

    /// <summary>Page <paramref name="layer"/>'s texels, row-major, stride <see cref="PageDimension"/> (memory atlas).</summary>
    public ReadOnlySpan<byte> PagePixels(int layer) => layer < pages.Count ? pages[layer] : ReadOnlySpan<byte>.Empty;

    /// <summary>Every memory page, indexed by layer (memory atlas).</summary>
    internal IReadOnlyList<byte[]> Pages => pages;

    /// <summary>Looks up a mask by key.</summary>
    public bool TryLookup(ulong key, out MaskRegion region)
    {
        ref var entry = ref CollectionsMarshal.GetValueRefOrNullRef(entries, key);
        if (Unsafe.IsNullRef(ref entry))
        {
            region = default;
            return false;
        }
        Touch(ref entry);
        region = entry.Region;
        return true;
    }

    private void Touch(ref Entry entry)
    {
        if (entry.LastUsed != frame)
        {
            entry.LastUsed = frame;
            frameArea += (long)entry.Region.Width * entry.Region.Height;
        }
    }

    /// <summary>
    /// Packs <paramref name="coverage"/> (row-major, stride <paramref name="width"/>) under
    /// <paramref name="key"/>. Returns false, setting <see cref="WasExhausted"/>, when every page is full.
    /// </summary>
    public bool TryInsert(ulong key, ReadOnlySpan<byte> coverage, int width, int height, out MaskRegion region)
    {
        if (TryLookup(key, out region))
        {
            return true;
        }
        if (width <= 0 || height <= 0 || width > MaxMaskSize || height > MaxMaskSize)
        {
            Panic.Invariant(PanicCodes.ArgumentOutOfRange, $"Mask size {width}x{height} outside 1..{MaxMaskSize}");
        }

        if (!TryAllocate(width, height, out int u, out int v, out int layer))
        {
            WasExhausted = true;
            region = default;
            return false;
        }

        region = new MaskRegion(u, v, layer, width, height);
        Upload(region, coverage);
        long area = (long)width * height;
        entries[key] = new Entry { Region = region, LastUsed = frame };
        allocatedArea += area;
        frameArea += area;
        return true;
    }

    /// <summary>
    /// Ends a frame (call between frames, before the next frame's draw list is built): resets the
    /// atlas when it filled up, or when masks the frame did not use outweigh the ones it did
    /// (eviction), and shrinks it back to <see cref="BudgetPages"/> when the last frame fits there.
    /// <paramref name="force"/> resets regardless.
    /// </summary>
    public void EndFrame(bool force = false)
    {
        lastFrameArea = frameArea;
        frameArea = 0;
        frame++;
        bool stale = allocatedArea > PageArea / 2 && allocatedArea > 2 * lastFrameArea;
        if (force || WasExhausted || stale)
        {
            Reset();
        }
        if (pageCount > BudgetPages && lastFrameArea * 2 <= BudgetPages * PageArea)
        {
            // The pages a peak frame needed are no longer needed: release them; a page comes back
            // when a mask is next inserted.
            Trim();
        }
    }

    /// <summary>Texels the last ended frame used.</summary>
    public long LastFrameArea => lastFrameArea;

    /// <summary>Forgets every mask (pages are kept, shelves restart). Call between frames.</summary>
    public void Reset()
    {
        entries.Clear();
        allocatedArea = 0;
        shelves.Clear();
        for (int i = 0; i < nextShelfY.Count; i++)
        {
            nextShelfY[i] = 0;
        }
        WasExhausted = false;
        ContentGeneration++;
    }

    /// <summary>
    /// Forgets every mask and releases the pages (memory and GPU texture); the next mask starts a
    /// page of <see cref="InitialPageSize"/> again.
    /// </summary>
    public void Trim()
    {
        Reset();
        pages.Clear();
        nextShelfY.Clear();
        pageCount = 0;
        pageDimension = InitialPageSize;
        uploads?.Clear();
        ReleaseTexture();
        textureLayers = 0;
        textureDimension = 0;
        TextureGeneration++;
    }

    private bool TryAllocate(int width, int height, out int u, out int v, out int layer)
    {
        int w = width + Padding;
        int h = height + Padding;
        while (true)
        {
            // The shortest shelf tall enough with room left, on any page.
            int best = -1;
            for (int i = 0; i < shelves.Count; i++)
            {
                var shelf = shelves[i];
                if (shelf.Height >= h && shelf.NextX + w <= pageDimension && (best < 0 || shelf.Height < shelves[best].Height))
                {
                    best = i;
                }
            }
            if (best < 0)
            {
                // A new shelf, rounded up to a multiple of 8 so similar masks share it, on the first
                // page with room.
                int shelfHeight = (h + 7) & ~7;
                int page = -1;
                for (int p = 0; p < pageCount && w <= pageDimension; p++)
                {
                    if (nextShelfY[p] + shelfHeight <= pageDimension)
                    {
                        page = p;
                        break;
                    }
                }
                if (page < 0)
                {
                    // No room: the first page doubles (masks keep their texels) until it is full
                    // size; only then are pages added.
                    if (pageCount == 0)
                    {
                        AddPage();
                    }
                    else if (pageDimension < PageSize)
                    {
                        GrowPages(pageDimension * 2);
                    }
                    else if (pageCount < MaxPages)
                    {
                        AddPage();
                    }
                    else
                    {
                        u = v = layer = 0;
                        return false;
                    }
                    continue;
                }
                shelves.Add(new Shelf { Layer = page, Y = nextShelfY[page], Height = shelfHeight, NextX = 0 });
                nextShelfY[page] += shelfHeight;
                best = shelves.Count - 1;
            }

            var chosen = shelves[best];
            u = chosen.NextX;
            v = chosen.Y;
            layer = chosen.Layer;
            chosen.NextX += w;
            shelves[best] = chosen;
            return true;
        }
    }

    private void AddPage()
    {
        pageCount++;
        nextShelfY.Add(0);
        if (!gpu)
        {
            pages.Add(new byte[pageDimension * pageDimension]);
            return;
        }
        if (pageCount > textureLayers || textureDimension != pageDimension)
        {
            EnsureTexture(pageDimension, Math.Min(MaxPages, Math.Max(pageCount, textureLayers * 2)));
        }
    }

    // The single page grows to dimension² texels; every mask keeps its texel coordinates.
    private void GrowPages(int dimension)
    {
        int old = pageDimension;
        pageDimension = dimension;
        if (!gpu)
        {
            for (int p = 0; p < pages.Count; p++)
            {
                var grown = new byte[dimension * dimension];
                for (int row = 0; row < old; row++)
                {
                    pages[p].AsSpan(row * old, old).CopyTo(grown.AsSpan(row * dimension, old));
                }
                pages[p] = grown;
            }
            return;
        }
        EnsureTexture(dimension, Math.Max(textureLayers, 1));
    }

    // Replaces the texture array with one of the given size, copying the existing layers on the GPU.
    private unsafe void EnsureTexture(int dimension, int layers)
    {
        var grown = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)dimension, Height = (uint)dimension, DepthOrArrayLayers = (uint)layers },
            Format = TextureFormat.R8Unorm,
            Dimension = TextureDimension.D2,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc),
            MipLevelCount = 1,
            SampleCount = 1,
        });
        if (textureLayers > 0)
        {
            // Uploads queued for the old texture run before this submission's copy.
            int copyDimension = Math.Min(textureDimension, dimension);
            using var encoder = device.CreateCommandEncoder();
            encoder.CopyTextureToTexture(texture, 0, default, grown, 0, default,
                new Extent3D { Width = (uint)copyDimension, Height = (uint)copyDimension, DepthOrArrayLayers = (uint)Math.Min(textureLayers, layers) });
            using var commands = encoder.Finish();
            Span<CommandBuffer> submit = stackalloc CommandBuffer[1];
            submit[0] = commands;
            device.Queue.Submit(submit);
        }
        ReleaseTexture();
        texture = grown;
        view = texture.CreateView(new TextureViewDescriptor
        {
            Format = TextureFormat.R8Unorm,
            Dimension = TextureViewDimension.D2Array,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = (uint)layers,
            Aspect = TextureAspect.All,
        });
        textureLayers = layers;
        textureDimension = dimension;
        TextureGeneration++;
    }

    private void ReleaseTexture()
    {
        if (!view.IsInvalid)
        {
            view.Dispose();
        }
        if (!texture.IsInvalid)
        {
            texture.Dispose();
        }
        view = default;
        texture = default;
    }

    private void Upload(MaskRegion region, ReadOnlySpan<byte> coverage)
    {
        if (!gpu)
        {
            var dst = pages[region.Layer].AsSpan();
            for (int row = 0; row < region.Height; row++)
            {
                coverage.Slice(row * region.Width, region.Width)
                    .CopyTo(dst.Slice((region.V + row) * pageDimension + region.U, region.Width));
            }
            return;
        }

        // Queued: FlushUploads writes every mask of the frame in one batch.
        uploads!.Add((uint)region.U, (uint)region.V, (uint)region.Layer, region.Width, region.Height, 1, coverage);
    }

    /// <summary>
    /// Records the mask uploads queued since the last call into <paramref name="encoder"/> (GPU
    /// atlas; the memory atlas writes its texels on insertion). <see cref="GpuComposer.Encode"/> calls it.
    /// </summary>
    public void FlushUploads(CommandEncoder encoder)
    {
        if (uploads is null || texture.IsInvalid)
        {
            return;
        }
        uploads.Flush(encoder, texture);
    }

    /// <summary>Mask uploads waiting for <see cref="FlushUploads"/> (GPU atlas).</summary>
    public int PendingUploads => uploads?.PendingCount ?? 0;

    /// <summary>Releases the pages (GPU texture array, or memory).</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        uploads?.Dispose();
        ReleaseTexture();
        pages.Clear();
    }
}
