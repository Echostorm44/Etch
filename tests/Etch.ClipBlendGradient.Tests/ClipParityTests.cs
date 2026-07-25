using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Etch.Geometry;
using Etch.Scene;
using Etch.Scene.Serialization;
using Etch.Testing;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

internal sealed class ClipParityTests
{
    private const int Width = ClipFixtureScenes.FixtureWidth;
    private const int Height = ClipFixtureScenes.FixtureHeight;
    private static readonly string FixtureDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Fixtures", "impeller");
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    [Test]
    public async Task NestedCircles_Parity()
        => await RunParityTest("nested-circles", ClipFixtureScenes.NestedCircles()).ConfigureAwait(false);

    [Test]
    public async Task RectMinusCircle_Parity()
        => await RunParityTest("rect-minus-circle", ClipFixtureScenes.RectMinusCircle()).ConfigureAwait(false);

    [Test]
    public async Task SoftClippedRect_Parity()
        => await RunParityTest("soft-clipped-rect", ClipFixtureScenes.SoftClippedRect()).ConfigureAwait(false);

    [Test]
    public async Task EightLevelNesting_Parity()
        => await RunParityTest("8-level-nesting", ClipFixtureScenes.EightLevelNesting()).ConfigureAwait(false);

    [Test]
    public async Task ClipAroundSolid_Parity()
        => await RunParityTest("clip-around-solid", ClipFixtureScenes.ClipAroundSolid()).ConfigureAwait(false);

    [Test]
    public async Task OverlappingClips_Parity()
        => await RunParityTest("overlapping-clips", ClipFixtureScenes.OverlappingClips()).ConfigureAwait(false);

    [Test]
    public async Task ClipThenTranslate_Parity()
        => await RunParityTest("clip-then-translate", ClipFixtureScenes.ClipThenTranslate()).ConfigureAwait(false);

    [Test]
    public async Task ClipRotate_Parity()
        => await RunParityTest("clip-rotate", ClipFixtureScenes.ClipRotate()).ConfigureAwait(false);

    [Test]
    public async Task ClipScale_Parity()
        => await RunParityTest("clip-scale", ClipFixtureScenes.ClipScale()).ConfigureAwait(false);

    [Test]
    public async Task NonConvexClip_Parity()
        => await RunParityTest("non-convex-clip", ClipFixtureScenes.NonConvexClip()).ConfigureAwait(false);

    [Test]
    public async Task EtscRoundTrip_CommandCountMatches()
    {
        string etscPath = Path.Combine(FixtureDir, "nested-circles.etsc");
        if (!File.Exists(etscPath))
            throw new InvalidOperationException("Fixture not generated yet; run with ETCH_REGEN_GOLDENS=1");

        byte[] data = await File.ReadAllBytesAsync(etscPath).ConfigureAwait(false);
        var scene = SceneReader.Read(data);

        var original = ClipFixtureScenes.NestedCircles();
        await Assert.That(scene.CommandCount).IsEqualTo(original.CommandCount);
    }

    private static async Task RunParityTest(string name, SceneBuffer scene)
    {
        string impellerPngPath = Path.Combine(FixtureDir, $"{name}.impeller.png");
        string cpuRawPath = Path.Combine(FixtureDir, $"{name}.golden");
        string cpuPngPath = Path.Combine(FixtureDir, $"{name}.png");
        string diffPngPath = Path.Combine(FixtureDir, $"{name}-diff.png");
        string etscPath = Path.Combine(FixtureDir, $"{name}.etsc");
        string jsonPath = Path.Combine(FixtureDir, $"{name}.json");
        Directory.CreateDirectory(FixtureDir);

        if (SceneRunner.RegenerateGoldens || !File.Exists(etscPath))
        {
            int size = SceneWriter.GetRequiredSize(scene);
            byte[] etscBuffer = new byte[size];
            int written = SceneWriter.Write(scene, etscBuffer);
            await File.WriteAllBytesAsync(etscPath, etscBuffer.AsSpan(0, written).ToArray()).ConfigureAwait(false);

            var tolerance = GetTolerance(name);
            var json = new
            {
                scenePath = $"{name}.etsc",
                goldenPath = $"{name}.impeller.png",
                toleranceMean = tolerance.MeanMaxChannel,
                toleranceMax = tolerance.AbsMaxChannel,
                notes = GetNotes(name)
            };
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(json, s_jsonOptions)).ConfigureAwait(false);
        }

        byte[] actual = SceneCpuRenderer.RenderToRgba8(scene, Width, Height);

        if (SceneRunner.RegenerateGoldens || !File.Exists(cpuRawPath))
        {
            await File.WriteAllBytesAsync(cpuRawPath, actual).ConfigureAwait(false);
            ImageWriter.WriteRgbaToPng(cpuPngPath, actual, Width, Height);
        }

        if (!File.Exists(impellerPngPath))
        {
            throw new InvalidOperationException(
                $"Impeller reference PNG not found for {name}: {impellerPngPath}. " +
                "Run the Flutter generator: cd tools/etch-impeller-ref && flutter run --enable-impeller -d windows");
        }

        byte[] reference = ImageReader.ReadPngToRgba8(impellerPngPath);

        if (reference.Length != actual.Length)
        {
            throw new InvalidOperationException(
                $"Reference size mismatch for {name}: expected {actual.Length}, got {reference.Length}");
        }

        var diffTolerance = GetTolerance(name);
        var result = PixelDiff.Compare(actual, reference, Width, Height, diffTolerance);

        if (!result.Pass)
        {
            PixelDiffPngWriter.Write4PanelPng(diffPngPath, actual, reference, Width, Height);
            throw new InvalidOperationException(
                $"{name} parity failed: mean={result.MeanError:F2}, p95={result.P95Error:F2}, max={result.MaxError:F2}, " +
                $"failingPixels={result.FailingPixelCount}/{result.PixelCount}");
        }

        await Assert.That(result.Pass).IsTrue();
        await Assert.That(result.MeanError).IsLessThanOrEqualTo(diffTolerance.MeanMaxChannel);
    }

    private static DiffTolerance GetTolerance(string name)
    {
        return name switch
        {
            "clip-rotate" => new DiffTolerance(meanMaxChannel: 4.0f, p95MaxChannel: 6.0f, absMaxChannel: 255.0f),
            "non-convex-clip" => new DiffTolerance(meanMaxChannel: 2.0f, p95MaxChannel: 4.0f, absMaxChannel: 255.0f),
            "clip-around-solid" => new DiffTolerance(meanMaxChannel: 2.0f, p95MaxChannel: 4.0f, absMaxChannel: 255.0f),
            "overlapping-clips" => new DiffTolerance(meanMaxChannel: 2.0f, p95MaxChannel: 4.0f, absMaxChannel: 255.0f),
            _ => new DiffTolerance(meanMaxChannel: 1.0f, p95MaxChannel: 3.0f, absMaxChannel: 255.0f),
        };
    }

    private static string? GetNotes(string name)
    {
        return name switch
        {
            "clip-rotate" => "Rotated clip has slightly higher tolerance due to sub-pixel edge positioning.",
            "non-convex-clip" => "Non-convex clip uses different flatten strategy; scene-specific override applied.",
            _ => null,
        };
    }
}
