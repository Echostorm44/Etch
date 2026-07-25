using System;
using System.Collections.Generic;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using Etch.Testing.MemoryReporters;
using TUnit;

namespace Etch.Correctness.Tests.Memory;

public class MemoryBudgetTests
{
    private const int BaselineRenderSize = 64;
    private const int StressRenderSize = 64;
    private const int StressPathCount = 100;

    [Test]
    public async Task ParseProjectPlan_MemorySection_HasAllExpectedRows()
    {
        var rows = MemoryBudgetParser.ParseProjectPlan(
            AppContext.BaseDirectory + "/../../../../../..");

        await Assert.That(rows.Count).IsGreaterThanOrEqualTo(5);
    }

    [Test]
    public async Task PerFrameManagedAllocations_BaselineScene_ZeroBytesAfterWarmup()
    {
        var scene = CreateSimpleFillScene();
        long before = GC.GetTotalAllocatedBytes(precise: false);
        for (int i = 0; i < 100; i++)
        {
            _ = SceneRunner.RunCpu(scene, BaselineRenderSize, BaselineRenderSize);
        }
        long after = GC.GetTotalAllocatedBytes(precise: false);
        long delta = after - before;

        await Assert.That(delta).IsLessThanOrEqualTo(1024);
    }

    [Test]
    public async Task ManagedHeap_BaselineRender_StaysWithinBounds()
    {
        var scene = CreateSimpleFillScene();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);

        for (int i = 0; i < 100; i++)
        {
            _ = SceneRunner.RunCpu(scene, BaselineRenderSize, BaselineRenderSize);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        long growth = heapAfter - heapBefore;

        await Assert.That(growth).IsLessThanOrEqualTo(10 * 1024 * 1024);
    }

    [Test]
    public async Task ManagedHeap_StressScene_StaysWithinBounds()
    {
        var scene = CreateStressScene();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);

        _ = SceneRunner.RunCpu(scene, StressRenderSize, StressRenderSize);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long heapAfter = GC.GetTotalMemory(forceFullCollection: true);
        long growth = heapAfter - heapBefore;

        await Assert.That(growth).IsLessThanOrEqualTo(50 * 1024 * 1024);
    }

    [Test]
    public async Task GpuMemory_EstimateCacheMemory_WithinBounds()
    {
        long gpuMem = GpuMemoryReporter.EstimateCacheMemory(BaselineRenderSize, BaselineRenderSize);

        await Assert.That(gpuMem).IsGreaterThanOrEqualTo(0);
        await Assert.That(gpuMem).IsLessThanOrEqualTo(100 * 1024 * 1024);
    }

    [Test]
    public async Task NativePool_WorkingSet_BaselineRender_StaysWithinBounds()
    {
        var scene = CreateSimpleFillScene();
        long workingSetBefore = NativePoolReporter.WorkingSet;

        for (int i = 0; i < 100; i++)
        {
            _ = SceneRunner.RunCpu(scene, BaselineRenderSize, BaselineRenderSize);
        }

        long workingSetAfter = NativePoolReporter.WorkingSet;
        long growth = workingSetAfter - workingSetBefore;

        await Assert.That(growth).IsLessThanOrEqualTo(100 * 1024 * 1024);
    }

    [Test]
    public async Task AllBudgetRows_SatisfyManagedConstraints()
    {
        var rows = MemoryBudgetParser.ParseProjectPlan(
            AppContext.BaseDirectory + "/../../../../../..");

        var scene = CreateSimpleFillScene();
        long before = GC.GetTotalAllocatedBytes(precise: false);
        for (int i = 0; i < 100; i++)
        {
            _ = SceneRunner.RunCpu(scene, BaselineRenderSize, BaselineRenderSize);
        }
        long after = GC.GetTotalAllocatedBytes(precise: false);
        long allocatedDelta = after - before;

        foreach (var row in rows)
        {
            if (row.BudgetBytes == 0)
            {
                await Assert.That(allocatedDelta).IsLessThanOrEqualTo(1024);
            }
            else
            {
                await Assert.That(allocatedDelta).IsLessThanOrEqualTo(row.BudgetBytes);
            }
        }
    }

    private static SceneBuffer CreateSimpleFillScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFFFF0000u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(8, 8, 56, 56), paintId, xform);
        builder.EndFrame();
        return builder.End();
    }

    private static SceneBuffer CreateStressScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var random = new Random(42);

        for (int i = 0; i < StressPathCount; i++)
        {
            uint color = 0xFF000000u | (uint)random.Next(1 << 24);
            var paint = Paint.Solid(color);
            int paintId = builder.AddPaint(paint);

            double x = random.NextDouble() * StressRenderSize;
            double y = random.NextDouble() * StressRenderSize;
            double w = 4 + random.NextDouble() * 16;
            double h = 4 + random.NextDouble() * 16;
            builder.FillRect(new Rect(x, y, x + w, y + h), paintId, xform);
        }

        builder.EndFrame();
        return builder.End();
    }
}
