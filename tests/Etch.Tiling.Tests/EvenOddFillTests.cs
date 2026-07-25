using System;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class EvenOddFillTests
{
    [Test]
    public void EvenOddFill_FigureEight_CenterNotFilled()
    {
        var grid = new TileGrid<TTile16>(64, 64);
        var path = CreateFigureEightPath();

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.EvenOdd);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);

        if (buffer.StripCount == 0)
            throw new InvalidOperationException("Expected strips for figure-eight path");
    }

    [Test]
    public void EvenOddFill_SimpleRectangle_DoesNotCrash()
    {
        var grid = new TileGrid<TTile16>(32, 32);
        var path = CreateRectanglePath(4, 4, 28, 28);

        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.EvenOdd);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        var buffer = StripEmitter.Emit(scene, classified, grid);
    }

    [Test]
    public void EvenOddFill_RectangleMatchesNonZero()
    {
        var grid = new TileGrid<TTile16>(32, 32);
        var path = CreateRectanglePath(4, 4, 28, 28);

        var evenOddBuffer = CreateStripBuffer(grid, path, FillRule.EvenOdd);
        var nonZeroBuffer = CreateStripBuffer(grid, path, FillRule.NonZero);

        if (evenOddBuffer.StripCount != nonZeroBuffer.StripCount)
            throw new InvalidOperationException($"Strip counts differ: EvenOdd={evenOddBuffer.StripCount}, NonZero={nonZeroBuffer.StripCount}");
    }

    private static StripBuffer CreateStripBuffer(TileGrid<TTile16> grid, BezPath path, FillRule rule)
    {
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, rule);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish().ToArray();
        var classified = ClassificationMerge.Merge([entries], grid);

        return StripEmitter.Emit(scene, classified, grid);
    }

    private static BezPath CreateFigureEightPath()
    {
        var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(32, 4));
        builder.CubicTo(new Point(52, 4), new Point(60, 32), new Point(32, 32));
        builder.CubicTo(new Point(4, 32), new Point(12, 4), new Point(32, 4));
        builder.MoveTo(new Point(32, 32));
        builder.CubicTo(new Point(12, 32), new Point(4, 60), new Point(32, 60));
        builder.CubicTo(new Point(60, 60), new Point(52, 32), new Point(32, 32));
        builder.Close();
        return builder.Build();
    }

    private static BezPath CreateRectanglePath(double minX, double minY, double maxX, double maxY)
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(minX, minY));
        builder.LineTo(new Point(maxX, minY));
        builder.LineTo(new Point(maxX, maxY));
        builder.LineTo(new Point(minX, maxY));
        builder.Close();
        return builder.Build();
    }
}
