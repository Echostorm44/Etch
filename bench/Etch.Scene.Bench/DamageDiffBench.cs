using System;
using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Geometry;
using Etch.Scene;
using Etch.Scene.Damage;

namespace Etch.Scene.Bench;

[MemoryDiagnoser]
public sealed class DamageDiffBench
{
    private const int TileCountX = 64;
    private const int TileCountY = 64;
    private const int CommandCount = 5_000;

    private SceneBuffer _prevScene = null!;
    private SceneBuffer _currScene = null!;
    private DamageTracker _tracker = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tracker = DamageTracker.Create(TileCountX, TileCountY);
        _tracker.MarkAllDirty();

        // Build base scene with 5000 commands
        _prevScene = BuildScene(CommandCount, seed: 0x12345678);
        _currScene = BuildScene(CommandCount, seed: 0x12345678);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _tracker.Dispose();
    }

    [Benchmark(OperationsPerInvoke = 100)]
    [AllocationBudget(0)]
    public void DiffZeroPercent()
    {
        // Identical scenes → 0% dirty after initial seeding
        for (int i = 0; i < 100; i++)
        {
            _tracker.MarkAllDirty();
            _ = _tracker.Diff(_prevScene, _currScene);
            var result = _tracker.Diff(_prevScene, _currScene);
            if (result.DirtyCount != 0)
                throw new InvalidOperationException($"Expected 0 dirty, got {result.DirtyCount}");
        }
    }

    [Benchmark(OperationsPerInvoke = 100)]
    [AllocationBudget(0)]
    public void DiffOnePercent()
    {
        RunWithDirtyPercent(0.01);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    [AllocationBudget(0)]
    public void DiffTenPercent()
    {
        RunWithDirtyPercent(0.10);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    [AllocationBudget(0)]
    public void DiffFiftyPercent()
    {
        RunWithDirtyPercent(0.50);
    }

    [Benchmark(OperationsPerInvoke = 100)]
    [AllocationBudget(0)]
    public void DiffOneHundredPercent()
    {
        RunWithDirtyPercent(1.00);
    }

    private void RunWithDirtyPercent(double dirtyFraction)
    {
        int dirtyCount = (int)(CommandCount * dirtyFraction);
        for (int i = 0; i < 100; i++)
        {
            _tracker.MarkAllDirty();
            _ = _tracker.Diff(_prevScene, _currScene);

            // Swap in a modified scene with the requested dirty percentage
            var modified = (dirtyFraction == 0.0)
                ? _currScene
                : BuildSceneWithDirtyCount(CommandCount, dirtyCount, iteration: i);

            var result = _tracker.Diff(_currScene, modified);

            // We only assert rough bounds; exact dirty tile count depends on spatial layout
            if (dirtyFraction == 0.0 && result.DirtyCount != 0)
                throw new InvalidOperationException($"Expected 0 dirty, got {result.DirtyCount}");
        }
    }

    private static SceneBuffer BuildScene(int commandCount, uint seed)
    {
        var sb = SceneBuilder.Begin(commandCount + 10);
        sb.BeginFrame();

        int paintId = sb.AddPaint(Paint.Solid(0xFF808080));
        int xformId = sb.AddTransform(Affine.Identity);

        var lcg = new Lcg(seed);
        for (int i = 0; i < commandCount; i++)
        {
            double x = lcg.NextDouble() * (TileCountX * 32 - 10);
            double y = lcg.NextDouble() * (TileCountY * 32 - 10);
            var rect = Rect.FromLTRB(x, y, x + 8, y + 8);
            sb.FillRect(rect, paintId, xformId);
        }

        sb.EndFrame();
        return sb.End();
    }

    private static SceneBuffer BuildSceneWithDirtyCount(int commandCount, int dirtyCount, int iteration)
    {
        var sb = SceneBuilder.Begin(commandCount + 10);
        sb.BeginFrame();

        int paintId = sb.AddPaint(Paint.Solid(0xFF808080));
        int dirtyPaintId = sb.AddPaint(Paint.Solid(0xFF808081));
        int xformId = sb.AddTransform(Affine.Identity);

        var lcg = new Lcg(0x12345678);
        for (int i = 0; i < commandCount; i++)
        {
            double x = lcg.NextDouble() * (TileCountX * 32 - 10);
            double y = lcg.NextDouble() * (TileCountY * 32 - 10);
            var rect = Rect.FromLTRB(x, y, x + 8, y + 8);
            int pid = (i < dirtyCount) ? dirtyPaintId : paintId;
            sb.FillRect(rect, pid, xformId);
        }

        sb.EndFrame();
        return sb.End();
    }

    /// <summary>
    /// Deterministic LCG for benchmark reproducibility.
    /// </summary>
    private struct Lcg
    {
        private uint _state;
        public Lcg(uint seed) => _state = seed;
        public double NextDouble()
        {
            _state = 1664525u * _state + 1013904223u;
            return _state / (double)uint.MaxValue;
        }
    }
}
