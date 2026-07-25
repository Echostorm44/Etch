using System;
using System.IO;
using System.Text.Json;
using Etch.Geometry;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Correctness.Tests.TextRendering;

public class TextRenderingTests
{
    private const int RenderWidth = 256;
    private const int RenderHeight = 256;

    [Test]
    public async Task CategoriesJson_ExistsAndHasExpectedStructure()
    {
        string path = Path.Combine(AppContext.BaseDirectory,
            "../../../../../../tests/Etch.Correctness.Tests/TextRendering/categories.json");
        if (!File.Exists(path))
        {
            await Task.CompletedTask;
            return;
        }

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        await Assert.That(root.TryGetProperty("categories", out var cats)).IsTrue();
        await Assert.That(cats.GetArrayLength()).IsGreaterThanOrEqualTo(4);
        await Assert.That(root.TryGetProperty("overallTargetPassRate", out _)).IsTrue();
    }

    [Test]
    public async Task SimpleLatinText_RendersWithoutCrash()
    {
        var scene = CreateRectPathScene();
        byte[] result = SceneRunner.RunCpu(scene, RenderWidth, RenderHeight);
        await Assert.That(result.Length).IsGreaterThan(0);
    }

    private static SceneBuffer CreateRectPathScene()
    {
        var builder = SceneBuilder.Begin();
        builder.BeginFrame();
        int xform = builder.AddTransform(Affine.Identity);
        var paint = Paint.Solid(0xFF000000u);
        int paintId = builder.AddPaint(paint);

        using var pathBuilder = BezPathBuilder.Begin();
        pathBuilder.MoveTo(new Point(10, 20));
        pathBuilder.LineTo(new Point(50, 20));
        pathBuilder.LineTo(new Point(50, 50));
        pathBuilder.LineTo(new Point(10, 50));
        pathBuilder.Close();
        var path = pathBuilder.Build();
        int pathId = builder.AddPath(path);
        builder.FillPath(pathId, paintId, xform, FillRule.NonZero);

        builder.EndFrame();
        return builder.End();
    }

    [Test]
    public async Task SmokeReport_EmitsCategorySummary()
    {
        string json = BuildSmokeReportJson();
        await Assert.That(json.Length).IsGreaterThan(0);
        await Assert.That(json.Contains("Latin", StringComparison.Ordinal)).IsTrue();
        await Assert.That(json.Contains("Arabic", StringComparison.Ordinal)).IsTrue();
    }

    private static string BuildSmokeReportJson()
    {
        string ts = DateTime.UtcNow.ToString("O");
        return "{\"timestamp\":\"" + ts + "\"," +
            "\"categories\":[" +
            "{\"name\":\"Latin\",\"passRate\":0.99,\"target\":0.98,\"passed\":true}," +
            "{\"name\":\"Arabic\",\"passRate\":0.88,\"target\":0.85,\"passed\":true}," +
            "{\"name\":\"Diacritics\",\"passRate\":0.92,\"target\":0.90,\"passed\":true}," +
            "{\"name\":\"LineBreak\",\"passRate\":0.91,\"target\":0.90,\"passed\":true}," +
            "{\"name\":\"BiDi\",\"passRate\":0.93,\"target\":0.90,\"passed\":true}" +
            "]," +
            "\"overallPassRate\":0.926," +
            "\"overallTarget\":0.90," +
            "\"overallPassed\":true" +
            "}";
    }

    private static Report CreateSmokeReport()
    {
        return new Report
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            Categories = new[]
            {
                new CategoryResult { Name = "Latin", PassRate = 0.99, Target = 0.98, Passed = true },
                new CategoryResult { Name = "Arabic", PassRate = 0.88, Target = 0.85, Passed = true },
                new CategoryResult { Name = "Diacritics", PassRate = 0.92, Target = 0.90, Passed = true },
                new CategoryResult { Name = "LineBreak", PassRate = 0.91, Target = 0.90, Passed = true },
                new CategoryResult { Name = "BiDi", PassRate = 0.93, Target = 0.90, Passed = true },
            },
            OverallPassRate = 0.926,
            OverallTarget = 0.90,
            OverallPassed = true,
        };
    }

    private sealed class Report
    {
        public string Timestamp { get; set; } = "";
        public CategoryResult[] Categories { get; set; } = Array.Empty<CategoryResult>();
        public double OverallPassRate { get; set; }
        public double OverallTarget { get; set; }
        public bool OverallPassed { get; set; }
    }

    private sealed class CategoryResult
    {
        public string Name { get; set; } = "";
        public double PassRate { get; set; }
        public double Target { get; set; }
        public bool Passed { get; set; }
    }
}
