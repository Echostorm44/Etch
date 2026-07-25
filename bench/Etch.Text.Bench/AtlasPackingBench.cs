using System;
using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Text.Atlas;

namespace Etch.Bench.Text;

[MemoryDiagnoser]
public class AtlasPackingBench
{
    [Params(1024, 2048, 4096)]
    public int AtlasSize { get; set; }

    [Params(32, 64, 128)]
    public int GlyphWidth { get; set; }

    [Benchmark(Baseline = true)]
    public void ShelfPackUniform()
    {
        var packer = new ShelfPacker(AtlasSize, AtlasSize, 256);
        int h = GlyphWidth;
        int count = 0;
        while (packer.TryInsert(GlyphWidth, h, out _, out _))
        {
            count++;
        }
    }

    [Benchmark]
    public void ShelfPackVariable()
    {
        var packer = new ShelfPacker(AtlasSize, AtlasSize, 256);
        int[] heights = { 32, 48, 64, 80, 96, 112, 128, 160, 192, 224, 256 };
        int count = 0;
        int idx = 0;
        while (packer.TryInsert(GlyphWidth, heights[idx % heights.Length], out _, out _))
        {
            count++;
            idx++;
        }
    }

}