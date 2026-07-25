using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Etch.Text.Atlas;

namespace Etch.Bench.Text;

/// <summary>
/// Benchmarks for <see cref="GlyphCacheKey"/> hash code and equality performance.
/// The key grew from 8 bytes (4 fields) to 12 bytes (6 fields + padding) in Phase 3.
/// </summary>
[MemoryDiagnoser]
public class GlyphCacheKeyBench
{
    private GlyphCacheKey[] _keys = null!;
    private Dictionary<GlyphCacheKey, int> _dictionary = null!;

    [Params(100, 1000, 10000)]
    public int KeyCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _keys = new GlyphCacheKey[KeyCount];
        _dictionary = new Dictionary<GlyphCacheKey, int>(KeyCount);

        uint state = 42;
        for (int i = 0; i < KeyCount; i++)
        {
            state = state * 1103515245 + 12345;
            int faceId = (int)(state % 10) + 1;
            state = state * 1103515245 + 12345;
            ushort size = (ushort)(640 + (state % 640));
            state = state * 1103515245 + 12345;
            ushort glyphId = (ushort)(state % 600);
            state = state * 1103515245 + 12345;
            byte subpixel = (byte)(state % 16);
            state = state * 1103515245 + 12345;
            byte gamma = (byte)(14 + (state % 9));
            state = state * 1103515245 + 12345;
            byte lum = (byte)(state % 256);

            _keys[i] = new GlyphCacheKey(faceId, size, glyphId, subpixel, gamma, lum);
            _dictionary[_keys[i]] = i;
        }
    }

    [Benchmark]
    public void HashCode()
    {
        int sum = 0;
        for (int i = 0; i < _keys.Length; i++)
        {
            sum += _keys[i].GetHashCode();
        }
        _ = sum;
    }

    [Benchmark]
    public void EqualsTrue()
    {
        int count = 0;
        for (int i = 0; i < _keys.Length; i++)
        {
            if (_keys[i].Equals(_keys[i]))
            {
                count++;
            }
        }
        _ = count;
    }

    [Benchmark]
    public void DictionaryLookupHit()
    {
        int sum = 0;
        for (int i = 0; i < _keys.Length; i++)
        {
            if (_dictionary.TryGetValue(_keys[i], out int value))
            {
                sum += value;
            }
        }
        _ = sum;
    }

    [Benchmark]
    public void DictionaryLookupMiss()
    {
        int sum = 0;
        var missKey = new GlyphCacheKey(999, 999, 999, 99, 99, 99);
        for (int i = 0; i < _keys.Length; i++)
        {
            if (_dictionary.TryGetValue(missKey, out int value))
            {
                sum += value;
            }
        }
        _ = sum;
    }
}
