using System;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class LineWalkClassifierTests
{
    [Test]
    public void SupercoverDda_DiagonalLine_ReportsTiles()
    {
        var indices = new int[256];

        int count = SupercoverDda.Walk(
            new Point(0, 0),
            new Point(256, 256),
            4, 4,
            indices);

        if (count == 0)
            throw new InvalidOperationException("Expected tiles to be reported");
    }

    [Test]
    public void SupercoverDda_HorizontalLine_ReportsTiles()
    {
        var indices = new int[256];

        int count = SupercoverDda.Walk(
            new Point(0, 8),
            new Point(32, 8),
            4, 4,
            indices);

        if (count < 1)
            throw new InvalidOperationException($"Expected tiles for horizontal line, got {count}");
    }

    [Test]
    public void SupercoverDda_VerticalLine_ReportsTiles()
    {
        var indices = new int[256];

        int count = SupercoverDda.Walk(
            new Point(8, 0),
            new Point(8, 32),
            4, 4,
            indices);

        if (count < 1)
            throw new InvalidOperationException($"Expected tiles for vertical line, got {count}");
    }

    [Test]
    public void LineWalkClassifier_DiagonalLine_ReportsTiles()
    {
        var grid = new TileGrid<TTile16>(256, 256);
        var path = CreateDiagonalPath();
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        LineWalkClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length == 0)
            throw new InvalidOperationException("Expected tiles to be reported");
    }

    [Test]
    public void LineWalkClassifier_FilledRect_SameAsBbox()
    {
        var grid = new TileGrid<TTile16>(64, 64);
        var path = CreateRectPath(32, 32);
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accumLineWalk = new ClassificationAccumulator(4096);
        LineWalkClassifier.Classify(scene, grid, ref accumLineWalk);
        var lwEntries = accumLineWalk.Finish();

        var accumBbox = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accumBbox);
        var bboxEntries = accumBbox.Finish();

        if (lwEntries.Length != bboxEntries.Length)
            throw new InvalidOperationException($"LineWalk ({lwEntries.Length}) should match Bbox ({bboxEntries.Length}) for filled rect");
    }

    [Test]
    public void LineWalkClassifier_EmptyScene_ZeroEntries()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        LineWalkClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length != 0)
            throw new InvalidOperationException($"Expected 0 entries, got {entries.Length}");
    }

    [Test]
    public void LineWalkClassifier_CommandOrder_MonotonicallyIncreasing()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var path = CreateRectPath(16, 16);
        var sb = SceneBuilder.Begin(8);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        LineWalkClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        for (int i = 1; i < entries.Length; i++)
        {
            if (entries[i].CommandOrder < entries[i - 1].CommandOrder)
                throw new InvalidOperationException($"CommandOrder not monotonically increasing at index {i}");
        }
    }

    private static BezPath CreateDiagonalPath()
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(256, 256));
        builder.Close();
        return builder.Build();
    }

    private static BezPath CreateRectPath(double width, double height)
    {
        var builder = BezPathBuilder.Begin(4);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(width, 0));
        builder.LineTo(new Point(width, height));
        builder.LineTo(new Point(0, height));
        builder.Close();
        return builder.Build();
    }
}
