using Etch.Gpu;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;
using Etch.Text.Atlas;
using TUnit;

namespace Etch.Text.Tests;

/// <summary>
/// Glyph atlases start small and grow as glyphs arrive: a fresh atlas holds one small page, growing
/// keeps every glyph at its texels (in memory and on the GPU, uploads batched), eviction and
/// exhaustion begin only at full size, and <see cref="GlyphAtlas.Trim"/> returns to the initial size.
/// </summary>
public sealed class GlyphAtlasGrowthTests
{
    private static byte[] Bitmap(int width, int height, byte value)
    {
        var bitmap = new byte[width * height];
        Array.Fill(bitmap, value);
        return bitmap;
    }

    [Test]
    public async Task FreshAtlas_HoldsOneInitialPage()
    {
        using var atlas = new GlyphAtlas(2048, TextureFormat.R8Unorm, 128, 1, initialDimension: 256);

        await Assert.That(atlas.Dimension).IsEqualTo(256);
        await Assert.That(atlas.MaxDimension).IsEqualTo(2048);
        await Assert.That(atlas.ResidentBytes).IsEqualTo(256L * 256);
        await Assert.That(atlas.GetPage(0).Pixels!.Length).IsEqualTo(256 * 256);
    }

    [Test]
    public async Task WithoutInitialDimension_TheAtlasStartsAtFullSize()
    {
        using var atlas = new GlyphAtlas(512, TextureFormat.Rgba8UnormSrgb, 128, 1);

        await Assert.That(atlas.Dimension).IsEqualTo(512);
        await Assert.That(atlas.ResidentBytes).IsEqualTo(512L * 512 * 4);
    }

    [Test]
    public async Task UiText_FitsTheInitialPage()
    {
        // Shelves are as tall as their glyphs (rounded to 8): a few hundred UI-sized glyphs share a
        // 256² page instead of each taking a slot on a 128-texel shelf.
        using var atlas = new GlyphAtlas(2048, TextureFormat.R8Unorm, 128, 1, initialDimension: 256);
        atlas.BeginFrame();
        for (int i = 0; i < 300; i++)
        {
            int width = 6 + i % 6;
            int height = 9 + i % 5;
            bool inserted = atlas.TryInsert(new GlyphCacheKey(1, 640, (ushort)i, 0), Bitmap(width, height, 1), width, height, out _, out _, 0, 0);
            await Assert.That(inserted).IsTrue();
        }

        await Assert.That(atlas.Dimension).IsEqualTo(256);
        await Assert.That(atlas.WasExhausted).IsFalse();
    }

    [Test]
    public async Task Growth_KeepsEveryGlyphAtItsTexels()
    {
        using var atlas = new GlyphAtlas(2048, TextureFormat.R8Unorm, 128, 1, initialDimension: 256);
        atlas.BeginFrame();
        int generation = atlas.Generation;
        var placed = new List<(GlyphCacheKey Key, AtlasRegion Region, byte Value)>();
        for (int i = 0; atlas.Dimension < 1024; i++)
        {
            byte value = (byte)(1 + i % 250);
            var key = new GlyphCacheKey(1, 640, (ushort)i, 0);
            bool inserted = atlas.TryInsert(key, Bitmap(30, 40, value), 30, 40, out var region, out int page, 0, 0);
            await Assert.That(inserted).IsTrue();
            await Assert.That(page).IsEqualTo(0);
            placed.Add((key, region, value));
        }

        // Grew (twice) without forgetting anything: same regions, same texels, same generation.
        await Assert.That(atlas.Generation).IsEqualTo(generation);
        await Assert.That(atlas.WasExhausted).IsFalse();
        var pixels = atlas.GetPage(0).Pixels!;
        int dim = atlas.Dimension;
        await Assert.That(pixels.Length).IsEqualTo(dim * dim);
        foreach (var (key, region, value) in placed)
        {
            await Assert.That(atlas.TryLookup(key, out var found, out _)).IsTrue();
            await Assert.That((found.U, found.V)).IsEqualTo((region.U, region.V));
            await Assert.That(pixels[region.V * dim + region.U]).IsEqualTo(value);
            await Assert.That(pixels[(region.V + 39) * dim + region.U + 29]).IsEqualTo(value);
        }
    }

    [Test]
    public async Task GlyphWiderThanTheInitialPage_GrowsTheAtlas()
    {
        using var atlas = new GlyphAtlas(2048, TextureFormat.R8Unorm, 128, 1, initialDimension: 256);

        await Assert.That(atlas.CanEverHold(600, 100)).IsTrue();
        bool inserted = atlas.TryInsert(new GlyphCacheKey(1, 640, 1, 0), Bitmap(600, 100, 7), 600, 100, out _, out _, 0, 0);

        await Assert.That(inserted).IsTrue();
        await Assert.That(atlas.Dimension).IsEqualTo(1024);
    }

