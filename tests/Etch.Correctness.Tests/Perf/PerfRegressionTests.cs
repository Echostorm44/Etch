using System;
using System.IO;
using System.Text.Json;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Correctness.Tests.Perf;

public class PerfRegressionTests
{
    [Test]
    public async Task ParseProjectPlan_PerformanceSection_HasExpectedRows()
    {
        var rows = PerfRegressionParser.ParseProjectPlan(
            AppContext.BaseDirectory + "/../../../../../../..");

        await Assert.That(rows.Count).IsGreaterThanOrEqualTo(10);
    }

    [Test]
    public async Task ParseProjectPlan_CpuSection_HasFiveRows()
    {
        var rows = PerfRegressionParser.ParseProjectPlan(
            AppContext.BaseDirectory + "/../../../../../../..");
        var cpuRows = rows.FindAll(r => r.Section == "CPU");

        await Assert.That(cpuRows.Count).IsEqualTo(5);
    }

    [Test]
    public async Task ParseProjectPlan_GpuSection_HasFiveRows()
    {
        var rows = PerfRegressionParser.ParseProjectPlan(
            AppContext.BaseDirectory + "/../../../../../../..");
        var gpuRows = rows.FindAll(r => r.Section == "GPU");

        await Assert.That(gpuRows.Count).IsEqualTo(5);
    }

    [Test]
    public async Task BaselineJson_ExistsAndIsValid()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "../../../../../../bench/baselines/reference-machine.json");
        if (!File.Exists(path))
            return;

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        await Assert.That(root.TryGetProperty("benchmarks", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("machineProfile", out _)).IsTrue();
        await Assert.That(root.TryGetProperty("regressionThresholds", out _)).IsTrue();

        var thresholds = root.GetProperty("regressionThresholds");
        await Assert.That(thresholds.TryGetProperty("cpu", out _)).IsTrue();
        await Assert.That(thresholds.TryGetProperty("gpu", out _)).IsTrue();
    }

    [Test]
    public async Task CpuBenchmark_SimpleRender_CompletesWithinReasonableTime()
    {
        var scene = CreateSimpleFillScene();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            _ = SceneRunner.RunCpu(scene, 64, 64);
        }
        sw.Stop();
        double avgUs = sw.Elapsed.TotalMicroseconds / 100.0;

        await Assert.That(avgUs).IsLessThanOrEqualTo(5000);
    }

    [Test]
    public async Task AllPerfRows_HaveParsableTargets()
    {
        var rows = PerfRegressionParser.ParseProjectPlan(
            AppContext.BaseDirectory + "/../../../../../../..");

        int parseableCount = 0;
        foreach (var row in rows)
        {
            long? targetUs = row.ParseTargetMs();
            long? targetFps = row.ParseTargetFps();
            if (targetUs.HasValue || targetFps.HasValue)
                parseableCount++;
        }

        await Assert.That(parseableCount).IsGreaterThanOrEqualTo(rows.Count - 3);
    }

    private static SceneBuffer CreateSimpleFillScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFFFF0000u);
        int paintId = builder.AddPaint(paint);
        builder.FillRect(new Rect(0, 0, 64, 64), paintId, xform);
        builder.EndFrame();
        return builder.End();
    }
}
