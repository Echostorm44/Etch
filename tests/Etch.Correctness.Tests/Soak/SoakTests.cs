using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Correctness.Tests.Soak;

public class SoakTests
{
    private const int FrameBudgetMs = 16;
    private const int SmokeDurationSeconds = 10;
    private const int SoakDurationHours = 24;

    [Test]
    public async Task Soak_Smoke_ShortRun_CompletesWithoutPanic()
    {
        var results = RunSoak(TimeSpan.FromSeconds(SmokeDurationSeconds));
        await Assert.That(results.FrameCount).IsGreaterThan(0);
        await Assert.That(results.Panics).IsEqualTo(0);
        await Assert.That(results.GcSpikes).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task Soak_Smoke_FrameTimeIsReasonable()
    {
        var results = RunSoak(TimeSpan.FromSeconds(SmokeDurationSeconds));
        await Assert.That(results.P99FrameMs).IsLessThanOrEqualTo(FrameBudgetMs * 3);
    }

    [Test]
    public async Task Soak_Smoke_GcSpikesWithinLimits()
    {
        var results = RunSoak(TimeSpan.FromSeconds(SmokeDurationSeconds));
        foreach (var spike in results.GcPausesMs)
            await Assert.That(spike).IsLessThanOrEqualTo(200);
    }

    [Test]
    public async Task Soak_Smoke_ReportIsEmitted()
    {
        var results = RunSoak(TimeSpan.FromSeconds(SmokeDurationSeconds));
        string json = SerializeSoakResult(results);
        await Assert.That(json.Length).IsGreaterThan(0);

        string reportPath = Path.GetTempFileName() + ".json";
        await File.WriteAllTextAsync(reportPath, json);
        await Assert.That(File.Exists(reportPath)).IsTrue();
        File.Delete(reportPath);
    }

    [Test]
    public async Task Soak_Nightly_24Hour_CompletesOrSkips()
    {
        string? nightly = Environment.GetEnvironmentVariable("ETCH_SOAK_NIGHTLY");
        if (nightly != "1")
            return;

        var results = RunSoak(TimeSpan.FromHours(SoakDurationHours), sampleIntervalSeconds: 30);

        await Assert.That(results.Panics).IsEqualTo(0);
        await Assert.That(results.GcSpikes).IsEqualTo(0);
        await Assert.That(results.P99FrameMs).IsLessThanOrEqualTo(FrameBudgetMs);

        string reportPath = Path.Combine(AppContext.BaseDirectory, "soak-report.json");
        string json = SerializeSoakResult(results);
        await File.WriteAllTextAsync(reportPath, json);
    }

    private static SoakResult RunSoak(TimeSpan duration, int sampleIntervalSeconds = 1)
    {
        var scene = CreateAnimatedScene();
        var result = new SoakResult
        {
            StartTime = DateTime.UtcNow,
            DurationHours = duration.TotalHours,
        };

        var stopwatch = Stopwatch.StartNew();
        var gcPauses = new List<double>();
        var frameTimes = new List<double>();
        int frameCount = 0;
        int panics = 0;

        long lastGcTime = GC.GetTotalPauseDuration().Ticks;
        long lastAllocBytes = GC.GetTotalAllocatedBytes(precise: false);
        long rssBaseline = Environment.WorkingSet;

        var frameTimer = new Stopwatch();
        DateTime nextSample = DateTime.UtcNow.AddSeconds(sampleIntervalSeconds);

        while (stopwatch.Elapsed < duration)
        {
            try
            {
                frameTimer.Restart();
                _ = SceneRunner.RunCpu(scene, 256, 256);
                frameTimer.Stop();
                frameCount++;

                frameTimes.Add(frameTimer.Elapsed.TotalMilliseconds);

                if (DateTime.UtcNow >= nextSample)
                {
                    long currentGcPause = GC.GetTotalPauseDuration().Ticks;
                    long deltaTicks = currentGcPause - lastGcTime;
                    if (deltaTicks > TimeSpan.TicksPerMillisecond * 50)
                    {
                        gcPauses.Add(deltaTicks / (double)TimeSpan.TicksPerMillisecond);
                        result.GcSpikes++;
                    }
                    lastGcTime = currentGcPause;
                    nextSample = DateTime.UtcNow.AddSeconds(sampleIntervalSeconds);
                }
            }
            catch (EtchException)
            {
                panics++;
                result.Panics = panics;
            }
        }

        stopwatch.Stop();

        result.FrameCount = frameCount;
        result.Panics = panics;
        result.TotalAllocBytes = GC.GetTotalAllocatedBytes(precise: false) - lastAllocBytes;
        result.RssEndBytes = Environment.WorkingSet;
        result.RssGrowthPercent = rssBaseline > 0
            ? (result.RssEndBytes - rssBaseline) / (double)rssBaseline * 100.0
            : 0;

        if (frameTimes.Count > 0)
        {
            frameTimes.Sort();
            int p50Idx = frameTimes.Count / 2;
            int p95Idx = (int)(frameTimes.Count * 0.95);
            int p99Idx = (int)(frameTimes.Count * 0.99);
            int p999Idx = (int)(frameTimes.Count * 0.999);

            result.P50FrameMs = frameTimes[p50Idx];
            result.P95FrameMs = p95Idx < frameTimes.Count ? frameTimes[p95Idx] : frameTimes[^1];
            result.P99FrameMs = p99Idx < frameTimes.Count ? frameTimes[p99Idx] : frameTimes[^1];
            result.P999FrameMs = p999Idx < frameTimes.Count ? frameTimes[p999Idx] : frameTimes[^1];
        }

        result.GcPausesMs = gcPauses.ToArray();

        return result;
    }

    private static SceneBuffer CreateAnimatedScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int identity = builder.AddTransform(Affine.Identity);

        var rand = new Random(42);
        for (int i = 0; i < 50; i++)
        {
            uint color = 0xFF000000u | (uint)rand.Next(1 << 24);
            int paintId = builder.AddPaint(Paint.Solid(color));

            double x = rand.NextDouble() * 256;
            double y = rand.NextDouble() * 256;
            double w = 10 + rand.NextDouble() * 30;
            double h = 10 + rand.NextDouble() * 30;
            builder.FillRect(new Rect(x, y, x + w, y + h), paintId, identity);
        }

        builder.EndFrame();
        return builder.End();
    }

