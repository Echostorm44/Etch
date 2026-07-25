using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Etch.Bench.Shared;
using Etch.Geometry;
using Etch.Raster.Cpu;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;
using Etch.Tiling.Strips;

namespace Etch.Bench.Cpu;

[MemoryDiagnoser]
[CategoriesColumn]
public class CpuPerfGateBenchmark
{
    private const int SurfaceWidth = 1920;
    private const int SurfaceHeight = 1080;

    private TileGrid<TTile8> _grid;
    private Rgba16f[] _framebuffer = null!;
    private Framebuffer _fb;

    private SceneBuffer _singleRectScene = null!;
    private ClassifiedScene _singleRectClassified;

    private SceneBuffer _thousandRectsScene = null!;
    private ClassifiedScene _thousandRectsClassified;
    private StripBuffer _thousandRectsStrips = null!;

    private SceneBuffer _thousandBlendedRectsScene = null!;
    private ClassifiedScene _thousandBlendedRectsClassified;
    private StripBuffer _thousandBlendedRectsStrips = null!;

    private SceneBuffer _fiveHundredPathsScene = null!;
    private ClassifiedScene _fiveHundredPathsClassified;
    private StripBuffer _fiveHundredPathsStrips = null!;

    [GlobalSetup]
    public void Setup()
    {
        _grid = new TileGrid<TTile8>(SurfaceWidth, SurfaceHeight);
        _framebuffer = new Rgba16f[SurfaceWidth * SurfaceHeight];
        _fb = new Framebuffer(SurfaceWidth, SurfaceHeight, SurfaceWidth, _framebuffer);

        (_singleRectScene, _singleRectClassified) = BuildSingleRectScene();

        (_thousandRectsScene, _thousandRectsClassified, _thousandRectsStrips) =
            BuildThousandRectsScene(useBlending: false);

        (_thousandBlendedRectsScene, _thousandBlendedRectsClassified, _thousandBlendedRectsStrips) =
            BuildThousandRectsScene(useBlending: true);

        (_fiveHundredPathsScene, _fiveHundredPathsClassified, _fiveHundredPathsStrips) =
            BuildFiveHundredPathsScene();
    }

    [Benchmark]
    [AllocationBudget(1)]
    [BenchmarkCategory("Regression")]
    public void Render1080pRedRect()
    {
        Array.Clear(_framebuffer, 0, _framebuffer.Length);
        SolidFillRenderer.Render(_singleRectScene, _singleRectClassified, _grid, _fb);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Regression")]
    public void Render1080p1000SolidRects()
    {
        Array.Clear(_framebuffer, 0, _framebuffer.Length);
        StripRenderer.Render(_thousandRectsScene, _thousandRectsStrips, _grid, _fb);
    }

    [Benchmark]
    [BenchmarkCategory("Regression")]
    public void Render1080p1000AlphaBlendedRects()
    {
        for (int i = 0; i < _framebuffer.Length; i++)
            _framebuffer[i] = Rgba16f.From(0.5f, 0.5f, 0.5f, 0.5f);
        StripRenderer.Render(_thousandBlendedRectsScene, _thousandBlendedRectsStrips, _grid, _fb);
    }

    [Benchmark]
    [BenchmarkCategory("Regression")]
    public void Render1080p500AntiAliasedPaths()
    {
        Array.Clear(_framebuffer, 0, _framebuffer.Length);
        StripRenderer.Render(_fiveHundredPathsScene, _fiveHundredPathsStrips, _grid, _fb);
    }

    [Benchmark]
    [BenchmarkCategory("Regression")]
    public void Render1080pFullScreenAAGradientPlaceholder()
    {
        Array.Clear(_framebuffer, 0, _framebuffer.Length);
    }

    private static (SceneBuffer scene, ClassifiedScene classified) BuildSingleRectScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
        var transformId = builder.AddTransform(Affine.Identity);
        builder.FillRect(new Rect(0, 0, SurfaceWidth, SurfaceHeight), paintId, transformId);
        builder.EndFrame();
        var scene = builder.End();

        var tileCount = (SurfaceWidth / 8) * (SurfaceHeight / 8);
        var entries = new ClassificationEntry[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            entries[i] = new ClassificationEntry(i, 0, ClassificationKind.FillRect, default);
        }
        var offsets = new int[tileCount + 1];
        for (int i = 0; i < tileCount; i++)
            offsets[i] = i;
        offsets[tileCount] = tileCount;

        var classified = new ClassifiedScene(entries, offsets, tileCount);
        return (scene, classified);
    }

