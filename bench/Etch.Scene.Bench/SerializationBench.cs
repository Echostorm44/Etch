using System;
using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Geometry;
using Etch.Scene;
using Etch.Scene.Serialization;

namespace Etch.Scene.Bench;

[MemoryDiagnoser]
public class SerializationBench
{
    private BezPath _path;
    private int _pathId, _paintId, _transformId;
    private byte[] _serializedBuffer = null!;
    private SceneBuffer _sceneBuffer = null!;

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

        var sb = SceneBuilder.Begin(estimatedCommands: 1_100);
        sb.BeginFrame();
        _pathId = sb.AddPath(_path);
        _paintId = sb.AddPaint(Paint.Solid(0xFFFF0000));
        _transformId = sb.AddTransform(Affine.Identity);
        for (int i = 0; i < 1_000; i++)
            sb.FillPath(_pathId, _paintId, _transformId, FillRule.NonZero);
        sb.EndFrame();
        _sceneBuffer = sb.End();

        int size = SceneWriter.GetRequiredSize(_sceneBuffer);
        _serializedBuffer = new byte[size];
    }

    [Benchmark(OperationsPerInvoke = 1000)]
    [AllocationBudget(0)]
    public void WriteHot()
    {
        for (int i = 0; i < 1000; i++)
        {
            _serializedBuffer.AsSpan().Clear();
            SceneWriter.Write(_sceneBuffer, _serializedBuffer);
        }
    }

    [Benchmark(OperationsPerInvoke = 1000)]
    [AllocationBudget(0)]
    public void ReadHot()
    {
        for (int i = 0; i < 1000; i++)
        {
            var buffer = SceneWriter.Write(_sceneBuffer, _serializedBuffer);
            _ = SceneReader.Read(_serializedBuffer.AsSpan(..buffer));
        }
    }

    [Benchmark(OperationsPerInvoke = 1000)]
    [AllocationBudget(0)]
    public void RoundTripHot()
    {
        for (int i = 0; i < 1000; i++)
        {
            var size = SceneWriter.Write(_sceneBuffer, _serializedBuffer);
            _ = SceneReader.Read(_serializedBuffer.AsSpan(..size));
        }
    }
}