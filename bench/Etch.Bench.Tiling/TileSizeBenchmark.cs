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
public class TileSizeBenchmark
{
    private SceneBuffer? _sceneBuffer;
    private int _surfaceWidth;
    private int _surfaceHeight;

    [Params(8, 16, 32)]
    public int TileSize { get; set; }

    public SceneFixture Scene { get; set; }

    public static IEnumerable<SceneFixture> AllScenes() => Enum.GetValues<SceneFixture>();

    [ParamsSource(nameof(AllScenes))]
    public SceneFixture SceneParam
    {
        get => Scene;
        set => Scene = value;
    }

    [GlobalSetup]
    public void Setup()
    {
        (_sceneBuffer, _surfaceWidth, _surfaceHeight) = Scene switch
        {
            SceneFixture.TextHeavy => CreateTextHeavyScene(),
            SceneFixture.UiChrome => CreateUiChromeScene(),
            SceneFixture.VectorArt => CreateVectorArtScene(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static (SceneBuffer, int, int) CreateTextHeavyScene()
    {
        var sb = SceneBuilder.Begin(8192);
        sb.BeginFrame();

        int width = 1920;
        int height = 1080;
        int paintId = sb.AddPaint(Paint.Solid(0xFF804080));
        int xformId = sb.AddTransform(Affine.Identity);

        for (int i = 0; i < 5000; i++)
        {
            int px = (i * 17) % (width - 12);
            int py = (i * 31) % (height - 12);
            var rect = Rect.FromLTRB(px, py, px + 12.0, py + 12.0);
            sb.FillRect(rect, paintId, xformId);
        }

        sb.EndFrame();
        var scene = sb.End();
        return (scene, width, height);
    }

    private static (SceneBuffer, int, int) CreateUiChromeScene()
    {
        var sb = SceneBuilder.Begin(8192);
        sb.BeginFrame();

        int width = 1920;
        int height = 1080;
        int buttonPaintId = sb.AddPaint(Paint.Solid(0xFF404080));
        int strokePaintId = sb.AddPaint(Paint.Solid(0xFF202020));
        int xformId = sb.AddTransform(Affine.Identity);
        var strokeStyle = default(StrokeStyle);

        for (int i = 0; i < 500; i++)
        {
            int bx = (i * 37) % (width - 100);
            int by = (i * 53) % (height - 40);
            var buttonRect = Rect.FromLTRB(bx, by, bx + 100.0, by + 40.0);
            sb.FillRect(buttonRect, buttonPaintId, xformId);

            var pathBuilder = BezPathBuilder.Begin(8);
            pathBuilder.MoveTo(new Point(bx, by));
            pathBuilder.LineTo(new Point(bx + 100.0, by));
            pathBuilder.LineTo(new Point(bx + 100.0, by + 40.0));
            pathBuilder.LineTo(new Point(bx, by + 40.0));
            pathBuilder.Close();
            int borderPathId = sb.AddPath(pathBuilder.Build());
            pathBuilder.Dispose();
            sb.StrokePath(borderPathId, strokePaintId, xformId, 1.0f, strokeStyle);
        }

        for (int i = 0; i < 2000; i++)
        {
            int x1 = (i * 19) % width;
            int y1 = (i * 41) % height;
            int x2 = (i * 23 + 50) % width;
            int y2 = (i * 29 + 50) % height;

            var pathBuilder = BezPathBuilder.Begin(16);
            pathBuilder.MoveTo(new Point(x1, y1));
            pathBuilder.LineTo(new Point(x2, y2));
            int linePathId = sb.AddPath(pathBuilder.Build());
            pathBuilder.Dispose();
            sb.StrokePath(linePathId, strokePaintId, xformId, 1.0f, strokeStyle);
        }

        sb.EndFrame();
        var scene = sb.End();
        return (scene, width, height);
    }

    private static (SceneBuffer, int, int) CreateVectorArtScene()
    {
        var sb = SceneBuilder.Begin(8192);
        sb.BeginFrame();

        int width = 1920;
        int height = 1080;
        int paintId = sb.AddPaint(Paint.Solid(0xFF6080A0));
        int xformId = sb.AddTransform(Affine.Identity);
        var fillRule = FillRule.NonZero;

        int seed = 42;
        int state = seed;

        for (int i = 0; i < 200; i++)
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
            sb.FillPath(pathId, paintId, xformId, fillRule);
        }

        sb.EndFrame();
        var scene = sb.End();
        return (scene, width, height);
    }

    [Benchmark]
    [AllocationBudget(0)]
    public void ClassifyAndEmit()
    {
        switch (TileSize)
        {
            case 8:
                RunOnce<TTile8>();
                break;
            case 16:
                RunOnce<TTile16>();
                break;
            case 32:
                RunOnce<TTile32>();
                break;
        }
    }

    private void RunOnce<TTile>() where TTile : struct, ITileSize
    {
        var grid = new TileGrid<TTile>(_surfaceWidth, _surfaceHeight);
        var accum = new ClassificationAccumulator(grid.TotalTiles);

        ArgumentNullException.ThrowIfNull(_sceneBuffer);
        BBoxClassifier.Classify(_sceneBuffer, grid, ref accum);

        var classifiedSpan = accum.Finish();
        var entries = classifiedSpan.ToArray();
        var offsets = new int[grid.TotalTiles + 1];

        for (int t = 0; t < grid.TotalTiles; t++)
        {
            offsets[t + 1] = offsets[t];
            foreach (var entry in classifiedSpan)
            {
                if (entry.TileIndex == t)
                    offsets[t + 1]++;
            }
        }

        var classified = new ClassifiedScene(entries, offsets, grid.TotalTiles);
        var strips = StripEmitter.Emit(_sceneBuffer, classified, grid);

        accum.Dispose();
    }
}

public enum SceneFixture
{
    TextHeavy,
    UiChrome,
    VectorArt,
}
