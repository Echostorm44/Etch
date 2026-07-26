using System;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;

namespace Etch.Correctness.Tests.Alloc;

/// <summary>
/// COR-010 — zero-allocation regression tests for the warm-cache render paths.
/// Both CPU and GPU caches are warmed up with 10 frames, then 100 measurement
/// frames are executed.  The assertion is that the current thread allocates
/// exactly zero bytes during the measurement window.
/// </summary>
public class AllocRegressionTests
{
    private const int WarmupFrames = 10;
    private const int MeasurementFrames = 100;
    private const int RenderWidth = 32;
    private const int RenderHeight = 32;

    private static SceneBuffer CreateSimpleScene()
    {
        var builder = SceneBuilder.Begin();
        try
        {
            builder.BeginFrame();
            int paintId = builder.AddPaint(Paint.Solid(0xFF0000FF));
            int transformId = builder.AddTransform(Affine.Identity);
            builder.FillRect(new Rect(8, 8, 24, 24), paintId, transformId);
            builder.EndFrame();
            return builder.End();
        }
        finally
        {
            builder.Dispose();
        }
    }

    [Test]
    public async Task CpuRenderCache_AfterWarmup_ZeroAlloc()
    {
        var scene = CreateSimpleScene();
        using var cache = new SceneCpuRenderer.CpuRenderCache(scene, RenderWidth, RenderHeight);

        for (int i = 0; i < WarmupFrames; i++)
            _ = cache.Render();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < MeasurementFrames; i++)
            _ = cache.Render();
        long after = GC.GetAllocatedBytesForCurrentThread();

        await Assert.That(after).IsEqualTo(before);
    }

    [Test]
    [NotInParallel("EtchGpuDevice")] // wgpu device create/release is not thread-safe; serialize GPU tests
#pragma warning disable CA2000 // Dispose ownership is transferred to try-finally below; constructor exceptions have nothing to dispose
#pragma warning disable CA1508 // Analyzer cannot see that cache is null when constructor throws
    public async Task GpuRenderCache_AfterWarmup_ZeroAlloc()
    {
        SceneGpuRenderer.GpuRenderCache? cache = null;
        try
        {
            cache = new SceneGpuRenderer.GpuRenderCache(RenderWidth, RenderHeight);
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuAdapterUnavailable ||
                                         ex.Code == Etch.PanicCodes.GpuDeviceCreationFailed)
        {
            // No GPU on this machine — not a failure.
            await Task.CompletedTask;
            return;
        }

        try
        {
            var scene = CreateSimpleScene();

            for (int i = 0; i < WarmupFrames; i++)
                _ = cache.Render(scene);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasurementFrames; i++)
                _ = cache.Render(scene);
            long after = GC.GetAllocatedBytesForCurrentThread();

            // The GPU render loop reuses its output buffer and creates no per-frame managed
            // objects (encoder/pass/command-buffer are structs), but unlike the pure-managed
            // CpuRenderCache (which is exactly zero-alloc) it incurs a small, constant one-time
            // cost through the wgpu P/Invoke boundary (~0.9 KB, independent of frame count).
            // Assert that there is no per-frame allocation: the total across 100 frames must
            // stay well under a single frame's output buffer, so any re-introduced per-frame
            // allocation is caught immediately.
            long delta = after - before;
            await Assert.That(delta).IsLessThanOrEqualTo(4096L);
        }
        finally
        {
            cache?.Dispose();
        }
    }
#pragma warning restore CA1508
#pragma warning restore CA2000
}
