using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Text.Atlas;
using Etch.Text.Rasterize;
using Etch.Text.Shape;

namespace Etch.Bench.Text;

/// <summary>
/// End-to-end 10 K glyph benchmark.
/// Budget: ≤ 5 ms CPU (warm), ≤ 2 ms GPU (warm), ≤ 40 ms CPU (cold).
/// </summary>
[MemoryDiagnoser]
public class TenKGlyphBench
{
    // 10 000 ASCII characters — a mix of letters, spaces and punctuation.
    private const int GlyphCount = 10_000;
    private const int AtlasDim = 2048;

    private FontFace _face = null!;
    private string _text = null!;
    private ShapedRun _shapedRun;

    // Per-glyph data extracted from the shaped run.
    private ushort[] _glyphIds = null!;
    private GlyphCacheKey[] _cacheKeys = null!;

    // Pre-rasterized bitmaps for each *unique* glyph.
    private readonly Dictionary<ushort, (byte[] Bitmap, int W, int H)> _bitmaps = new();

    // Warm-state caches.
    private ShapingCache _shapingCache = null!;
    private ShelfPacker _shelfPacker = null!;

    [GlobalSetup]
    public void Setup()
    {
        byte[] fontBlob = LoadRobotoAsync().GetAwaiter().GetResult();
        _face = FontFace.Load(fontBlob, 2048, 14f);
        _text = GenerateCorpus(GlyphCount);
        // --- shape once to extract glyph IDs ---
        _shapedRun = Shaper.Shape(new ShapeRequest(_text, _face, BiDiLevel.LeftToRight, "Latn"));
        _glyphIds = new ushort[_shapedRun.GlyphCount];
        _cacheKeys = new GlyphCacheKey[_shapedRun.GlyphCount];
        for (int i = 0; i < _shapedRun.GlyphCount; i++)
        {
            _glyphIds[i] = _shapedRun.Glyphs[i].GlyphId;
            _cacheKeys[i] = GlyphCacheKey.FromSizeAndSubpixel(
                _face.PointSize, _face.Id, _glyphIds[i], 0);
        }

        // --- pre-rasterize every unique glyph ---
        var unique = new HashSet<ushort>(_glyphIds);
        foreach (ushort gid in unique)
        {
            var buffer = new byte[64 * 64]; // generous upper bound for 14 pt
            GlyphRasterizer.Rasterize(_face, gid, subpixelX: 0f,
                buffer, out int w, out int h);
            if (w > 0 && h > 0)
            {
                var trimmed = new byte[w * h];
                Buffer.BlockCopy(buffer, 0, trimmed, 0, w * h);
                _bitmaps[gid] = (trimmed, w, h);
            }
        }

        // --- warm caches ---
        WarmCaches();
    }

    private void WarmCaches()
    {
        _shapingCache = new ShapingCache(capacity: 4096);
        _shapingCache.TryShape(new ShapeRequest(_text, _face, BiDiLevel.LeftToRight, "Latn"), out _); // prime

        _shelfPacker = new ShelfPacker(AtlasDim, AtlasDim, 64);
        foreach (ushort gid in new HashSet<ushort>(_glyphIds))
        {
            if (_bitmaps.TryGetValue(gid, out var bmp))
            {
                var key = GlyphCacheKey.FromSizeAndSubpixel(
                    _face.PointSize, _face.Id, gid, 0);
                _shelfPacker.TryInsert(bmp.W, bmp.H, out _, out _);
            }
        }
    }

    // =====================================================================
    // Warm benchmarks — steady state (caches hot)
    // =====================================================================

    [Benchmark(Baseline = true)]
    [AllocationBudget(0)]
    public void WarmShape()
    {
        _shapingCache.TryShape(new ShapeRequest(_text, _face, BiDiLevel.LeftToRight, "Latn"), out _);
    }

    [Benchmark]
    [AllocationBudget(0)]
    public void WarmRasterLookup()
    {
        // Simulate atlas lookup for every glyph instance.
        for (int i = 0; i < _cacheKeys.Length; i++)
        {
            _ = _bitmaps.ContainsKey(_glyphIds[i]);
        }
    }

    [Benchmark]
    [AllocationBudget(0)]
    public void WarmAtlasPack()
    {
        // Re-pack the same glyphs (simulates a frame where nothing changed).
        var packer = new ShelfPacker(AtlasDim, AtlasDim, 64);
        foreach (ushort gid in new HashSet<ushort>(_glyphIds))
        {
            if (_bitmaps.TryGetValue(gid, out var bmp))
            {
                packer.TryInsert(bmp.W, bmp.H, out _, out _);
            }
        }
    }

    // =====================================================================
    // Cold benchmarks — first frame (empty caches)
    // =====================================================================

    [Benchmark]
    public void ColdShape()
    {
        // No shaping cache — raw HarfBuzz.
        _ = Shaper.Shape(new ShapeRequest(_text, _face, BiDiLevel.LeftToRight, "Latn"));
    }

    [Benchmark]
    public void ColdRaster()
    {
        // Rasterize every unique glyph from scratch.
        var buffer = new byte[64 * 64];
        foreach (ushort gid in new HashSet<ushort>(_glyphIds))
        {
            GlyphRasterizer.Rasterize(_face, gid, 0f, buffer, out _, out _);
        }
    }

    [Benchmark]
    public void ColdAtlasPack()
    {
        var packer = new ShelfPacker(AtlasDim, AtlasDim, 64);
        foreach (ushort gid in new HashSet<ushort>(_glyphIds))
        {
            if (_bitmaps.TryGetValue(gid, out var bmp))
            {
                packer.TryInsert(bmp.W, bmp.H, out _, out _);
            }
        }
    }

    // =====================================================================
    // Corpus generation
    // =====================================================================

    private static string GenerateCorpus(int length)
    {
        const string pool =
            "abcdefghijklmnopqrstuvwxyz" +
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "0123456789" +
            " ,.!?;:'\"-()[]{}";

        var chars = new char[length];
        uint state = 42;
        for (int i = 0; i < length; i++)
        {
            // LCG: same algorithm as glibc (deterministic)
            state = state * 1103515245 + 12345;
            int idx = (int)(state % (uint)pool.Length);
            chars[i] = pool[idx];
        }
        return new string(chars);
    }

    private static async Task<byte[]> LoadRobotoAsync()
    {
        using var client = new HttpClient();
        var uri = new Uri("https://fonts.gstatic.com/s/roboto/v32/KFOmCnqEu92Fr1Me5Q.ttf");
        using var response = await client.GetAsync(uri).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }
}