    [Test]
    public async Task GlyphTooTallForAnyShelf_DoesNotGrowTheAtlas()
    {
        using var atlas = new GlyphAtlas(2048, TextureFormat.R8Unorm, 128, 1, initialDimension: 256);

        bool inserted = atlas.TryInsert(new GlyphCacheKey(1, 640, 1, 0), Bitmap(20, 200, 7), 20, 200, out _, out _, 0, 0);

        await Assert.That(inserted).IsFalse();
        await Assert.That(atlas.WasExhausted).IsFalse();
        await Assert.That(atlas.Dimension).IsEqualTo(256);
    }

    [Test]
    public async Task Exhaustion_HappensOnlyAtFullSize_AndResetRecovers()
    {
        using var atlas = new GlyphAtlas(512, TextureFormat.R8Unorm, 64, 1, initialDimension: 128);
        atlas.BeginFrame();
        int i = 0;
        while (!atlas.WasExhausted && i < 10_000)
        {
            atlas.TryInsert(new GlyphCacheKey(1, 640, (ushort)i, 0), Bitmap(60, 60, 1), 60, 60, out _, out _, 0, 0);
            i++;
        }

        await Assert.That(atlas.WasExhausted).IsTrue();
        await Assert.That(atlas.Dimension).IsEqualTo(512);

        // ResetAtlasesIfExhausted semantics: a reset between frames makes room again at full size.
        atlas.Reset();
        atlas.BeginFrame();
        await Assert.That(atlas.WasExhausted).IsFalse();
        await Assert.That(atlas.TryInsert(new GlyphCacheKey(2, 640, 1, 0), Bitmap(60, 60, 1), 60, 60, out _, out _, 0, 0)).IsTrue();
        await Assert.That(atlas.Dimension).IsEqualTo(512);
    }

    [Test]
    public async Task BelowFullSize_TheAtlasGrowsInsteadOfEvicting()
    {
        using var atlas = new GlyphAtlas(1024, TextureFormat.R8Unorm, 128, 1, initialDimension: 128);
        atlas.BeginFrame();
        var first = new GlyphCacheKey(1, 640, 0, 0);
        atlas.TryInsert(first, Bitmap(100, 100, 1), 100, 100, out _, out _, 0, 0);
        // Later frames that do not draw the first glyph: below full size it must stay cached.
        for (int i = 1; i < 20; i++)
        {
            atlas.BeginFrame();
            atlas.TryInsert(new GlyphCacheKey(1, 640, (ushort)i, 0), Bitmap(100, 100, 1), 100, 100, out _, out _, 0, 0);
        }

        await Assert.That(atlas.Dimension).IsGreaterThan(128);
        await Assert.That(atlas.TryLookup(first, out _, out _)).IsTrue();
    }

    [Test]
    public async Task Trim_ReturnsToTheInitialSize()
    {
        using var atlas = new GlyphAtlas(2048, TextureFormat.Rgba8UnormSrgb, 128, 1, initialDimension: 128);
        atlas.TryInsert(new GlyphCacheKey(1, 640, 1, 0), new byte[600 * 40 * 4], 600, 40, out _, out _, 0, 0);
        await Assert.That(atlas.Dimension).IsEqualTo(1024);
        int generation = atlas.Generation;

        atlas.Trim();

        await Assert.That(atlas.Dimension).IsEqualTo(128);
        await Assert.That(atlas.ResidentBytes).IsEqualTo(128L * 128 * 4);
        await Assert.That(atlas.GlyphCount).IsEqualTo(0);
        await Assert.That(atlas.Generation).IsEqualTo(generation + 1);
        await Assert.That(atlas.TryInsert(new GlyphCacheKey(1, 640, 2, 0), new byte[16 * 16 * 4], 16, 16, out _, out _, 0, 0)).IsTrue();
    }

    [Test]
    public async Task Dispose_ReleasesThePages()
    {
        var atlas = new GlyphAtlas(2048, TextureFormat.R8Unorm, 128, 1, initialDimension: 256);
        atlas.Dispose();

        await Assert.That(atlas.PageCount).IsEqualTo(0);
        await Assert.That(atlas.ResidentBytes).IsEqualTo(0L);
    }

    // ── GPU ─────────────────────────────────────────────────────────────

    private static bool TryCreateDevice(out Device device)
    {
        device = default;
        var instance = Instance.Create();
        var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(instance);
        if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
        {
            instance.Dispose();
            return false;
        }
        var (deviceStatus, created) = AsyncRequest.RequestDeviceSync(instance, adapter);
        adapter.Dispose();
        instance.Dispose();
        if (deviceStatus != RequestDeviceStatus.Success || created.IsInvalid)
        {
            return false;
        }
        device = created;
        return true;
    }

