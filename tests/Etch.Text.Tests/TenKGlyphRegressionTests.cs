using System;
using System.IO;
using System.Text.Json;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class TenKGlyphRegressionTests
{
    [Test]
    public async Task BaselineFile_ExistsAndIsValidJson()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "bench", "Etch.Text.Bench", "baselines", "10k.json");

        await Assert.That(File.Exists(path)).IsTrue();

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);

        await Assert.That(doc.RootElement.TryGetProperty("budgets", out _)).IsTrue();
        await Assert.That(doc.RootElement.TryGetProperty("regressionThreshold", out _)).IsTrue();
    }

    [Test]
    public async Task BaselineFile_HasAllWarmBudgets()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "bench", "Etch.Text.Bench", "baselines", "10k.json");

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var budgets = doc.RootElement.GetProperty("budgets");

        await Assert.That(budgets.TryGetProperty("WarmShape", out _)).IsTrue();
        await Assert.That(budgets.TryGetProperty("WarmRasterLookup", out _)).IsTrue();
        await Assert.That(budgets.TryGetProperty("WarmAtlasPack", out _)).IsTrue();
    }

    [Test]
    public async Task BaselineFile_HasAllColdBudgets()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "bench", "Etch.Text.Bench", "baselines", "10k.json");

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var budgets = doc.RootElement.GetProperty("budgets");

        await Assert.That(budgets.TryGetProperty("ColdShape", out _)).IsTrue();
        await Assert.That(budgets.TryGetProperty("ColdRaster", out _)).IsTrue();
        await Assert.That(budgets.TryGetProperty("ColdAtlasPack", out _)).IsTrue();
    }

    [Test]
    public async Task BaselineFile_WarmCpuBudget_IsFiveMs()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "bench", "Etch.Text.Bench", "baselines", "10k.json");

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var warmShape = doc.RootElement.GetProperty("budgets").GetProperty("WarmShape");
        double cpuMs = warmShape.GetProperty("cpuMs").GetDouble();

        await Assert.That(cpuMs).IsEqualTo(5.0);
    }

    [Test]
    public async Task BaselineFile_ColdCpuBudget_IsFortyMs()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "bench", "Etch.Text.Bench", "baselines", "10k.json");

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        var coldShape = doc.RootElement.GetProperty("budgets").GetProperty("ColdShape");
        double cpuMs = coldShape.GetProperty("cpuMs").GetDouble();

        await Assert.That(cpuMs).IsEqualTo(40.0);
    }

    [Test]
    public async Task BaselineFile_RegressionThreshold_IsTenPercent()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "bench", "Etch.Text.Bench", "baselines", "10k.json");

        string json = await File.ReadAllTextAsync(path);
        using var doc = JsonDocument.Parse(json);
        double threshold = doc.RootElement.GetProperty("regressionThreshold").GetDouble();

        await Assert.That(threshold).IsEqualTo(0.10);
    }
}
