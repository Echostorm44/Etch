using System;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Scheduler;
using TUnit;

#pragma warning disable CA2000
namespace Etch.Tiling.Tests;

internal sealed class TileSchedulerTests
{
    [Test]
    public void SingleThreadedTileScheduler_ImplementsITileScheduler()
    {
        var scheduler = new SingleThreadedTileScheduler();
        if (scheduler is not ITileScheduler)
            throw new InvalidOperationException("SingleThreadedTileScheduler should implement ITileScheduler");
        scheduler.Dispose();
    }

    [Test]
    public void WorkStealingTileScheduler_ImplementsITileScheduler()
    {
        var scheduler = new WorkStealingTileScheduler(2);
        if (scheduler is not ITileScheduler)
            throw new InvalidOperationException("WorkStealingTileScheduler should implement ITileScheduler");
        scheduler.Dispose();
    }

    [Test]
    public void WorkStealingTileScheduler_WorkerCount()
    {
        var scheduler1 = new WorkStealingTileScheduler(4);
        if (scheduler1.WorkerCount != 4)
            throw new InvalidOperationException($"Expected WorkerCount=4, got {scheduler1.WorkerCount}");
        scheduler1.Dispose();

        var scheduler2 = new WorkStealingTileScheduler();
        if (scheduler2.WorkerCount != Environment.ProcessorCount)
            throw new InvalidOperationException($"Expected WorkerCount={Environment.ProcessorCount}, got {scheduler2.WorkerCount}");
        scheduler2.Dispose();
    }

    [Test]
    public void ParallelClassifier_NullScheduler_PanicsET_P_0503()
    {
        bool threw = false;
        try
        {
            var grid = new TileGrid<TTile16>(1920, 1080);
            var scene = CreateSimpleScene();
            ParallelClassifier.Classify(scene, grid, null);
        }
        catch (EtchException ex) when (ex.Code.Value == "ET-P-0503")
        {
            threw = true;
        }

        if (!threw)
            throw new InvalidOperationException("Expected panic ET-P-0503 for null scheduler");
    }

    [Test]
    public void ParallelClassifier_SingleThreadedTileScheduler_ProducesResult()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var scene = CreateSimpleScene();
        var scheduler = new SingleThreadedTileScheduler();

        var result = ParallelClassifier.Classify(scene, grid, scheduler);

        if (result.AllEntries.Length == 0)
            throw new InvalidOperationException("Expected non-empty classified scene");
    }

    [Test]
    public void ParallelClassifier_WorkStealingTileScheduler_ProducesResult()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var scene = CreateSimpleScene();
        var scheduler = new WorkStealingTileScheduler(2);

        var result = ParallelClassifier.Classify(scene, grid, scheduler);

        if (result.AllEntries.Length == 0)
            throw new InvalidOperationException("Expected non-empty classified scene");
    }

    [Test]
    public void ParallelClassifier_SingleVsMultiThread_SameOutput()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var scene = CreateComplexScene();

        ITileScheduler? singleScheduler = new SingleThreadedTileScheduler();
        ITileScheduler? multiScheduler = new WorkStealingTileScheduler(4);

        var singleResult = ParallelClassifier.Classify(scene, grid, singleScheduler);
        var multiResult = ParallelClassifier.Classify(scene, grid, multiScheduler);

        singleScheduler = null;
        multiScheduler = null;

        var singleEntries = singleResult.AllEntries;
        var multiEntries = multiResult.AllEntries;

        if (singleEntries.Length != multiEntries.Length)
            throw new InvalidOperationException($"Length mismatch: single={singleEntries.Length}, multi={multiEntries.Length}");

        for (int i = 0; i < singleEntries.Length; i++)
        {
            if (singleEntries[i].TileIndex != multiEntries[i].TileIndex)
                throw new InvalidOperationException($"TileIndex mismatch at {i}: single={singleEntries[i].TileIndex}, multi={multiEntries[i].TileIndex}");
            if (singleEntries[i].CommandOrder != multiEntries[i].CommandOrder)
                throw new InvalidOperationException($"CommandOrder mismatch at {i}: single={singleEntries[i].CommandOrder}, multi={multiEntries[i].CommandOrder}");
            if (singleEntries[i].Kind != multiEntries[i].Kind)
                throw new InvalidOperationException($"Kind mismatch at {i}: single={singleEntries[i].Kind}, multi={multiEntries[i].Kind}");
        }
    }

    [Test]
    public void WorkStealingTileScheduler_Dispose_CanBeCalledMultipleTimes()
    {
        var scheduler = new WorkStealingTileScheduler(2);
        scheduler.Dispose();
        scheduler.Dispose();
    }

    [Test]
    public void SingleThreadedTileScheduler_Dispose_CanBeCalledMultipleTimes()
    {
        var scheduler = new SingleThreadedTileScheduler();
        scheduler.Dispose();
        scheduler.Dispose();
    }

    private static SceneBuffer CreateSimpleScene()
    {
        var sb = SceneBuilder.Begin(256);
        sb.BeginFrame();

        int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
        int xformId = sb.AddTransform(Affine.Identity);

        var rect = Rect.FromLTRB(100, 100, 200, 200);
        sb.FillRect(rect, paintId, xformId);

        sb.EndFrame();
        return sb.End();
    }

    private static SceneBuffer CreateComplexScene()
    {
        var sb = SceneBuilder.Begin(4096);
        sb.BeginFrame();

        int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
        int xformId = sb.AddTransform(Affine.Identity);

        for (int i = 0; i < 100; i++)
        {
            int seed = 42 + i;
            int x = ((seed * 1103515245 + 12345) & 0x7FFFFFFF) % 1800;
            int y = ((seed * 1103515245 + 67890) & 0x7FFFFFFF) % 1000;
            int w = 50 + ((seed * 13579) & 0x7FFFFFFF) % 150;
            int h = 50 + ((seed * 24680) & 0x7FFFFFFF) % 150;
            var rect = Rect.FromLTRB(x, y, x + w, y + h);
            sb.FillRect(rect, paintId, xformId);
        }

        sb.EndFrame();
        return sb.End();
    }
}