    private static string SerializeSoakResult(SoakResult r)
    {
        var pausesArr = "";
        for (int i = 0; i < r.GcPausesMs.Count; i++)
            pausesArr += (i > 0 ? "," : "") + r.GcPausesMs[i].ToString("F1", CultureInfo.InvariantCulture);

        return $"{{\"startTime\":\"{r.StartTime:O}\",\"durationHours\":{r.DurationHours},\"frameCount\":{r.FrameCount},\"panics\":{r.Panics},\"gcSpikes\":{r.GcSpikes},\"p50FrameMs\":{r.P50FrameMs:F2},\"p95FrameMs\":{r.P95FrameMs:F2},\"p99FrameMs\":{r.P99FrameMs:F2},\"p999FrameMs\":{r.P999FrameMs:F2},\"totalAllocBytes\":{r.TotalAllocBytes},\"rssEndBytes\":{r.RssEndBytes},\"rssGrowthPercent\":{r.RssGrowthPercent:F1},\"gcPausesMs\":[{pausesArr}]}}";
    }
}

public sealed class SoakResult
{
    public DateTime StartTime { get; set; }
    public double DurationHours { get; set; }
    public int FrameCount { get; set; }
    public int Panics { get; set; }
    public int GcSpikes { get; set; }
    public double P50FrameMs { get; set; }
    public double P95FrameMs { get; set; }
    public double P99FrameMs { get; set; }
    public double P999FrameMs { get; set; }
    public long TotalAllocBytes { get; set; }
    public long RssEndBytes { get; set; }
    public double RssGrowthPercent { get; set; }
    public IReadOnlyList<double> GcPausesMs { get; set; } = Array.Empty<double>();
}
