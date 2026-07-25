#pragma warning disable CA2000 // Test SceneBuffers use public constructors (non-owning); disposal is optional

using System;
using Etch.Geometry;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class ClipMaskTests
{
    [Test]
    public async Task EmptyClipStackReturnsEmpty()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        builder.EndFrame();
        var scene = builder.End();

        var scratch = new Framebuffer(16, 16, 16, new Rgba16f[16 * 16]);
        var mask = ClipMask.Build(scene, scene.Commands, scratch);

        await Assert.That(mask.Coverage.Width).IsEqualTo(0);
        await Assert.That(mask.Coverage.Height).IsEqualTo(0);
    }

    [Test]
    public async Task SingleRectClipMasksCorrectly()
    {
        var path = CreateRectPath(4, 4, 12, 12);

        var sb = SceneBuilder.Begin();
        sb.BeginFrame();
        var pathId = sb.AddPath(path);
        sb.PushClip(pathId, FillRule.NonZero, ClipMode.Intersect);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();

        var scratch = new Framebuffer(16, 16, 16, new Rgba16f[16 * 16]);
        var mask = ClipMask.Build(scene, scene.Commands.Slice(1, 1), scratch);

        await Assert.That(mask.Coverage.Width).IsEqualTo(16);
        await Assert.That(mask.Coverage.Height).IsEqualTo(16);

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                float alpha = (float)mask.Coverage.RowSpan(y)[x].R;
                bool inside = x >= 4 && x < 12 && y >= 4 && y < 12;
                float expected = inside ? 1.0f : 0.0f;
                if (Math.Abs(alpha - expected) > 0.01f)
                {
                    throw new InvalidOperationException($"Clip mask mismatch at ({x},{y}): expected {expected}, got {alpha}");
                }
            }
        }
    }

    [Test]
    public async Task NestedIntersectClips()
    {
        var outer = CreateRectPath(2, 2, 14, 14);
        var inner = CreateRectPath(4, 4, 12, 12);

        var sb = SceneBuilder.Begin();
        sb.BeginFrame();
        var outerId = sb.AddPath(outer);
        var innerId = sb.AddPath(inner);
        sb.PushClip(outerId, FillRule.NonZero, ClipMode.Intersect);
        sb.PushClip(innerId, FillRule.NonZero, ClipMode.Intersect);
        sb.PopClip();
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();

        var scratch = new Framebuffer(16, 16, 16, new Rgba16f[16 * 16]);
        var mask = ClipMask.Build(scene, scene.Commands.Slice(1, 2), scratch);

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                float alpha = (float)mask.Coverage.RowSpan(y)[x].R;
                bool inside = x >= 4 && x < 12 && y >= 4 && y < 12;
                float expected = inside ? 1.0f : 0.0f;
                if (Math.Abs(alpha - expected) > 0.01f)
                {
                    throw new InvalidOperationException($"Nested clip mismatch at ({x},{y}): expected {expected}, got {alpha}");
                }
            }
        }
    }

    [Test]
    public async Task DifferenceClip()
    {
        var outer = CreateRectPath(2, 2, 14, 14);
        var hole = CreateRectPath(6, 6, 10, 10);

        var sb = SceneBuilder.Begin();
        sb.BeginFrame();
        var outerId = sb.AddPath(outer);
        var holeId = sb.AddPath(hole);
        sb.PushClip(outerId, FillRule.NonZero, ClipMode.Intersect);
        sb.PushClip(holeId, FillRule.NonZero, ClipMode.Difference);
        sb.PopClip();
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();

        var scratch = new Framebuffer(16, 16, 16, new Rgba16f[16 * 16]);
        var mask = ClipMask.Build(scene, scene.Commands.Slice(1, 2), scratch);

        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                float alpha = (float)mask.Coverage.RowSpan(y)[x].R;
                bool insideOuter = x >= 2 && x < 14 && y >= 2 && y < 14;
                bool insideHole = x >= 6 && x < 10 && y >= 6 && y < 10;
                bool inside = insideOuter && !insideHole;
                float expected = inside ? 1.0f : 0.0f;
                if (Math.Abs(alpha - expected) > 0.01f)
                {
                    throw new InvalidOperationException($"Difference clip mismatch at ({x},{y}): expected {expected}, got {alpha}");
                }
            }
        }
    }

    [Test]
    public async Task UnbalancedPopClipThrows()
    {
        var commands = new SceneCommand[]
        {
            new SceneCommand(SceneOpcode.PopClip, new PopClipPayload()),
        };

        var scene = new SceneBuffer(
            commands,
            Array.Empty<PathEntry>(),
            Array.Empty<byte>(),
            Array.Empty<Paint>(),
            Array.Empty<Affine>(),
            Array.Empty<Rect>(),
            Array.Empty<GradientStops>());

        var scratch = new Framebuffer(16, 16, 16, new Rgba16f[16 * 16]);

        try
        {
            ClipMask.Build(scene, scene.Commands, scratch);
            throw new InvalidOperationException("Expected EtchException");
        }
        catch (EtchException e) when (e.Code == PanicCodes.UnbalancedClipStack)
        {
        }
    }

    [Test]
    public async Task ClipStackOverflowThrows()
    {
        var path = CreateRectPath(0, 0, 1, 1);
        int verbCount = path.VerbCount;
        int coordCount = 8;
        int headerSize = 8;
        int arenaLength = headerSize + verbCount + coordCount * 8;

        var pathArena = new byte[arenaLength];
        pathArena[0] = (byte)(verbCount & 0xFF);
        pathArena[1] = (byte)((verbCount >> 8) & 0xFF);
        pathArena[2] = (byte)((verbCount >> 16) & 0xFF);
        pathArena[3] = (byte)((verbCount >> 24) & 0xFF);
        pathArena[4] = (byte)(coordCount & 0xFF);
        pathArena[5] = (byte)((coordCount >> 8) & 0xFF);
        pathArena[6] = (byte)((coordCount >> 16) & 0xFF);
        pathArena[7] = (byte)((coordCount >> 24) & 0xFF);

        var verbs = new byte[] { (byte)PathVerb.MoveTo, (byte)PathVerb.LineTo, (byte)PathVerb.LineTo, (byte)PathVerb.LineTo, (byte)PathVerb.Close };
        verbs.CopyTo(pathArena, headerSize);
        var coords = new double[] { 0, 0, 1, 0, 1, 1, 0, 1 };
        Buffer.BlockCopy(coords, 0, pathArena, headerSize + verbCount, coordCount * 8);

        var pathTable = new PathEntry[] { new PathEntry(0, arenaLength, verbCount, coordCount) };

        var commands = new SceneCommand[17];
        for (int i = 0; i < 17; i++)
        {
            commands[i] = new SceneCommand(SceneOpcode.PushClip, new PushClipPayload { ClipId = 0, FillRule = 0, ClipMode = 0 });
        }

        var scene = new SceneBuffer(
            commands,
            pathTable,
            pathArena,
            Array.Empty<Paint>(),
            Array.Empty<Affine>(),
            Array.Empty<Rect>(),
            Array.Empty<GradientStops>());

        var scratch = new Framebuffer(16, 16, 16, new Rgba16f[16 * 16]);

        try
        {
            ClipMask.Build(scene, scene.Commands, scratch);
            throw new InvalidOperationException("Expected EtchException");
        }
        catch (EtchException e) when (e.Code == PanicCodes.ClipStackOverflow)
        {
        }
    }

    [Test]
    public async Task SolidFillRendererRespectsClipMask()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var path = CreateRectPath(4, 4, 12, 12);

        var sb = SceneBuilder.Begin();
        sb.BeginFrame();
        var pathId = sb.AddPath(path);
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.PushClip(pathId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 16, 16), paintId, transformId);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 2, ClassificationKind.FillRect, default),
            new ClassificationEntry(1, 2, ClassificationKind.FillRect, default),
            new ClassificationEntry(2, 2, ClassificationKind.FillRect, default),
            new ClassificationEntry(3, 2, ClassificationKind.FillRect, default),
        };
        var offsets = new int[] { 0, 1, 2, 3, 4 };
        var classified = new ClassifiedScene(entries, offsets, 4);

        var scratch = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, new Rgba16f[surfaceWidth * surfaceHeight]);
        var clipMask = ClipMask.Build(scene, scene.Commands.Slice(1, 1), scratch);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        SolidFillRenderer.Render(scene, classified, grid, fb, clipMask);

        for (int y = 0; y < surfaceHeight; y++)
        {
            for (int x = 0; x < surfaceWidth; x++)
            {
                float r = (float)buffer[y * surfaceWidth + x].R;
                bool inside = x >= 4 && x < 12 && y >= 4 && y < 12;
                if (inside && r < 0.8f)
                    throw new InvalidOperationException($"Expected red pixel inside clip at ({x},{y}), got R={r}");
                if (!inside && r > 0.1f)
                    throw new InvalidOperationException($"Expected black pixel outside clip at ({x},{y}), got R={r}");
            }
        }
    }

    [Test]
    public async Task StripRendererRespectsClipMask()
    {
        int surfaceWidth = 16;
        int surfaceHeight = 16;
        var grid = new TileGrid<TTile8>(surfaceWidth, surfaceHeight);

        var path = CreateRectPath(4, 4, 12, 12);

        var sb = SceneBuilder.Begin();
        sb.BeginFrame();
        var pathId = sb.AddPath(path);
        var paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = sb.AddTransform(Affine.Identity);
        sb.PushClip(pathId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 16, 16), paintId, transformId);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();

        var entries = new ClassificationEntry[]
        {
            new ClassificationEntry(0, 2, ClassificationKind.FillRect, default),
            new ClassificationEntry(1, 2, ClassificationKind.FillRect, default),
            new ClassificationEntry(2, 2, ClassificationKind.FillRect, default),
            new ClassificationEntry(3, 2, ClassificationKind.FillRect, default),
        };
        var offsets = new int[] { 0, 1, 2, 3, 4 };
        var classified = new ClassifiedScene(entries, offsets, 4);

        var strips = StripEmitter.Emit(scene, classified, grid);

        var scratch = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, new Rgba16f[surfaceWidth * surfaceHeight]);
        var clipMask = ClipMask.Build(scene, scene.Commands.Slice(1, 1), scratch);

        var buffer = new Rgba16f[surfaceWidth * surfaceHeight];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = Rgba16f.From(0, 0, 0, 1);
        var fb = new Framebuffer(surfaceWidth, surfaceHeight, surfaceWidth, buffer);

        StripRenderer.Render(scene, strips, grid, fb, clipMask);

        for (int y = 0; y < surfaceHeight; y++)
        {
            for (int x = 0; x < surfaceWidth; x++)
            {
                float r = (float)buffer[y * surfaceWidth + x].R;
                bool inside = x >= 4 && x < 12 && y >= 4 && y < 12;
                if (inside && r < 0.8f)
                    throw new InvalidOperationException($"Expected red pixel inside clip at ({x},{y}), got R={r}");
            }
        }
    }

    private static BezPath CreateRectPath(double x0, double y0, double x1, double y1)
    {
        using var builder = BezPathBuilder.Begin();
        builder.MoveTo(new Point(x0, y0));
        builder.LineTo(new Point(x1, y0));
        builder.LineTo(new Point(x1, y1));
        builder.LineTo(new Point(x0, y1));
        builder.Close();
        return builder.Build();
    }
}
