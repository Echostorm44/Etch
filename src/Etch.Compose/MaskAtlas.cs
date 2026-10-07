using System.Buffers;
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
/// nothing; each page is 4 MB. The atlas grows to <see cref="MaxPages"/> pages (existing masks keep
/// their place, the GPU array is copied into a larger one); only when that is full does insertion
/// fail for the rest of the frame, setting <see cref="WasExhausted"/> so the owner resets the atlas
/// between frames.
/// </para>
/// <para>
/// A mask larger than <see cref="MaxMaskSize"/> must be split by the caller: path fills are drawn
/// as tiles and clip masks are tiled (<see cref="ClipMaskTile"/>), with uniform tiles stored as a
/// value instead of texels, so masks of any size fit.
/// </para>
/// </remarks>
public sealed class MaskAtlas : IDisposable
{
    /// <summary>Page edge length in texels.</summary>
    public const int PageSize = 2048;

    /// <summary>Largest mask edge the atlas accepts.</summary>
    public const int MaxMaskSize = 1024;

    /// <summary>Most pages the atlas grows to (64 MB of masks).</summary>
    public const int MaxPages = 16;

    /// <summary>Edge of a clip-mask tile, texels (a power of two the shaders shift by).</summary>
    public const int ClipMaskTile = 256;

    private const int Padding = 1;

    private readonly Device device;
    private readonly bool gpu;
    private readonly Dictionary<ulong, MaskRegion> entries = new();
    private readonly List<Shelf> shelves = new();
    private readonly List<int> nextShelfY = new();
    private int pageCount;
    private Texture texture;
    private TextureView view;
    private int textureLayers;
    private readonly List<byte[]> pages = new();
    private bool disposed;

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

    /// <summary>Page <paramref name="layer"/>'s texels, row-major, stride <see cref="PageSize"/> (memory atlas).</summary>
    public ReadOnlySpan<byte> PagePixels(int layer) => layer < pages.Count ? pages[layer] : ReadOnlySpan<byte>.Empty;

    /// <summary>Every memory page, indexed by layer (memory atlas).</summary>
    internal IReadOnlyList<byte[]> Pages => pages;

    /// <summary>Looks up a mask by key.</summary>
    public bool TryLookup(ulong key, out MaskRegion region) => entries.TryGetValue(key, out region);

    /// <summary>
    /// Packs <paramref name="coverage"/> (row-major, stride <paramref name="width"/>) under
    /// <paramref name="key"/>. Returns false, setting <see cref="WasExhausted"/>, when every page is full.
    /// </summary>
    public bool TryInsert(ulong key, ReadOnlySpan<byte> coverage, int width, int height, out MaskRegion region)
    {
        if (entries.TryGetValue(key, out region))
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
        entries[key] = region;
        return true;
    }

    /// <summary>Forgets every mask (pages are kept, shelves restart). Call between frames.</summary>
    public void Reset()
    {
        entries.Clear();
        shelves.Clear();
        for (int i = 0; i < nextShelfY.Count; i++)
        {
            nextShelfY[i] = 0;
        }
        WasExhausted = false;
        ContentGeneration++;
    }

    /// <summary>Forgets every mask and releases the pages (memory and GPU texture).</summary>
    public void Trim()
    {
        Reset();
        pages.Clear();
        nextShelfY.Clear();
        pageCount = 0;
        ReleaseTexture();
        textureLayers = 0;
        TextureGeneration++;
    }

    private bool TryAllocate(int width, int height, out int u, out int v, out int layer)
    {
        int w = width + Padding;
        int h = height + Padding;
        // The shortest shelf tall enough with room left, on any page.
        int best = -1;
        for (int i = 0; i < shelves.Count; i++)
        {
            var shelf = shelves[i];
            if (shelf.Height >= h && shelf.NextX + w <= PageSize && (best < 0 || shelf.Height < shelves[best].Height))
            {
                best = i;
            }
        }
        if (best < 0)
        {
            // A new shelf, rounded up to a multiple of 8 so similar masks share it, on the first
            // page with room; a new page when none has.
            int shelfHeight = (h + 7) & ~7;
            int page = -1;
            for (int p = 0; p < pageCount; p++)
            {
                if (nextShelfY[p] + shelfHeight <= PageSize)
                {
                    page = p;
                    break;
                }
            }
            if (page < 0)
            {
                if (pageCount >= MaxPages)
                {
                    u = v = layer = 0;
                    return false;
                }
                page = AddPage();
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

    private int AddPage()
    {
        int page = pageCount++;
        if (page < nextShelfY.Count)
        {
            nextShelfY[page] = 0;
        }
        else
        {
            nextShelfY.Add(0);
        }
        if (!gpu)
        {
            if (page >= pages.Count)
            {
                pages.Add(new byte[PageSize * PageSize]);
            }
            return page;
        }
        if (pageCount > textureLayers)
        {
            GrowTexture(Math.Min(MaxPages, Math.Max(1, textureLayers * 2)));
        }
        return page;
    }

    // Replaces the texture array with a larger one, copying the existing layers on the GPU.
    private unsafe void GrowTexture(int layers)
    {
        var grown = device.CreateTexture(new TextureDescriptor
        {
            Size = new Extent3D { Width = PageSize, Height = PageSize, DepthOrArrayLayers = (uint)layers },
            Format = TextureFormat.R8Unorm,
            Dimension = TextureDimension.D2,
            Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst | TextureUsage.CopySrc),
            MipLevelCount = 1,
            SampleCount = 1,
        });
        if (textureLayers > 0)
        {
            using var encoder = device.CreateCommandEncoder();
            encoder.CopyTextureToTexture(texture, 0, default, grown, 0, default,
                new Extent3D { Width = PageSize, Height = PageSize, DepthOrArrayLayers = (uint)textureLayers });
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
                    .CopyTo(dst.Slice((region.V + row) * PageSize + region.U, region.Width));
            }
            return;
        }

        uint bytesPerRow = (uint)((region.Width + 255) & ~255);
        int length = (int)bytesPerRow * region.Height;
        byte[] rented = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            var staging = rented.AsSpan(0, length);
            for (int row = 0; row < region.Height; row++)
            {
                coverage.Slice(row * region.Width, region.Width).CopyTo(staging.Slice(row * (int)bytesPerRow, region.Width));
            }
            var origin = new WGPUOrigin3D { X = (uint)region.U, Y = (uint)region.V, Z = (uint)region.Layer };
            var size = new Extent3D { Width = (uint)region.Width, Height = (uint)region.Height, DepthOrArrayLayers = 1 };
            device.Queue.WriteTexture(texture, 0, origin, staging, bytesPerRow, (uint)region.Height, size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Releases the GPU pages.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ReleaseTexture();
    }
}
