namespace Etch.Compose.Tests;

/// <summary>
/// The CPU composer produces the GPU composer's image: every parity scene, at several device
/// scales, rendered both ways and compared per pixel.
/// </summary>
/// <remarks>
/// <para>
/// The contract is checked against the software reference rasterizer (WARP), whose arithmetic is
/// the specification's: float blending in linear light and 8-bit subtexel filter weights. There
/// the tolerance (decision D4) holds for every primitive, text included: mean channel error
/// &lt; 0.1, 99.9th-percentile pixel error ≤ 2, maximum ≤ 4 (8-bit sRGB levels).
/// </para>
/// <para>
/// Hardware deviates from the reference by its own precision choices (NVIDIA, for one, rounds the
/// destination blend term of sRGB targets to 8 bits, which moves dark text by up to 3 levels, and
/// filters with finer subtexel weights). Against hardware the CPU must be as close as the reference
/// rasterizer itself is: no more than one level beyond the reference's own p99.9 and maximum.
/// </para>
/// </remarks>
[NotInParallel(nameof(GpuCpuParityTests))]
internal sealed class GpuCpuParityTests
{
    private const double MeanBudget = 0.1;
    private const int P999Budget = 2;
    private const int MaxBudget = 4;

    public static IEnumerable<(string, float)> Cases()
    {
        foreach (string scene in ParityScenes.Names)
        {
            foreach (float scale in new[] { 1f, 1.25f, 1.5f, 2f })
            {
                yield return (scene, scale);
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task CpuMatchesReferenceGpu(string scene, float scale)
    {
        var (width, height) = Size(scale);
        using var harness = ParityHarness.TryCreate(width, height, ParityAdapter.Reference, out string reason);
        if (harness is null)
        {
            Skip.Test($"GPU parity needs the reference adapter: {reason}");
            return;
        }

        var recording = ParityScenes.Build(scene, scale);
        var (gpu, cpu) = harness.Render(recording, width, height, Parameters);
        var stats = ParityStats.Compare(gpu, cpu, (int)width, (int)height);
        string name = $"{scene}-{scale * 100:0}";
        await Report($"{name} reference: {stats}");

        bool pass = stats.MeanError < MeanBudget && stats.P999 <= P999Budget && stats.Max <= MaxBudget;
        if (!pass)
        {
            await Fail(name, gpu, cpu, (int)width, (int)height, stats);
        }
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task CpuMatchesHardwareGpuAsCloselyAsTheReference(string scene, float scale)
    {
        var (width, height) = Size(scale);
        using var hardware = ParityHarness.TryCreate(width, height, ParityAdapter.Hardware, out string reason);
        if (hardware is null)
        {
            Skip.Test($"No hardware adapter: {reason}");
            return;
        }
        using var reference = ParityHarness.TryCreate(width, height, ParityAdapter.Reference, out reason);
        if (reference is null)
        {
            Skip.Test($"No reference adapter to calibrate against: {reason}");
            return;
        }

        var recording = ParityScenes.Build(scene, scale);
        var (gpu, cpu) = hardware.Render(recording, width, height, Parameters);
        byte[] warp = reference.RenderGpu(recording, width, height, Parameters);
        var cpuStats = ParityStats.Compare(gpu, cpu, (int)width, (int)height);
        var referenceStats = ParityStats.Compare(gpu, warp, (int)width, (int)height);
        string name = $"{scene}-{scale * 100:0}";
        await Report($"{name} {hardware.AdapterName}: cpu {cpuStats} | reference {referenceStats}");

        bool pass = cpuStats.MeanError < Math.Max(MeanBudget, referenceStats.MeanError * 1.25)
            && cpuStats.P999 <= Math.Max(P999Budget, referenceStats.P999 + 1)
            && cpuStats.Max <= Math.Max(MaxBudget, referenceStats.Max + 1);
        if (!pass)
        {
            await Fail(name + "-hw", gpu, cpu, (int)width, (int)height, cpuStats);
        }
    }

    /// <summary>Clip and path masks far beyond an atlas page (tiled clip masks, several pages).</summary>
    [Test]
    [Arguments(3840, 1200)]
    [Arguments(4096, 2160)]
    public async Task LargeClipsMatchTheReferenceGpu(int width, int height)
    {
        using var harness = ParityHarness.TryCreate((uint)width, (uint)height, ParityAdapter.Reference, out string reason);
        if (harness is null)
        {
            Skip.Test($"GPU parity needs the reference adapter: {reason}");
            return;
        }
        var recording = ParityScenes.BuildLarge(width, height);
        var (gpu, cpu) = harness.Render(recording, (uint)width, (uint)height, Parameters);
        var stats = ParityStats.Compare(gpu, cpu, width, height);
        string name = $"large-{width}x{height}";
        await Report($"{name} reference: {stats}; mask pages {harness.MaskPages}");
        await Assert.That(harness.MaskPages).IsGreaterThanOrEqualTo(height >= 2000 ? 2 : 1).Because("at 4K the stripe clip needs more than one atlas page");
        // Parity cannot see a clip both backends drop; check the masks exist and cut.
        await Assert.That(harness.CpuMaskedClips).IsGreaterThanOrEqualTo(3);
        await Assert.That(harness.CpuDroppedMasks).IsEqualTo(0);
        int corner = (24 * width + 24) * 4;
        await Assert.That(cpu[corner + 2] > cpu[corner] + 40).IsFalse().Because("the rounded corner outside the clip keeps the paper colour"); bool pass = stats.MeanError < MeanBudget && stats.P999 <= P999Budget && stats.Max <= MaxBudget;
        if (!pass)
        {
            await Fail(name, gpu, cpu, width, height, stats);
        }
    }

    private static ComposeParameters Parameters => new() { TextGamma = 1.5f, LightWeight = 1f };

    private static (uint Width, uint Height) Size(float scale)
        => ((uint)Math.Round(ParityScenes.Width * scale), (uint)Math.Round(ParityScenes.Height * scale));

    private static async Task Report(string line)
    {
        var output = TestContext.Current?.OutputWriter;
        if (output is not null)
        {
            await output.WriteLineAsync(line);
        }
    }

    private static async Task Fail(string name, byte[] gpu, byte[] cpu, int width, int height, ParityStats stats)
    {
        string diff = ParityHarness.WriteDiff(name, gpu, cpu, width, height);
        string worst = ParityReport.WorstPixels(gpu, cpu, width, 12);
        await Assert.That(false).IsTrue().Because($"{name}: {stats}. Worst: {worst} Diff: {diff}");
    }
}
