using System;
using System.Threading;
using Etch.Geometry;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Scheduler;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class ParallelStripRendererTests
{
    [Test]
    public void ParallelRenderMatchesSingleThreaded()
    {
        int surfaceWidth = 32;
        int surfaceHeight = 32;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillRect(new Rect(4, 4, 28, 28), paintId, transformId);
        builder.EndFrame();
        var scene = builder.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 1, ClassificationKind.FillRect, default),
        };
        var offsets = new int[] { 0, 1, 1 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var strips = StripEmitter.Emit(scene, classified, grid);

        var singleBuffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < singleBuffer.Length; i++)
            singleBuffer[i] = Rgba16f.From(0, 0, 0, 1);
        var singleFb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, singleBuffer);
        StripRenderer.Render(scene, strips, grid, singleFb);

        var parallelBuffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < parallelBuffer.Length; i++)
            parallelBuffer[i] = Rgba16f.From(0, 0, 0, 1);
        var parallelFb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, parallelBuffer);

        var scheduler = new WorkStealingTileScheduler(4);
        try
        {
            ParallelStripRenderer.Render(scene, strips, grid, parallelFb, scheduler);
        }
        finally
        {
            scheduler.Dispose();
        }

        for (int i = 0; i < singleBuffer.Length; i++)
        {
            var s = singleBuffer[i];
            var p = parallelBuffer[i];
            if (Math.Abs((float)s.R - (float)p.R) > 0.0001f ||
                Math.Abs((float)s.G - (float)p.G) > 0.0001f ||
                Math.Abs((float)s.B - (float)p.B) > 0.0001f ||
                Math.Abs((float)s.A - (float)p.A) > 0.0001f)
            {
                throw new InvalidOperationException($"Mismatch at pixel {i}: single=({s.R},{s.G},{s.B},{s.A}) parallel=({p.R},{p.G},{p.B},{p.A})");
            }
        }
    }

    [Test]
    public void WorkItemCountMatchesTileCount()
    {
        int surfaceWidth = 32;
        int surfaceHeight = 32;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillRect(new Rect(4, 4, 28, 28), paintId, transformId);
        builder.EndFrame();
        var scene = builder.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 1, ClassificationKind.FillRect, default),
        };
        var offsets = new int[] { 0, 1, 1 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var strips = StripEmitter.Emit(scene, classified, grid);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Rgba16f.From(0, 0, 0, 1);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        var scheduler = new WorkStealingTileScheduler(4);
        try
        {
            ParallelStripRenderer.Render(scene, strips, grid, fb, scheduler);
        }
        finally
        {
            scheduler.Dispose();
        }
    }

    [Test]
    public void SingleThreadedSchedulerProducesCorrectOutput()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillRect(new Rect(2, 2, 14, 14), paintId, transformId);
        builder.EndFrame();
        var scene = builder.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 1, ClassificationKind.FillRect, default),
        };
        var offsets = new int[] { 0, 1, 1 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var strips = StripEmitter.Emit(scene, classified, grid);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Rgba16f.From(0, 0, 0, 1);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        using var scheduler = new SingleThreadedTileScheduler();
        ParallelStripRenderer.Render(scene, strips, grid, fb, scheduler);

        int filledCount = 0;
        for (int y = 0; y < surfaceHeight; y++)
        {
            for (int x = 0; x < surfaceWidth; x++)
            {
                float r = (float)buffer[y * surfaceWidth + x].R;
                if (r > 0.5f)
                    filledCount++;
            }
        }

        if (filledCount < 30)
            throw new InvalidOperationException($"Expected more filled pixels, got {filledCount}");
    }
}
