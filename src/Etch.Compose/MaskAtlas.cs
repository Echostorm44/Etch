using System.Buffers;
using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Compose;

/// <summary>Where a coverage mask lives: its texel rect in the atlas page.</summary>
public readonly record struct MaskRegion(int U, int V, int Width, int Height);

/// <summary>
/// 8-bit coverage masks (rasterized path fills, strokes and clip shapes) packed into one square
/// page, keyed by a 64-bit content key. The page is a GPU texture when the atlas is created with a
/// device and plain memory otherwise; both composers read the same texels.
/// </summary>
/// <remarks>
/// <para>
/// The page is created on the first insertion: a UI that never fills an arbitrary path or clips to
/// one pays nothing (4 MB otherwise).
/// </para>
/// <para>
/// Masks are packed on shelves. When the page is full, insertion fails for the rest of the frame and
/// <see cref="WasExhausted"/> is set; the owner resets the atlas between frames and the next frame
/// re-rasterizes what it needs, as the glyph atlas does. A mask larger than
/// <see cref="MaxMaskSize"/> must be split by the caller.
/// </para>
/// </remarks>
public sealed class MaskAtlas : IDisposable
{
    /// <summary>Page edge length in texels.</summary>
    public const int PageSize = 2048;

    /// <summary>Largest mask edge the atlas accepts.</summary>
    public const int MaxMaskSize = 1024;

    private const int Padding = 1;

    private readonly Device device;
    private readonly bool gpu;
    private readonly Dictionary<ulong, MaskRegion> entries = new();
    private readonly List<Shelf> shelves = new();
    private int nextShelfY;
    private Texture texture;
    private TextureView view;
    private byte[]? pixels;
    private bool disposed;

    private struct Shelf
    {
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

    /// <summary>True once the page exists (after the first insertion).</summary>
    public bool HasPage => gpu ? !texture.IsInvalid : pixels is not null;

    /// <summary>The page's texture view (GPU atlas, after the first insertion).</summary>
    public TextureView PageView => view;

    /// <summary>The page's texels, row-major, stride <see cref="PageSize"/> (memory atlas, after the first insertion).</summary>
    public ReadOnlySpan<byte> PagePixels => pixels;

    /// <summary>Looks up a mask by key.</summary>
    public bool TryLookup(ulong key, out MaskRegion region) => entries.TryGetValue(key, out region);

    /// <summary>
    /// Packs <paramref name="coverage"/> (row-major, stride <paramref name="width"/>) under
    /// <paramref name="key"/>. Returns false, setting <see cref="WasExhausted"/>, when the page is full.
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

        EnsurePage();
        if (!TryAllocate(width, height, out int u, out int v))
        {
            WasExhausted = true;
            region = default;
            return false;
        }

        region = new MaskRegion(u, v, width, height);
        Upload(region, coverage);
        entries[key] = region;
        return true;
    }

    /// <summary>Forgets every mask (the page is kept, shelves restart). Call between frames.</summary>
    public void Reset()
    {
        entries.Clear();
        shelves.Clear();
        nextShelfY = 0;
        WasExhausted = false;
    }

    private void EnsurePage()
    {
        if (HasPage)
        {
            return;
        }
        if (gpu)
        {
            texture = device.CreateTexture(new TextureDescriptor
            {
                Size = new Extent3D { Width = PageSize, Height = PageSize, DepthOrArrayLayers = 1 },
                Format = TextureFormat.R8Unorm,
                Dimension = TextureDimension.D2,
                Usage = (ulong)(TextureUsage.TextureBinding | TextureUsage.CopyDst),
                MipLevelCount = 1,
                SampleCount = 1,
            });
            view = texture.CreateView();
        }
        else
        {
            pixels = new byte[PageSize * PageSize];
        }
    }

    private bool TryAllocate(int width, int height, out int u, out int v)
    {
        int w = width + Padding;
        int h = height + Padding;
        // The shortest shelf tall enough with room left.
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
            // New shelf, rounded up to a multiple of 8 so similar masks share it.
            int shelfHeight = (h + 7) & ~7;
            if (nextShelfY + shelfHeight > PageSize)
            {
                u = v = 0;
                return false;
            }
            shelves.Add(new Shelf { Y = nextShelfY, Height = shelfHeight, NextX = 0 });
            nextShelfY += shelfHeight;
            best = shelves.Count - 1;
        }

        var chosen = shelves[best];
        u = chosen.NextX;
        v = chosen.Y;
        chosen.NextX += w;
        shelves[best] = chosen;
        return true;
    }

    private void Upload(MaskRegion region, ReadOnlySpan<byte> coverage)
    {
        if (!gpu)
        {
            var dst = pixels.AsSpan();
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
            var origin = new WGPUOrigin3D { X = (uint)region.U, Y = (uint)region.V, Z = 0 };
            var size = new Extent3D { Width = (uint)region.Width, Height = (uint)region.Height, DepthOrArrayLayers = 1 };
            device.Queue.WriteTexture(texture, 0, origin, staging, bytesPerRow, (uint)region.Height, size);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>Releases the GPU page.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (!view.IsInvalid)
        {
            view.Dispose();
        }
        if (!texture.IsInvalid)
        {
            texture.Dispose();
        }
    }
}
