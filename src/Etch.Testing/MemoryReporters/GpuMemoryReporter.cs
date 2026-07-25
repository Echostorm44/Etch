using System;
using Etch.Gpu;
using Etch.Scene;
using Etch.Testing;

namespace Etch.Testing.MemoryReporters;

public static class GpuMemoryReporter
{
    /// <summary>
    /// Estimates GPU memory used by the render cache for a given scene size.
    /// Returns the approximate bytes allocated for textures, buffers, and pipelines.
    /// Returns 0 if GPU is unavailable.
    /// </summary>
    public static long EstimateCacheMemory(int width, int height)
    {
        try
        {
            using var cache = new SceneGpuRenderer.GpuRenderCache(width, height);
            var workingSetBefore = Environment.WorkingSet;
            using (var probeScene = CreateProbeScene(width, height))
            {
                _ = cache.Render(probeScene);
            }
            var workingSetAfter = Environment.WorkingSet;
            return Math.Max(0, workingSetAfter - workingSetBefore);
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuAdapterUnavailable ||
                                         ex.Code == Etch.PanicCodes.GpuDeviceCreationFailed)
        {
            return 0;
        }
    }

    private static SceneBuffer CreateProbeScene(int width, int height)
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Etch.Geometry.Affine.Identity);
        var paint = Paint.Solid(0xFFFF0000u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Etch.Geometry.Rect(0, 0, width, height), paintId, xform);
        builder.EndFrame();
        return builder.End();
    }
}
