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

    /// <summary>
    /// A caret blink: the frame is rebuilt from its recording (caret toggled) and rendered
    /// incrementally into the previous frame — only the caret's tiles render. Budget 0.5 ms.
    /// </summary>
    [Benchmark]
    [AllocationBudget(0)]
    public void CaretBlinkFrame1080() => harness.BlinkFrame();

    /// <summary>Replaying the recording into the draw list (steady state: glyphs and masks cached).</summary>
    [Benchmark]
    [AllocationBudget(0)]
    public void BuildUiFrame1080() => harness.Build();
}

/// <summary>A composer, its draw list and the UI frame, ready to build and render.</summary>
public sealed class ComposeHarness : IDisposable
{
    private readonly DrawRecording recording;
    private readonly DrawRecording caretOn;
    private readonly DrawRecording caretOff;
    private readonly DrawListBuilder builder = new();
    private bool blink;
    private readonly DrawList list = new();
    private readonly CpuFramebuffer framebuffer = new();
    private readonly uint width;
    private readonly uint height;

    public ComposeHarness(int width, int height, float scale)
    {
        this.width = (uint)width;
        this.height = (uint)height;
        recording = UiFrameScene.Build(width, height, scale);
        caretOn = UiFrameScene.Build(width, height, scale, caret: true);
        caretOff = UiFrameScene.Build(width, height, scale, caret: false);
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

    /// <summary>One caret-blink frame: rebuild with the caret toggled, render incrementally. Returns damaged pixels.</summary>
    public long BlinkFrame()
    {
        blink = !blink;
        builder.Begin(list, width, height, Composer.Masks, Composer.MonoAtlas, Composer.ColorAtlas);
        builder.Replay(blink ? caretOn : caretOff);
        builder.End();
        list.Parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
        long damaged = 0;
        foreach (var rect in Composer.RenderIncremental(list, framebuffer))
        {
            damaged += (long)rect.Width * rect.Height;
        }
        return damaged;
    }

    public void Dispose() => Composer.Dispose();
}
