using BenchmarkDotNet.Attributes;
using Etch.Bench.Shared;
using Etch.Compose;
using Etch.Compose.Cpu;

namespace Etch.Bench.Compose;

/// <summary>
/// CPU composer frame cost. Budgets (CPU renderer design): a 1080p application frame in ≤ 8 ms on
/// 8 cores and ≤ 25 ms on one; nothing allocated per frame in steady state.
/// </summary>
[MemoryDiagnoser]
public class CpuComposerBenchmarks
{
    private readonly ComposeHarness harness = new(1920, 1080, 1f);

    [Params(1, 8, 0)]
    public int Threads { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        harness.Composer.MaxDegreeOfParallelism = Threads == 0 ? -1 : Threads;
        harness.Build();
        harness.Render();
    }

    [GlobalCleanup]
    public void Cleanup() => harness.Dispose();

    /// <summary>Executing the finished draw list (the composer's whole job).</summary>
    [Benchmark]
    [AllocationBudget(0)]
    public void RenderUiFrame1080() => harness.Render();

    /// <summary>Replaying the recording into the draw list (steady state: glyphs and masks cached).</summary>
    [Benchmark]
    [AllocationBudget(0)]
    public void BuildUiFrame1080() => harness.Build();
}

/// <summary>A composer, its draw list and the UI frame, ready to build and render.</summary>
public sealed class ComposeHarness : IDisposable
{
    private readonly DrawRecording recording;
    private readonly DrawListBuilder builder = new();
    private readonly DrawList list = new();
    private readonly CpuFramebuffer framebuffer = new();
    private readonly uint width;
    private readonly uint height;

    public ComposeHarness(int width, int height, float scale)
    {
        this.width = (uint)width;
        this.height = (uint)height;
        recording = UiFrameScene.Build(width, height, scale);
    }

    public CpuComposer Composer { get; } = new();

    public CpuFramebuffer Framebuffer => framebuffer;

    public DrawList List => list;

    public void Build()
    {
        builder.Begin(list, width, height, Composer.Masks, Composer.MonoAtlas, Composer.ColorAtlas);
        builder.Replay(recording);
        builder.End();
        list.Parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
    }

    public void Render() => Composer.Render(list, framebuffer);

    public void Dispose() => Composer.Dispose();
}