    private static (SceneBuffer scene, ClassifiedScene classified, StripBuffer strips) BuildThousandRectsScene(bool useBlending)
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();

        uint baseColor = useBlending ? 0x80FF0000 : 0xFFFF0000;

        for (int i = 0; i < 1000; i++)
        {
            int x = (i * 37) % (SurfaceWidth - 200);
            int y = (i * 53) % (SurfaceHeight - 100);
            var paintId = builder.AddPaint(Paint.Solid(baseColor));
            var transformId = builder.AddTransform(Affine.Identity);
            builder.FillRect(new Rect(x, y, x + 200, y + 100), paintId, transformId);
        }

        builder.EndFrame();
        var scene = builder.End();

        var accum = new ClassificationAccumulator(4096);
        BBoxClassifier.Classify(scene, new TileGrid<TTile8>(SurfaceWidth, SurfaceHeight), ref accum);
        var entriesSpan = accum.Finish().ToArray();
        accum.Dispose();

        Array.Sort(entriesSpan, new TileOrderComparer());

        var grid = new TileGrid<TTile8>(SurfaceWidth, SurfaceHeight);
        var offsets = new int[grid.TotalTiles + 1];
        int cursor = 0;
        for (int t = 0; t < grid.TotalTiles; t++)
        {
            offsets[t] = cursor;
            while (cursor < entriesSpan.Length && entriesSpan[cursor].TileIndex == t)
            {
                cursor++;
            }
        }
        offsets[grid.TotalTiles] = entriesSpan.Length;

        var classified = new ClassifiedScene(entriesSpan, offsets, grid.TotalTiles);
        var strips = StripEmitter.Emit(scene, classified, grid);

        return (scene, classified, strips);
    }

    private static (SceneBuffer scene, ClassifiedScene classified, StripBuffer strips) BuildFiveHundredPathsScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();

        for (int i = 0; i < 500; i++)
        {
            int seed = i * 31337;
            int x0 = (seed % (SurfaceWidth - 400)) + 100;
            int y0 = (seed % (SurfaceHeight - 400)) + 100;
            int x1 = ((seed + 100) % (SurfaceWidth - 400)) + 100;
            int y1 = ((seed + 200) % (SurfaceHeight - 400)) + 100;
            int x2 = ((seed + 300) % (SurfaceWidth - 400)) + 100;
            int y2 = ((seed + 400) % (SurfaceHeight - 400)) + 100;

            var path = new BezPath(
                new byte[] { (byte)PathVerb.MoveTo, (byte)PathVerb.LineTo, (byte)PathVerb.LineTo, (byte)PathVerb.Close },
                new double[] { x0, y0, x1, y0, x2, y2 },
                4);
            var pathId = builder.AddPath(path);
            var paintId = builder.AddPaint(Paint.Solid(0xFFFF0000));
            var transformId = builder.AddTransform(Affine.Identity);
            builder.FillPath(pathId, paintId, transformId, FillRule.NonZero);
        }

        builder.EndFrame();
        var scene = builder.End();

        var accum = new ClassificationAccumulator(8192);
        BBoxClassifier.Classify(scene, new TileGrid<TTile8>(SurfaceWidth, SurfaceHeight), ref accum);
        var entriesSpan = accum.Finish().ToArray();
        accum.Dispose();

        Array.Sort(entriesSpan, new TileOrderComparer());

        var grid = new TileGrid<TTile8>(SurfaceWidth, SurfaceHeight);
        var offsets = new int[grid.TotalTiles + 1];
        int cursor = 0;
        for (int t = 0; t < grid.TotalTiles; t++)
        {
            offsets[t] = cursor;
            while (cursor < entriesSpan.Length && entriesSpan[cursor].TileIndex == t)
            {
                cursor++;
            }
        }
        offsets[grid.TotalTiles] = entriesSpan.Length;

        var classified = new ClassifiedScene(entriesSpan, offsets, grid.TotalTiles);
        var strips = StripEmitter.Emit(scene, classified, grid);

        return (scene, classified, strips);
    }

    private sealed class TileOrderComparer : global::System.Collections.Generic.IComparer<ClassificationEntry>
    {
        public int Compare(ClassificationEntry x, ClassificationEntry y)
        {
            int tileCompare = x.TileIndex - y.TileIndex;
            if (tileCompare != 0)
                return tileCompare;
            return x.CommandOrder - y.CommandOrder;
        }
    }
}
