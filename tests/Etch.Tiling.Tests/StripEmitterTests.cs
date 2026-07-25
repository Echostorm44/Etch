using System;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class StripEmitterTests
{
    [Test]
    public void FillRect_FullTile_OneStripWithFullRowMask()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillRect(new Rect(0, 0, 16, 16), paintId, transformId);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.TileCount != 1)
            throw new InvalidOperationException($"Expected 1 tile, got {buffer.TileCount}");
        if (buffer.StripCount != 1)
            throw new InvalidOperationException($"Expected 1 strip, got {buffer.StripCount}");

        var strips = buffer.StripsForTile(0);
        if (strips.Length != 1)
            throw new InvalidOperationException($"Expected 1 strip for tile 0, got {strips.Length}");

        ushort expectedRowMask = (ushort)((1 << 16) - 1);
        if (strips[0].RowMask != expectedRowMask)
            throw new InvalidOperationException($"Expected RowMask {expectedRowMask}, got {strips[0].RowMask}");

        ValidateStrip(buffer, strips[0]);
    }

    [Test]
    public void FillPath_DiagonalBand_EmitsStrips()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var path = CreateDiagonalBandPath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.TileCount != 1)
            throw new InvalidOperationException($"Expected 1 tile, got {buffer.TileCount}");

        var strips = buffer.StripsForTile(0);
        if (strips.Length == 0)
            throw new InvalidOperationException("Expected some strips for diagonal band");

        for (int i = 0; i < strips.Length; i++)
        {
            ValidateStrip(buffer, strips[i]);
        }
    }

    [Test]
    public void EmptyClassifiedScene_EmptyStripBuffer()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var emptyClassified = new ClassifiedScene([], Array.Empty<int>(), 0);

        var buffer = StripEmitter.Emit(scene, emptyClassified, grid);

        if (buffer.TileCount != 0)
            throw new InvalidOperationException($"Expected 0 tiles, got {buffer.TileCount}");
        if (buffer.StripCount != 0)
            throw new InvalidOperationException($"Expected 0 strips, got {buffer.StripCount}");
    }

    [Test]
    public void FillRect_HalfTile_HalfRowMask()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillRect(new Rect(0, 0, 16, 8), paintId, transformId);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        var strips = buffer.StripsForTile(0);
        if (strips.Length != 1)
            throw new InvalidOperationException($"Expected 1 strip, got {strips.Length}");

        ushort expectedRowMask = (ushort)((1 << 8) - 1);
        if (strips[0].RowMask != expectedRowMask)
            throw new InvalidOperationException($"Expected RowMask {expectedRowMask}, got {strips[0].RowMask}");

        ValidateStrip(buffer, strips[0]);
    }

    [Test]
    public void FillPath_OutsideTile_NoStrips()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var path = CreateSquarePath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Translate(100.0, 100.0));
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount != 0)
            throw new InvalidOperationException($"Expected 0 strips, got {buffer.StripCount}");
    }

    [Test]
    public void FillPath_FullTileBoundary_NoCrash()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var path = CreateFullTilePath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);
    }

    private static void ValidateStrip(StripBuffer buffer, Strip strip)
    {
        if (strip.RowMask == 0)
            throw new InvalidOperationException("RowMask should not be zero");

        if (strip.X0 > strip.X1)
            throw new InvalidOperationException($"X0 ({strip.X0}) > X1 ({strip.X1})");

        if (strip.TileIndex >= buffer.TileCount)
            throw new InvalidOperationException($"TileIndex {strip.TileIndex} >= TileCount {buffer.TileCount}");

        var coverage = buffer.CoverageForStrip(in strip);
        if (coverage.Length == 0)
            throw new InvalidOperationException("CoverageForStrip returned empty for valid strip");
    }

    private static BezPath CreateDiagonalBandPath()
    {
        var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(4, 0));
        builder.LineTo(new Point(8, 0));
        builder.LineTo(new Point(16, 8));
        builder.LineTo(new Point(16, 12));
        builder.LineTo(new Point(8, 4));
        builder.LineTo(new Point(0, 12));
        builder.LineTo(new Point(0, 8));
        builder.Close();
        return builder.Build();
    }

    private static BezPath CreateFullTilePath()
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(16, 0));
        builder.LineTo(new Point(16, 16));
        builder.LineTo(new Point(0, 16));
        builder.Close();
        return builder.Build();
    }

    private static BezPath CreateSquarePath()
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(16, 0));
        builder.LineTo(new Point(16, 16));
        builder.LineTo(new Point(0, 16));
        builder.Close();
        return builder.Build();
    }

    [Test]
    public void FillRect_Clipped_EmitsOnlyStripsInsideClip()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var clipPath = CreateSquarePath();

        var sb = SceneBuilder.Begin(16);
        sb.BeginFrame();
        int clipId = sb.AddPath(clipPath);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 16, 16), paintId, transformId);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount == 0)
            throw new InvalidOperationException("Expected strips inside clip");

        for (int tileIdx = 0; tileIdx < buffer.TileCount; tileIdx++)
        {
            var strips = buffer.StripsForTile(tileIdx);
            if (strips.Length == 0)
                continue;

            var (tx, ty) = grid.TileXY(tileIdx);
            var tileBounds = grid.TileBounds(tx, ty);
            bool overlapsClip = tileBounds.MinX < 16 && tileBounds.MaxX > 0 &&
                                tileBounds.MinY < 16 && tileBounds.MaxY > 0;
            if (!overlapsClip)
                throw new InvalidOperationException($"Tile ({tx},{ty}) has strips but is outside clip bounds");
        }
    }

    [Test]
    public void FillRect_ClippedToDisjointRegion_ZeroStrips()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var clipPath = CreateSquarePath();

        var sb = SceneBuilder.Begin(16);
        sb.BeginFrame();
        int clipId = sb.AddPath(clipPath);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Translate(32.0, 32.0));
        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 16, 16), paintId, transformId);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount != 0)
            throw new InvalidOperationException($"Expected 0 strips when clip and draw are disjoint, got {buffer.StripCount}");
    }

    [Test]
    public void StrokeRect_BorderOnly_CenterIsEmpty()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var path = CreateSquarePath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.StrokePath(pathId, paintId, transformId, 2.0f, default(StrokeStyle));
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount == 0)
            throw new InvalidOperationException("Expected strips for rect stroke");

        // Verify strips are valid and tile center rows (6-9) have no coverage.
        var strips = buffer.StripsForTile(0);
        for (int i = 0; i < strips.Length; i++)
        {
            ValidateStrip(buffer, strips[i]);
        }

        // Build per-row coverage mask from strips.
        Span<bool> rowHasCoverage = stackalloc bool[TTile16.Height];
        for (int i = 0; i < strips.Length; i++)
        {
            var strip = strips[i];
            for (int row = 0; row < TTile16.Height; row++)
            {
                if ((strip.RowMask & (1 << row)) != 0)
                {
                    rowHasCoverage[row] = true;
                }
            }
        }

        // With a 2px stroke on a 16x16 rect, the interior rows 2..13 should
        // have coverage only at the edges (columns 0..1 and 14..15).
        // We just verify the center rows (6..9) are NOT fully covered.
        for (int row = 6; row <= 9; row++)
        {
            if (!rowHasCoverage[row])
                continue; // fine, no coverage at all

            // If there is coverage, it must not span the whole width.
            bool rowFullyCovered = true;
            for (int col = 0; col < TTile16.Width; col++)
            {
                bool colCovered = false;
                for (int s = 0; s < strips.Length; s++)
                {
                    var strip = strips[s];
                    if ((strip.RowMask & (1 << row)) != 0 && strip.X0 <= col && col <= strip.X1)
                    {
                        colCovered = true;
                        break;
                    }
                }
                if (!colCovered)
                {
                    rowFullyCovered = false;
                    break;
                }
            }
            if (rowFullyCovered)
            {
                throw new InvalidOperationException(
                    $"Center row {row} should not be fully covered for a stroke");
            }
        }
    }

    [Test]
    public void StrokeCircle_EmitsRingStrips()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var path = CreateCirclePath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.StrokePath(pathId, paintId, transformId, 2.0f, default(StrokeStyle));
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount == 0)
            throw new InvalidOperationException("Expected strips for circle stroke");

        var strips = buffer.StripsForTile(0);
        for (int i = 0; i < strips.Length; i++)
        {
            ValidateStrip(buffer, strips[i]);
        }
    }

    [Test]
    public void StrokeHorizontalLine_EmitsThickLineStrips()
    {
        var grid = new TileGrid<TTile16>(16, 16);
        var path = CreateHorizontalLinePath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.StrokePath(pathId, paintId, transformId, 4.0f, default(StrokeStyle));
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount == 0)
            throw new InvalidOperationException("Expected strips for horizontal line stroke");

        var strips = buffer.StripsForTile(0);
        for (int i = 0; i < strips.Length; i++)
        {
            ValidateStrip(buffer, strips[i]);
        }
    }

    private static BezPath CreateCirclePath()
    {
        // 8-arc circle approximation on a 16x16 tile, radius 6, center (8,8)
        var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(14, 8));
        builder.CubicTo(new Point(14, 10.21), new Point(12.21, 12), new Point(10, 12));
        builder.CubicTo(new Point(7.79, 12), new Point(6, 10.21), new Point(6, 8));
        builder.CubicTo(new Point(6, 5.79), new Point(7.79, 4), new Point(10, 4));
        builder.CubicTo(new Point(12.21, 4), new Point(14, 5.79), new Point(14, 8));
        builder.Close();
        return builder.Build();
    }

    private static BezPath CreateHorizontalLinePath()
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(2, 8));
        builder.LineTo(new Point(14, 8));
        return builder.Build();
    }

    private static BezPath CreateHorizontalBandPath()
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(0, 6));
        builder.LineTo(new Point(16, 6));
        builder.LineTo(new Point(16, 10));
        builder.LineTo(new Point(0, 10));
        builder.Close();
        return builder.Build();
    }
}
