using System;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Geometry;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;

namespace Etch.Bench.Tiling;

[MemoryDiagnoser]
public class ParallelClassificationBenchmark
{
    [Params(1, 2, 4, 8)]
    public int ThreadCount { get; set; }

    public PathCountScene PathCount { get; set; }

    public static IEnumerable<PathCountScene> AllPathCounts() => Enum.GetValues<PathCountScene>();

    [ParamsSource(nameof(AllPathCounts))]
    public PathCountScene PathCountParam
    {
        get => PathCount;
        set => PathCount = value;
    }

    private SceneBuffer? _sceneBuffer;
    private int _surfaceWidth;
    private int _surfaceHeight;

    [GlobalSetup]
    public void Setup()
    {
        (_sceneBuffer, _surfaceWidth, _surfaceHeight) = PathCount switch
        {
            PathCountScene.Paths100 => CreateScene(100),
            PathCountScene.Paths1000 => CreateScene(1000),
            PathCountScene.Paths10000 => CreateScene(10000),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static (SceneBuffer, int, int) CreateScene(int pathCount)
    {
        int width = 3840;
        int height = 2160;
        int paintId;
        int strokePaintId;
        int xformId = 0;
        var strokeStyle = default(StrokeStyle);

        var sb = SceneBuilder.Begin(pathCount * 128);

        sb.BeginFrame();

        paintId = sb.AddPaint(Paint.Solid(0xFF804080));
        strokePaintId = sb.AddPaint(Paint.Solid(0xFF202020));
        xformId = sb.AddTransform(Affine.Identity);

        int seed = 42;
        int state = seed;

        int halfPaths = pathCount / 2;
        for (int i = 0; i < halfPaths; i++)
        {
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int cx = state % width;
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int cy = state % height;
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int radius = 20 + (state % 61);

            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int segments = 3 + (state % 4);

            var pathBuilder = BezPathBuilder.Begin(64);
            double firstX = cx + radius;
            double firstY = cy;
            pathBuilder.MoveTo(new Point(firstX, firstY));

            for (int j = 1; j <= segments; j++)
            {
                double angle = (j * 2.0 * Math.PI) / segments;
                double px = cx + radius * Math.Cos(angle);
                double py = cy + radius * Math.Sin(angle);
                pathBuilder.LineTo(new Point(px, py));
            }
            pathBuilder.Close();
            int pathId = sb.AddPath(pathBuilder.Build());
            pathBuilder.Dispose();
            sb.FillPath(pathId, paintId, xformId, FillRule.NonZero);
        }

        for (int i = 0; i < halfPaths; i++)
        {
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int x1 = state % width;
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int y1 = state % height;
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int x2 = (x1 + 50 + state % 200) % width;
            state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
            int y2 = (y1 + 50 + state % 200) % height;

            var pathBuilder = BezPathBuilder.Begin(16);
            pathBuilder.MoveTo(new Point(x1, y1));
            pathBuilder.LineTo(new Point(x2, y2));
            int linePathId = sb.AddPath(pathBuilder.Build());
            pathBuilder.Dispose();
            sb.StrokePath(linePathId, strokePaintId, xformId, 2.0f, strokeStyle);
        }

        sb.EndFrame();
        var scene = sb.End();
        return (scene, width, height);
    }

    [Benchmark]
    [AllocationBudget(0)]
    public void ClassifyAndEmit()
    {
        RunOnce<TTile16>();
    }

    private void RunOnce<TTile>() where TTile : struct, ITileSize
    {
        ArgumentNullException.ThrowIfNull(_sceneBuffer);

        var grid = new TileGrid<TTile>(_surfaceWidth, _surfaceHeight);
        var classified = ParallelClassifier.Classify(_sceneBuffer, grid, ThreadCount);
        var strips = StripEmitter.Emit(_sceneBuffer, classified, grid);
    }
}

public enum PathCountScene
{
    Paths100,
    Paths1000,
    Paths10000,
}