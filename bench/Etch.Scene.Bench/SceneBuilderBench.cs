using System;
using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.Scene.Bench;

[MemoryDiagnoser]
public class SceneBuilderBench
{
    private BezPath _path;
    private int _pathId, _paintId, _transformId;

    [GlobalSetup]
    public void Setup()
    {
        var builder = BezPathBuilder.Begin();
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        builder.LineTo(new Point(0, 10));
        builder.Close();
        _path = builder.Build();

        _pathId = 0;
        _paintId = 0;
        _transformId = 0;
    }

    [Benchmark(OperationsPerInvoke = 100_000)]
    [AllocationBudget(0)]
    public void FillPathHot()
    {
        var sb = SceneBuilder.Begin(estimatedCommands: 100_100);
        sb.BeginFrame();
        _pathId = sb.AddPath(_path);
        _paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        _transformId = sb.AddTransform(Affine.Identity);
        for (int i = 0; i < 100_000; i++)
            sb.FillPath(_pathId, _paintId, _transformId, FillRule.NonZero);
        sb.EndFrame();
        _ = sb.End();
    }

    [Benchmark(OperationsPerInvoke = 100_000)]
    [AllocationBudget(0)]
    public void StrokePathHot()
    {
        var sb = SceneBuilder.Begin(estimatedCommands: 100_100);
        sb.BeginFrame();
        _pathId = sb.AddPath(_path);
        _paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        _transformId = sb.AddTransform(Affine.Identity);
        for (int i = 0; i < 100_000; i++)
            sb.StrokePath(_pathId, _paintId, _transformId, 1.0f, new StrokeStyle());
        sb.EndFrame();
        _ = sb.End();
    }

    [Benchmark(OperationsPerInvoke = 10_000)]
    [AllocationBudget(0)]
    public void MixedOpsHot()
    {
        var sb = SceneBuilder.Begin(estimatedCommands: 50_050);
        sb.BeginFrame();
        _pathId = sb.AddPath(_path);
        _paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        _transformId = sb.AddTransform(Affine.Identity);
        for (int i = 0; i < 10_000; i++)
        {
            sb.SetTransform(_transformId);
            sb.PushLayer(Rect.FromLTRB(0, 0, 100, 100), 1.0f, BlendMode.SrcOver);
            sb.FillPath(_pathId, _paintId, _transformId, FillRule.NonZero);
            sb.PopLayer();
        }
        sb.EndFrame();
        _ = sb.End();
    }
}