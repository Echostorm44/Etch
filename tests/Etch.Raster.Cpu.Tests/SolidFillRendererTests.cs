#pragma warning disable CA2000 // Test SceneBuffers use public constructors (non-owning); disposal is optional

using System;
using Etch;
using Etch.Geometry;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class SolidFillRendererTests
{
    [Test]
    public void FillRectSpanningTwoByTwoTiles()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var rect = new Rect(0, 0, 16, 16);
        var transform = Affine.Identity;
        var paint = Paint.Solid(0xFFFF0000);

        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.FillRect, new FillRectPayload
            {
                RectId = 0,
                PaintId = 0,
                TransformId = 0
            })
        };

        var rects = new Rect[] { rect };
        var transforms = new Affine[] { transform };
        var paints = new Paint[] { paint };

        var scene = new SceneBuffer(
            commands,
            Array.Empty<PathEntry>(),
            Array.Empty<byte>(),
            paints,
            transforms,
            rects,
            Array.Empty<GradientStops>());

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 0, ClassificationKind.FillRect, default),
            new ClassificationEntry(1, 0, ClassificationKind.FillRect, default),
            new ClassificationEntry(2, 0, ClassificationKind.FillRect, default),
            new ClassificationEntry(3, 0, ClassificationKind.FillRect, default),
        };
        var offsets = new int[] { 0, 1, 2, 3, 4 };
        var classified = new ClassifiedScene(entries, offsets, 4);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        SolidFillRenderer.Render(scene, classified, grid, fb);

        for (int y = 0; y < surfaceHeight; y++)
        {
            for (int x = 0; x < surfaceWidth; x++)
            {
                ref readonly var pixel = ref buffer[y * surfaceWidth + x];
                float r = (float)pixel.R;
                if (r < 0.8f)
                    throw new InvalidOperationException($"Expected red pixel at ({x},{y}), got R={r}");
            }
        }
    }

    [Test]
    public void PartialTileRectLeavesOutsidePixelsUnchanged()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var rect = new Rect(2, 2, 6, 6);
        var transform = Affine.Identity;
        var paint = Paint.Solid(0xFF00FF00);

        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.FillRect, new FillRectPayload
            {
                RectId = 0,
                PaintId = 0,
                TransformId = 0
            })
        };

        var rects = new Rect[] { rect };
        var transforms = new Affine[] { transform };
        var paints = new Paint[] { paint };

        var scene = new SceneBuffer(
            commands,
            Array.Empty<PathEntry>(),
            Array.Empty<byte>(),
            paints,
            transforms,
            rects,
            Array.Empty<GradientStops>());

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 0, ClassificationKind.FillRect, default),
        };
        var offsets = new int[] { 0, 1, 1, 1, 1 };
        var classified = new ClassifiedScene(entries, offsets, 4);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = Rgba16f.From(1, 0, 0, 1);
        }
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        SolidFillRenderer.Render(scene, classified, grid, fb);

        for (int y = 0; y < surfaceHeight; y++)
        {
            for (int x = 0; x < surfaceWidth; x++)
            {
                ref readonly var pixel = ref buffer[y * surfaceWidth + x];
                if (x >= 2 && x < 6 && y >= 2 && y < 6)
                {
                    float g = (float)pixel.G;
                    if (g < 0.5f)
                        throw new InvalidOperationException($"Expected green pixel at ({x},{y}), got G={g}");
                }
                else
                {
                    float r = (float)pixel.R;
                    if (r < 0.8f)
                        throw new InvalidOperationException($"Expected red pixel at ({x},{y}), got R={r}");
                }
            }
        }
    }

    [Test]
    public void FillPathRendersSimpleQuad()
    {
        int surfaceWidth = 8;
        int surfaceHeight = 8;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var pathId = builder.AddPath(new Geometry.BezPath(
            new byte[] { (byte)PathVerb.MoveTo, (byte)PathVerb.LineTo, (byte)PathVerb.LineTo, (byte)PathVerb.Close },
            new double[] { 1, 1, 5, 1, 1, 5 },
            4));
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        builder.EndFrame();
        var scene = builder.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 1, ClassificationKind.FillPath, default),
        };
        var offsets = new int[] { 0, 1, 1 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Rgba16f.From(0, 0, 0, 1);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        SolidFillRenderer.Render(scene, classified, grid, fb);

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

        if (filledCount < 4)
            throw new InvalidOperationException($"Expected more filled pixels, got {filledCount}");
    }

    [Test]
    public void FillPathRendersQuadBezier()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var pathId = builder.AddPath(new Geometry.BezPath(
            new byte[] { (byte)PathVerb.MoveTo, (byte)PathVerb.QuadTo, (byte)PathVerb.Close },
            new double[] { 1, 8, 8, 1, 15, 8 },
            3));
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        builder.EndFrame();
        var scene = builder.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 1, ClassificationKind.FillPath, default),
        };
        var offsets = new int[] { 0, 1, 1 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Rgba16f.From(0, 0, 0, 1);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        SolidFillRenderer.Render(scene, classified, grid, fb);

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

        if (filledCount < 16)
            throw new InvalidOperationException($"Expected more filled pixels, got {filledCount}");
    }

    [Test]
    public void FillPathRendersCubicBezier()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var pathId = builder.AddPath(new Geometry.BezPath(
            new byte[] { (byte)PathVerb.MoveTo, (byte)PathVerb.CubicTo, (byte)PathVerb.Close },
            new double[] { 1, 8, 1, 1, 15, 15, 15, 8 },
            3));
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        builder.EndFrame();
        var scene = builder.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 1, ClassificationKind.FillPath, default),
        };
        var offsets = new int[] { 0, 1, 1 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Rgba16f.From(0, 0, 0, 1);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        SolidFillRenderer.Render(scene, classified, grid, fb);

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

        if (filledCount < 10)
            throw new InvalidOperationException($"Expected more filled pixels, got {filledCount}");
    }

    [Test]
    public void MismatchedFramebufferDimensionsThrowsInvariant()
    {
        int surfaceWidth = 8;
        int surfaceHeight = 8;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var scene = new SceneBuffer(
            Array.Empty<SceneCommand>(),
            Array.Empty<PathEntry>(),
            Array.Empty<byte>(),
            Array.Empty<Paint>(),
            Array.Empty<Affine>(),
            Array.Empty<Rect>(),
            Array.Empty<GradientStops>());

        var entries = Array.Empty<ClassificationEntry>();
        var offsets = new int[] { 0, 0 };
        var classified = new ClassifiedScene(entries, offsets, 1);

        var buffer = new Rgba16f[100];
        var fb = new Framebuffer(10, 10, 10, buffer);

        try
        {
            SolidFillRenderer.Render(scene, classified, grid, fb);
            throw new InvalidOperationException("Expected EtchException");
        }
        catch (EtchException e) when (e.Code == PanicCodes.InvariantViolation)
        {
        }
    }
}