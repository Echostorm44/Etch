using System;
using BenchmarkDotNet.Attributes;
using Etch.Text;

namespace Etch.Bench.Text;

/// <summary>
/// Benchmarks for <see cref="GammaLut"/> application during glyph rasterization.
/// </summary>
[MemoryDiagnoser]
public class GammaLutBench
{
    private GammaLut _gammaLut = null!;
    private byte[] _bitmap = null!;
    private byte[] _lcdBitmap = null!;

    [Params(16, 32, 64)]
    public int GlyphSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _gammaLut = new GammaLut(1.8f);
        int size = GlyphSize * GlyphSize;
        _bitmap = new byte[size];
        _lcdBitmap = new byte[size * 3]; // 3 bytes per pixel for LCD (R, G, B)

        // Fill with realistic coverage values using deterministic LCG
        uint state = 42;
        for (int i = 0; i < size; i++)
        {
            state = state * 1103515245 + 12345;
            byte value = (byte)((state >> 16) & 0xFF);
            _bitmap[i] = value;
            _lcdBitmap[i * 3 + 0] = value;
            _lcdBitmap[i * 3 + 1] = value;
            _lcdBitmap[i * 3 + 2] = value;
        }
    }

    [Benchmark(Baseline = true)]
    public void NoGamma()
    {
        // Baseline: no gamma correction applied
        // Just copy to prevent optimization
        byte sum = 0;
        for (int i = 0; i < _bitmap.Length; i++)
        {
            sum += _bitmap[i];
        }
        _ = sum;
    }

    [Benchmark]
    public void ApplyGrayscale()
    {
        _gammaLut.Apply(_bitmap, 128);
    }

    [Benchmark]
    public void ApplyLcd()
    {
        _gammaLut.ApplyLcd(_lcdBitmap, 200, 200, 200);
    }
}
