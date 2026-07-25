using System;
using Etch.Geometry;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class StripRendererTests
{
    [Test]
    public void FullCoverageStripMatchesSolidFillRenderer()
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

        var solidBuffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < solidBuffer.Length; i++)
            solidBuffer[i] = Rgba16f.From(0, 0, 0, 1);
        var solidFb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, solidBuffer);
        SolidFillRenderer.Render(scene, classified, grid, solidFb);

        var stripBuffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < stripBuffer.Length; i++)
            stripBuffer[i] = Rgba16f.From(0, 0, 0, 1);
        var stripFb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, stripBuffer);
        StripRenderer.Render(scene, strips, grid, stripFb);

        for (int i = 0; i < solidBuffer.Length; i++)
        {
            var s = solidBuffer[i];
            var t = stripBuffer[i];
            if (Math.Abs((float)s.R - (float)t.R) > 0.0001f ||
                Math.Abs((float)s.G - (float)t.G) > 0.0001f ||
                Math.Abs((float)s.B - (float)t.B) > 0.0001f ||
                Math.Abs((float)s.A - (float)t.A) > 0.0001f)
            {
                throw new InvalidOperationException($"Mismatch at pixel {i}: solid=({s.R},{s.G},{s.B},{s.A}) strip=({t.R},{t.G},{t.B},{t.A})");
            }
        }
    }

    [Test]
    public void HalfCoverageStripBlendsCorrectly()
    {
        int surfaceWidth = 8;
        int surfaceHeight = 8;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillRect(new Rect(0, 0, 8, 8), paintId, transformId);
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
            buffer[i] = Rgba16f.From(0, 0, 0, 0);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        StripRenderer.Render(scene, strips, grid, fb);

        float expectedR = Srgb.DecodeChannelScalar(255);
        float tolerance = 1.0f / 255.0f + 0.0001f;

        for (int y = 0; y < surfaceHeight; y++)
        {
            for (int x = 0; x < surfaceWidth; x++)
            {
                float actualR = (float)buffer[y * surfaceWidth + x].R;
                float diff = Math.Abs(actualR - expectedR);
                if (diff > tolerance)
                {
                    throw new InvalidOperationException($"Pixel ({x},{y}): expected R={expectedR}, got {actualR}, diff={diff}");
                }
            }
        }
    }

    [Test]
    public void EmptySceneProducesEmptyStrips()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        builder.EndFrame();
        var scene = builder.End();

        var entries = Array.Empty<ClassificationEntry>();
        var offsets = new int[] { 0, 0 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var strips = StripEmitter.Emit(scene, classified, grid);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Rgba16f.From(0, 0, 0, 1);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        StripRenderer.Render(scene, strips, grid, fb);
    }
}
