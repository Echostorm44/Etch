using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Etch.Text.Atlas;

namespace Etch.Bench.Text;

/// <summary>
/// Benchmarks for <see cref="LruCache"/> insertion and lookup performance.
/// Measures the overhead of the per-page LRU cache used by GlyphAtlas.
/// </summary>
[MemoryDiagnoser]
public class LruCacheBench
{
    private LruCache _cache = null!;
    private GlyphCacheKey[] _keys = null!;

    [Params(100, 1000, 5000)]
    public int CacheSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _cache = new LruCache(CacheSize);
        _keys = new GlyphCacheKey[CacheSize * 2]; // Double the cache size to test eviction

        uint state = 42;
        for (int i = 0; i < _keys.Length; i++)
        {
            state = state * 1103515245 + 12345;
            int faceId = (int)(state % 10) + 1;
            state = state * 1103515245 + 12345;
            ushort size = (ushort)(state % 1280);
            state = state * 1103515245 + 12345;
            ushort glyphId = (ushort)(i); // Unique
            state = state * 1103515245 + 12345;
            byte subpixel = (byte)(state % 16);

            _keys[i] = new GlyphCacheKey(faceId, size, glyphId, subpixel);
        }
    }

    [Benchmark]
    public void InsertAndLookup()
    {
        _cache = new LruCache(CacheSize);
        for (int i = 0; i < _keys.Length; i++)
        {
            _cache.TryInsert(_keys[i], 32, 32, 0, 0, 0, 0, out _);
        }
        for (int i = 0; i < _keys.Length; i++)
        {
            _cache.TryLookup(_keys[i], out _);
        }
    }

    [Benchmark]
    public void LookupHit()
    {
        // Prime the cache
        _cache = new LruCache(CacheSize);
        for (int i = 0; i < CacheSize; i++)
        {
            _cache.TryInsert(_keys[i], 32, 32, 0, 0, 0, 0, out _);
        }

        int sum = 0;
        for (int i = 0; i < CacheSize; i++)
        {
            if (_cache.TryLookup(_keys[i], out _))
            {
                sum++;
            }
        }
        _ = sum;
    }

    [Benchmark]
    public void LookupMiss()
    {
        // Prime the cache
        _cache = new LruCache(CacheSize);
        for (int i = 0; i < CacheSize; i++)
        {
            _cache.TryInsert(_keys[i], 32, 32, 0, 0, 0, 0, out _);
        }

        int sum = 0;
        for (int i = CacheSize; i < _keys.Length; i++)
        {
            if (_cache.TryLookup(_keys[i], out _))
            {
                sum++;
            }
        }
        _ = sum;
    }
}
