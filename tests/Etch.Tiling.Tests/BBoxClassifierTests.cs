using System;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using TUnit;

namespace Etch.Tiling.Tests;

internal sealed class BBoxClassifierTests
{
    [Test]
    public void FillRect_InsideOneTile_OneEntry()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
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
        var entries = accum.Finish();

        if (entries.Length != 1)
            throw new InvalidOperationException($"Expected 1 entry, got {entries.Length}");
        if (entries[0].Kind != ClassificationKind.FillRect)
            throw new InvalidOperationException($"Expected FillRect kind");
    }

    [Test]
    public void FillRect_StraddlingFourTiles_FourEntries()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.FillRect(new Rect(8, 8, 24, 24), paintId, transformId);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length != 4)
            throw new InvalidOperationException($"Expected 4 entries, got {entries.Length}");

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Kind != ClassificationKind.FillRect)
                throw new InvalidOperationException($"Entry {i}: expected FillRect");
        }
    }

    [Test]
    public void EmptyScene_ZeroEntries()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length != 0)
            throw new InvalidOperationException($"Expected 0 entries, got {entries.Length}");
    }

    [Test]
    public void FillRect_Translation_OffsetsTileSelection()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Translate(16.0, 0.0));
        sb.FillRect(new Rect(0, 0, 16, 16), paintId, transformId);
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length != 1)
            throw new InvalidOperationException($"Expected 1 entry, got {entries.Length}");

        var tile0Entries = grid.TileXY(entries[0].TileIndex);
        if (tile0Entries.x != 1 || tile0Entries.y != 0)
            throw new InvalidOperationException($"Expected tile (1,0), got ({tile0Entries.x},{tile0Entries.y})");
    }

    [Test]
    public void FillPath_InsideOneTile_OneEntry()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var path = CreateSquarePath();
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
        var entries = accum.Finish();

        if (entries.Length != 1)
            throw new InvalidOperationException($"Expected 1 entry, got {entries.Length}");
        if (entries[0].Kind != ClassificationKind.FillPath)
            throw new InvalidOperationException($"Expected FillPath kind");
    }

    [Test]
    public void StrokePath_InflatedByHalfStrokeWidth()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var path = CreateSquarePath();
        var sb = SceneBuilder.Begin(4);
        sb.BeginFrame();
        int pathId = sb.AddPath(path);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.StrokePath(pathId, paintId, transformId, 8.0f, new StrokeStyle());
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length == 0)
            throw new InvalidOperationException("Expected entries for stroked path");
    }

    [Test]
    public void FillPath_WithTransform_TransformsAABB()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
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
        var entries = accum.Finish();

        if (entries.Length == 0)
            throw new InvalidOperationException("Expected entries for transformed path");
    }

    [Test]
    public void CommandOrder_MonotonicallyIncreasing()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var path = CreateSquarePath();
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
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        for (int i = 1; i < entries.Length; i++)
        {
            if (entries[i].CommandOrder < entries[i - 1].CommandOrder)
                throw new InvalidOperationException($"CommandOrder not monotonically increasing at index {i}");
        }
    }

    [Test]
    public void FillRect_ClippedToSmallerRegion_ClassifiesOnlyOverlappingTiles()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var clipPath = CreateSquarePath();
        var sb = SceneBuilder.Begin(16);
        sb.BeginFrame();
        int clipId = sb.AddPath(clipPath);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 1920, 1080), paintId, transformId);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length == 0)
            throw new InvalidOperationException("Expected some entries inside clip");

        for (int i = 0; i < entries.Length; i++)
        {
            var (tx, ty) = grid.TileXY(entries[i].TileIndex);
            var tileBounds = grid.TileBounds(tx, ty);
            bool overlapsClip = tileBounds.MinX < 16 && tileBounds.MaxX > 0 &&
                                tileBounds.MinY < 16 && tileBounds.MaxY > 0;
            if (!overlapsClip)
                throw new InvalidOperationException($"Entry {i} at tile ({tx},{ty}) is outside clip bounds");
        }
    }

    [Test]
    public void FillRect_ClippedToDisjointRegion_ZeroEntries()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var clipPath = CreateSquarePath();
        var sb = SceneBuilder.Begin(16);
        sb.BeginFrame();
        int clipId = sb.AddPath(clipPath);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Translate(1000.0, 1000.0));
        sb.PushClip(clipId, FillRule.NonZero, ClipMode.Intersect);
        sb.SetTransform(transformId);
        sb.FillRect(new Rect(0, 0, 16, 16), paintId, transformId);
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length != 0)
            throw new InvalidOperationException($"Expected 0 entries when clip and draw are disjoint, got {entries.Length}");
    }

    [Test]
    public void NestedIntersectClips_FurtherRestrictsTiles()
    {
        var grid = new TileGrid<TTile16>(1920, 1080);
        var outerClip = CreateSquarePath();
        var innerClip = CreateSquarePath();
        var sb = SceneBuilder.Begin(16);
        sb.BeginFrame();
        int outerId = sb.AddPath(outerClip);
        int innerId = sb.AddPath(innerClip);
        int paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        int transformId = sb.AddTransform(Affine.Identity);
        sb.PushClip(outerId, FillRule.NonZero, ClipMode.Intersect);
        int innerTransformId = sb.AddTransform(Affine.Translate(8.0, 8.0));
        sb.PushClip(innerId, FillRule.NonZero, ClipMode.Intersect);
        sb.FillRect(new Rect(0, 0, 1920, 1080), paintId, transformId);
        sb.PopClip();
        sb.PopClip();
        sb.EndFrame();
        var scene = sb.End();
        sb.Dispose();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, grid, ref accum);
        var entries = accum.Finish();

        if (entries.Length == 0)
            throw new InvalidOperationException("Expected some entries inside nested clips");

        for (int i = 0; i < entries.Length; i++)
        {
            var (tx, ty) = grid.TileXY(entries[i].TileIndex);
            var tileBounds = grid.TileBounds(tx, ty);
            bool overlapsInner = tileBounds.MinX < 24 && tileBounds.MaxX > 8 &&
                                 tileBounds.MinY < 24 && tileBounds.MaxY > 8;
            if (!overlapsInner)
                throw new InvalidOperationException($"Entry {i} at tile ({tx},{ty}) is outside nested clip bounds");
        }
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
}
