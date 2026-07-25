using System;
using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;

namespace Etch.Bench.Blend;

[MemoryDiagnoser]
public sealed class FullStackBlendBenchmark
{
    private SceneBuffer _lowVarietyScene = null!;
    private SceneBuffer _mediumVarietyScene = null!;
    private SceneBuffer _highVarietyScene = null!;

    [GlobalSetup]
    public void Setup()
    {
        _lowVarietyScene = BuildScene(variety: 1);
        _mediumVarietyScene = BuildScene(variety: 4);
        _highVarietyScene = BuildScene(variety: 16);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public void LowVariety()
    {
        for (int i = 0; i < 100; i++)
            _ = SceneRunner.RunCpu(_lowVarietyScene, 256, 256);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public void MediumVariety()
    {
        for (int i = 0; i < 100; i++)
            _ = SceneRunner.RunCpu(_mediumVarietyScene, 256, 256);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    public void HighVariety()
    {
        for (int i = 0; i < 100; i++)
            _ = SceneRunner.RunCpu(_highVarietyScene, 256, 256);
    }

    private static SceneBuffer BuildScene(int variety)
    {
        var sb = SceneBuilder.Begin(estimatedCommands: 256);
        sb.BeginFrame();

        int xformId = sb.AddTransform(Affine.Identity);
        var modes = Enum.GetValues<BlendMode>();

        var lcg = new Lcg((uint)variety);
        for (int i = 0; i < 100; i++)
        {
            uint color = (uint)(0xFF000000 | (lcg.Next() & 0xFFFFFF));
            int paintId = sb.AddPaint(Paint.Solid(color));

            BlendMode mode = modes[i % Math.Min(variety, modes.Length)];
            double x = lcg.NextDouble() * 200;
            double y = lcg.NextDouble() * 200;
            var rect = new Rect(x, y, x + 50, y + 50);

            sb.PushLayer(rect, 1.0f, mode);
            sb.FillRect(rect, paintId, xformId);
            sb.PopLayer();
        }

        sb.EndFrame();
        return sb.End();
    }

    private struct Lcg
    {
        private uint _state;
        public Lcg(uint seed) => _state = seed == 0 ? 1 : seed;
        public uint Next()
        {
            _state = 1664525u * _state + 1013904223u;
            return _state;
        }
        public double NextDouble() => Next() / (double)uint.MaxValue;
    }
}