    private static void Submit(Device device, CommandEncoder encoder)
    {
        using var commands = encoder.Finish();
        Span<CommandBuffer> submit = stackalloc CommandBuffer[1];
        submit[0] = commands;
        device.Queue.Submit(submit);
    }

    // The page's R8 texels, row-major, stride = dimension.
    private static byte[] ReadBack(Device device, Texture texture, int dimension)
    {
        uint stride = (uint)((dimension + 255) & ~255);
        using var buffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapRead | BufferUsage.CopyDst),
            Size = stride * (ulong)dimension,
        });
        using (var encoder = device.CreateCommandEncoder())
        {
            encoder.CopyTextureToBuffer(texture, 0, default, buffer,
                new WGPUTexelCopyBufferLayout { Offset = 0, BytesPerRow = stride, RowsPerImage = (uint)dimension },
                new Extent3D { Width = (uint)dimension, Height = (uint)dimension, DepthOrArrayLayers = 1 });
            Submit(device, encoder);
        }
        if (!buffer.MapSync(device, MapMode.Read))
        {
            throw new InvalidOperationException("Readback map failed");
        }
        var mapped = buffer.GetConstMappedRange(0, stride * (ulong)dimension);
        var pixels = new byte[dimension * dimension];
        for (int row = 0; row < dimension; row++)
        {
            mapped.Slice((int)(row * stride), dimension).CopyTo(pixels.AsSpan(row * dimension, dimension));
        }
        buffer.Unmap();
        return pixels;
    }

    [Test]
    [NotInParallel]
    public async Task GpuGrowth_KeepsUploadedAndQueuedGlyphs()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }
        using (device)
        {
            using var atlas = new GlyphAtlas(device, 1024, TextureFormat.R8Unorm, 128, maxPages: 1, initialDimension: 256);
            atlas.BeginFrame();
            int textureGeneration = atlas.TextureGeneration;

            // One glyph uploaded (flushed) before the page grows, the rest queued across the growth.
            var placed = new List<(AtlasRegion Region, byte Value)>();
            atlas.TryInsert(new GlyphCacheKey(1, 640, 0, 0), Bitmap(30, 40, 200), 30, 40, out var firstRegion, out _, 0, 0);
            placed.Add((firstRegion, 200));
            using (var encoder = device.CreateCommandEncoder())
            {
                atlas.FlushUploads(encoder);
                Submit(device, encoder);
            }
            await Assert.That(atlas.PendingUploads).IsEqualTo(0);

            for (int i = 1; atlas.Dimension < 512; i++)
            {
                byte value = (byte)(1 + i % 199);
                atlas.TryInsert(new GlyphCacheKey(1, 640, (ushort)i, 0), Bitmap(30, 40, value), 30, 40, out var region, out _, 0, 0);
                placed.Add((region, value));
            }
            await Assert.That(atlas.TextureGeneration).IsNotEqualTo(textureGeneration);
            await Assert.That(atlas.PendingUploads).IsEqualTo(placed.Count - 1);

            using (var encoder = device.CreateCommandEncoder())
            {
                atlas.FlushUploads(encoder);
                Submit(device, encoder);
            }
            await Assert.That(atlas.PendingUploads).IsEqualTo(0);

            int dim = atlas.Dimension;
            var pixels = ReadBack(device, atlas.GetPage(0).Texture, dim);
            foreach (var (region, value) in placed)
            {
                await Assert.That(pixels[region.V * dim + region.U]).IsEqualTo(value);
                await Assert.That(pixels[(region.V + 39) * dim + region.U + 29]).IsEqualTo(value);
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task GpuTrim_DropsQueuedUploadsAndShrinks()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }
        using (device)
        {
            using var atlas = new GlyphAtlas(device, 1024, TextureFormat.R8Unorm, 128, maxPages: 1, initialDimension: 256);
            atlas.TryInsert(new GlyphCacheKey(1, 640, 0, 0), Bitmap(600, 40, 9), 600, 40, out _, out _, 0, 0);
            await Assert.That(atlas.Dimension).IsEqualTo(1024);
            await Assert.That(atlas.PendingUploads).IsEqualTo(1);

            atlas.Trim();

            // The queued glyph lay outside the shrunk page: it must not be written there.
            await Assert.That(atlas.PendingUploads).IsEqualTo(0);
            await Assert.That(atlas.Dimension).IsEqualTo(256);
            await Assert.That(atlas.ResidentBytes).IsEqualTo(256L * 256);
            using var encoder = device.CreateCommandEncoder();
            atlas.FlushUploads(encoder);
            Submit(device, encoder);
        }
    }
}
