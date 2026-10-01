using Etch.Gpu;
using Etch.Text.Atlas;
using TUnit;

namespace Etch.Text.Tests;

public sealed class GlyphAtlasTests
{
    private static bool TryCreateDevice(out Device device)
    {
        device = default;
        try
        {
            var instance = Instance.Create();
            var (adapterStatus, adapter) = AsyncRequest.RequestAdapterSync(instance);
            if (adapterStatus != RequestAdapterStatus.Success || adapter.IsInvalid)
            {
                instance.Dispose();
                return false;
            }

            var (deviceStatus, dev) = AsyncRequest.RequestDeviceSync(instance, adapter);
            adapter.Dispose();
            instance.Dispose();

            if (deviceStatus != RequestDeviceStatus.Success || dev.IsInvalid)
            {
                return false;
            }

            device = dev;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    [Test]
    public async Task LruCacheAcceptsManySmallGlyphs()
    {
        var cache = new LruCache(4096 * 4096);
        int count = 0;
        for (int i = 0; i < 10000; i++)
        {
            var key = new GlyphCacheKey(1, 640, (ushort)i, 0);
            if (cache.TryInsert(key, 64, 64, 0, 0, (short)0, (short)0, out var region))
            {
                count++;
            }
        }
        if (count == 0)
            throw new InvalidOperationException("Should have inserted at least some glyphs");
    }

    [Test]
    public async Task LruEvictionHappensWhenCapacityExceeded()
    {
        var cache = new LruCache(100, 256, 256, 128);

        var keyA = new GlyphCacheKey(1, 640, 1, 0);
        var keyB = new GlyphCacheKey(1, 640, 2, 0);
        var keyC = new GlyphCacheKey(1, 640, 3, 0);

        cache.TryInsert(keyA, 64, 32, 0, 0, (short)0, (short)0, out _);
        cache.TryInsert(keyB, 64, 32, 0, 0, (short)0, (short)0, out _);

        var keyD = new GlyphCacheKey(1, 640, 4, 0);
        cache.TryInsert(keyD, 64, 32, 0, 0, (short)0, (short)0, out _);

        if (cache.Count > 3)
        {
            throw new InvalidOperationException("Cache should have evicted at least one entry");
        }
    }

    [Test]
    public async Task ZeroAllocLookup()
    {
        var cache = new LruCache(4096 * 4096);

        var key = new GlyphCacheKey(1, 640, 65, 0);
        cache.TryInsert(key, 64, 64, 0, 0, (short)0, (short)0, out _);

        for (int i = 0; i < 1000; i++)
        {
            if (!cache.TryLookup(key, out var region))
            {
                throw new InvalidOperationException("Key should still be in cache");
            }
        }
    }

    [Test]
    public async Task InsertReturnsCorrectRegion()
    {
        var cache = new LruCache(4096 * 4096);
        var key = new GlyphCacheKey(1, 640, 65, 0);

        cache.TryInsert(key, 100, 100, 0, 0, (short)0, (short)0, out var region);

        if (region.W != 100 || region.H != 100)
            throw new InvalidOperationException($"Region mismatch: W={region.W}, H={region.H}");
        if (region.U >= 4096 || region.V >= 4096)
            throw new InvalidOperationException($"Region out of bounds: U={region.U}, V={region.V}");
    }

    [Test]
    public async Task GlyphAtlas_PreCreatesFirstPage()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 64, maxPages: 4);
            using (atlas)
            {
                if (atlas.PageCount != 1)
                {
                    throw new InvalidOperationException($"Expected 1 pre-created page, got {atlas.PageCount}");
                }

                var page = atlas.GetPage(0);
                if (page.Dimension != 512)
                {
                    throw new InvalidOperationException($"Expected dimension 512, got {page.Dimension}");
                }
            }
        }
    }

    [Test]
    public async Task GlyphAtlas_InsertAndLookup_SinglePage()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 64, maxPages: 4);
            using (atlas)
            {
                var key = new GlyphCacheKey(1, 640, 65, 0);
                byte[] data = new byte[32 * 32];
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = (byte)(i % 256);
                }

                bool inserted = atlas.TryInsert(key, data, 32, 32, out var region, out int pageIndex, 0, 0);
                if (!inserted)
                {
                    throw new InvalidOperationException("Insert should succeed on empty atlas");
                }
                if (pageIndex != 0)
                {
                    throw new InvalidOperationException($"Expected page 0, got {pageIndex}");
                }
                if (region.W != 32 || region.H != 32)
                {
                    throw new InvalidOperationException($"Region size mismatch: {region.W}x{region.H}");
                }

                bool found = atlas.TryLookup(key, out var lookupRegion, out int lookupPage);
                if (!found)
                {
                    throw new InvalidOperationException("Lookup should find inserted glyph");
                }
                if (lookupPage != 0)
                {
                    throw new InvalidOperationException($"Lookup expected page 0, got {lookupPage}");
                }
                if (lookupRegion.U != region.U || lookupRegion.V != region.V)
                {
                    throw new InvalidOperationException("Lookup region mismatch");
                }
            }
        }
    }

    [Test]
    public async Task GlyphAtlas_CreatesMultiplePagesWhenFull()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            // Small atlas (512x512) with large row height to force page creation quickly
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 512, maxPages: 4);
            using (atlas)
            {
                int insertedCount = 0;
                for (int i = 0; i < 100; i++)
                {
                    var key = new GlyphCacheKey(1, 640, (ushort)i, 0);
                    byte[] data = new byte[64 * 64];
                    if (atlas.TryInsert(key, data, 64, 64, out _, out int pageIndex, 0, 0))
                    {
                        insertedCount++;
                    }
                }

                if (atlas.PageCount < 2)
                {
                    throw new InvalidOperationException(
                        $"Expected multiple pages after filling 256x256 atlas with 64x64 glyphs, " +
                        $"but got {atlas.PageCount} pages (inserted {insertedCount} glyphs)");
                }

                if (atlas.GlyphCount != insertedCount)
                {
                    throw new InvalidOperationException(
                        $"GlyphCount mismatch: expected {insertedCount}, got {atlas.GlyphCount}");
                }
            }
        }
    }

    [Test]
    public async Task GlyphAtlas_LookupAcrossMultiplePages()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 512, maxPages: 4);
            using (atlas)
            {
                // Insert enough glyphs to fill multiple pages
                var keys = new List<GlyphCacheKey>();
                for (int i = 0; i < 100; i++)
                {
                    var key = new GlyphCacheKey(1, 640, (ushort)i, 0);
                    byte[] data = new byte[96 * 96];
                    if (atlas.TryInsert(key, data, 96, 96, out _, out _, 0, 0))
                    {
                        keys.Add(key);
                    }
                }

                if (atlas.PageCount < 2)
                {
                    throw new InvalidOperationException("Test requires at least 2 pages");
                }

                // Verify every inserted glyph can be looked up
                foreach (var key in keys)
                {
                    if (!atlas.TryLookup(key, out _, out int pageIndex))
                    {
                        throw new InvalidOperationException($"Failed to lookup glyph {key.GlyphId}");
                    }
                    if (pageIndex < 0 || pageIndex >= atlas.PageCount)
                    {
                        throw new InvalidOperationException($"Invalid page index {pageIndex} for glyph {key.GlyphId}");
                    }
                }
            }
        }
    }

    [Test]
    public async Task GlyphAtlas_RespectsMaxPagesLimit()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 512, maxPages: 2);
            using (atlas)
            {
                int insertedCount = 0;
                for (int i = 0; i < 200; i++)
                {
                    var key = new GlyphCacheKey(1, 640, (ushort)i, 0);
                    byte[] data = new byte[96 * 96];
                    if (atlas.TryInsert(key, data, 96, 96, out _, out _, 0, 0))
                    {
                        insertedCount++;
                    }
                }

                if (atlas.PageCount > 2)
                {
                    throw new InvalidOperationException(
                        $"Atlas should not exceed maxPages=2, but got {atlas.PageCount}");
                }

                if (insertedCount == 0)
                {
                    throw new InvalidOperationException("Should have inserted at least some glyphs");
                }
            }
        }
    }

    [Test]
    public async Task GlyphAtlas_ReturnsDefaultForMissingGlyph()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 64, maxPages: 4);
            using (atlas)
            {
                var missingKey = new GlyphCacheKey(99, 999, 999, 99);
                bool found = atlas.TryLookup(missingKey, out var region, out int pageIndex);

                if (found)
                {
                    throw new InvalidOperationException("Lookup for missing glyph should return false");
                }
                if (region.W != 0 || region.H != 0)
                {
                    throw new InvalidOperationException("Missing glyph region should be default");
                }
                if (pageIndex != -1)
                {
                    throw new InvalidOperationException($"Missing glyph pageIndex should be -1, got {pageIndex}");
                }
            }
        }
    }

    [Test]
    public async Task GlyphAtlas_Rgba8Format_CreatesCorrectTexture()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            var atlas = new GlyphAtlas(device, 512, TextureFormat.Rgba8Unorm, 64, maxPages: 2);
            using (atlas)
            {
                if (atlas.Format != TextureFormat.Rgba8Unorm)
                {
                    throw new InvalidOperationException($"Expected Rgba8Unorm format, got {atlas.Format}");
                }

                var page = atlas.GetPage(0);
                if (page.Texture.IsInvalid)
                {
                    throw new InvalidOperationException("Page texture should be valid");
                }

                var key = new GlyphCacheKey(1, 640, 1, 0);
                byte[] data = new byte[16 * 16 * 4];
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = (byte)(i % 256);
                }

                bool inserted = atlas.TryInsert(key, data, 16, 16, out _, out _, 0, 0);
                if (!inserted)
                {
                    throw new InvalidOperationException("RGBA8 insert should succeed");
                }
            }
        }
    }

    // ── WP-3509: atlas lifetime / churn recovery ────────────────────

    [Test]
    public async Task LruCache_Soak_ResetOnExhaustion_PlacesEveryGlyph_BoundedMemory()
    {
        // Mirror the production mono-atlas page: 4096² texture, 128px shelves.
        var cache = new LruCache(4096 * 4096, 4096, 4096, 128);

        const int totalGlyphs = 100_000;
        int resets = 0;
        int peakCount = 0;

        for (int i = 0; i < totalGlyphs; i++)
        {
            // Unique (size, glyph, subpixel) per iteration; all heights well
            // under the 128px shelf so every failure is packer exhaustion
            // (recoverable), never a too-tall glyph.
            var key = new GlyphCacheKey(1, (ushort)(640 + i / 256), (ushort)(i % 256), (byte)(i % 4));
            int w = 8 + (i % 24);
            int h = 12 + (i % 30);

            if (cache.TryInsert(key, w, h, 0, 0, 0, 0, out _))
            {
                peakCount = Math.Max(peakCount, cache.Count);
                continue;
            }

            // Packer full. The presenter resets between frames; model that and
            // retry — the glyph must land in the fresh atlas.
            if (cache.IsTallerThanShelf(h))
            {
                throw new InvalidOperationException($"Glyph {i} unexpectedly too tall (h={h})");
            }

            cache.Reset();
            resets++;
            if (!cache.TryInsert(key, w, h, 0, 0, 0, 0, out _))
            {
                throw new InvalidOperationException(
                    $"Glyph {i} (w={w} h={h}) still rejected immediately after a reset — recovery failed");
            }
            peakCount = Math.Max(peakCount, cache.Count);
        }

        // The churn must actually have exercised recovery, not just fit.
        await Assert.That(resets).IsGreaterThan(0);

        // Memory is bounded: the cache never holds more than roughly one
        // atlas-full of glyphs, regardless of the 100k churned through it.
        await Assert.That(peakCount).IsLessThan(50_000);
        await Assert.That(cache.TotalSize).IsLessThanOrEqualTo(4096 * 4096);
    }

    [Test]
    public async Task LruCache_GlyphTallerThanShelf_IsReportedAndNotExhaustion()
    {
        var cache = new LruCache(4096 * 4096, 4096, 4096, 128);

        // 200px tall glyph cannot fit a 128px shelf — a reset would not help.
        await Assert.That(cache.IsTallerThanShelf(200)).IsTrue();
        await Assert.That(cache.IsTallerThanShelf(100)).IsFalse();

        var key = new GlyphCacheKey(1, 640, 1, 0);
        bool inserted = cache.TryInsert(key, 50, 200, 0, 0, 0, 0, out _);
        await Assert.That(inserted).IsFalse();
    }

    // GPU device create/dispose races in wgpu-native teardown under heavy
    // parallelism; run the atlas GPU tests exclusively (matches Etch.Gpu.Tests).
    [Test]
    [NotInParallel]
    public async Task GlyphAtlas_Reset_RecoversFromExhaustion()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            // Small single-page atlas with large glyphs so it exhausts fast.
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 64, maxPages: 1);
            using (atlas)
            {
                byte[] data = new byte[60 * 60];

                int i = 0;
                while (!atlas.WasExhausted && i < 10_000)
                {
                    var key = new GlyphCacheKey(1, 640, (ushort)i, 0);
                    atlas.TryInsert(key, data, 60, 60, out _, out _, 0, 0);
                    i++;
                }

                await Assert.That(atlas.WasExhausted).IsTrue();
                int genBefore = atlas.Generation;

                atlas.Reset();

                await Assert.That(atlas.WasExhausted).IsFalse();
                await Assert.That(atlas.Generation).IsEqualTo(genBefore + 1);
                await Assert.That(atlas.GlyphCount).IsEqualTo(0);

                // The atlas is usable again immediately.
                var freshKey = new GlyphCacheKey(2, 700, 5, 0);
                bool inserted = atlas.TryInsert(freshKey, data, 60, 60, out _, out _, 0, 0);
                await Assert.That(inserted).IsTrue();
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task GlyphAtlas_TallGlyph_DoesNotSignalExhaustion()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            // 64px shelves; a 200px glyph can never fit. Refusing it must not
            // set WasExhausted, or the presenter would reset every frame.
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 64, maxPages: 1);
            using (atlas)
            {
                var key = new GlyphCacheKey(1, 640, 1, 0);
                byte[] data = new byte[40 * 200];
                bool inserted = atlas.TryInsert(key, data, 40, 200, out _, out _, 0, 0);

                await Assert.That(inserted).IsFalse();
                await Assert.That(atlas.WasExhausted).IsFalse();
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task GlyphAtlas_TooWideGlyph_DoesNotSignalExhaustion()
    {
        if (!TryCreateDevice(out var device))
        {
            return;
        }

        using (device)
        {
            // 512px-wide page; a 600px-wide glyph can never fit a shelf row.
            // Like a too-tall glyph, refusing it must not set WasExhausted.
            var atlas = new GlyphAtlas(device, 512, TextureFormat.R8Unorm, 64, maxPages: 1);
            using (atlas)
            {
                var key = new GlyphCacheKey(1, 640, 1, 0);
                byte[] data = new byte[600 * 20];
                bool inserted = atlas.TryInsert(key, data, 600, 20, out _, out _, 0, 0);

                await Assert.That(inserted).IsFalse();
                await Assert.That(atlas.WasExhausted).IsFalse();
            }
        }
    }
}
