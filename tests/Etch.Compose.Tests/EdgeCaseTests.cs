using Etch.Compose.Coverage;
using Etch.Compose.Cpu;
using Etch.Geometry;
using Etch.Scene;

namespace Etch.Compose.Tests;

/// <summary>
/// Inputs at the edges of the primitives' domains — degenerate geometry, shapes that resemble
/// analytic ones but are not — render correctly on the CPU and the same on the GPU.
/// </summary>
[NotInParallel(nameof(EdgeCaseTests))]
internal sealed class EdgeCaseTests
{
    private static readonly ComposeColor White = new(1, 1, 1, 1);
    private static readonly ComposeColor Blue = new(0, 0, 1, 1);

    /// <summary>Renders on the CPU; returns RGBA.</summary>
    internal static byte[] RenderCpu(DrawRecording recording, int width, int height)
    {
        using var composer = new CpuComposer();
        var builder = new DrawListBuilder();
        var list = new DrawList();
        var frame = new CpuFramebuffer();
        builder.Begin(list, (uint)width, (uint)height, composer.Masks, composer.MonoAtlas, composer.ColorAtlas);
        builder.Replay(recording);
        builder.End();
        list.Parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
        composer.Render(list, frame);
        var rgba = new byte[width * height * 4];
        frame.CopyToRgba(rgba);
        return rgba;
    }

    private static DrawRecording OnWhite(int width, int height)
    {
        var rec = new DrawRecording();
        rec.SetTransform(Affine.Identity);
        rec.FillRect(0, 0, width, height, ComposePaint.Solid(White));
        return rec;
    }

    [Test]
    [Arguments(StrokeCap.Butt)]
    [Arguments(StrokeCap.Round)]
    [Arguments(StrokeCap.Square)]
    public async Task ZeroLengthLine_DrawsItsCapOrNothing_NeverCorrupts(StrokeCap cap)
    {
        var rec = OnWhite(40, 40);
        rec.StrokeLine(20, 20, 20, 20, StrokeParameters.Solid(8, cap), Blue);
        // The same in local space, collapsed by the transform (device length 0, local length 10).
        rec.SetTransform(Affine.Translate(10, 10) * Affine.Scale(1e-9, 1e-9));
        rec.StrokeLine(0, 0, 10, 0, StrokeParameters.Solid(8, cap), Blue);
        byte[] rgba = RenderCpu(rec, 40, 40);

        for (int i = 0; i < 40 * 40; i++)
        {
            await Assert.That((int)rgba[i * 4 + 3]).IsEqualTo(255).Because($"pixel {i} alpha (a NaN coverage cleared it)");
        }
        int centre = (20 * 40 + 20) * 4;
        bool blue = rgba[centre] < 50 && rgba[centre + 2] > 200;
        await Assert.That(blue).IsEqualTo(cap != StrokeCap.Butt);
    }

    /// <summary>The CPU frame of <paramref name="recording"/> within D4 of the reference GPU's (skips without one).</summary>
    internal static async Task AssertGpuParity(DrawRecording recording, int width, int height)
    {
        using var harness = ParityHarness.TryCreate((uint)width, (uint)height, ParityAdapter.Reference, out string reason);
        if (harness is null)
        {
            Skip.Test($"Needs the reference adapter: {reason}");
            return;
        }
        var parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
        var (gpu, cpu) = harness.Render(recording, (uint)width, (uint)height, parameters);
        var stats = ParityStats.Compare(gpu, cpu, width, height);
        await Assert.That(stats.Max).IsLessThanOrEqualTo(4).Because(stats.ToString());
        await Assert.That(stats.P999).IsLessThanOrEqualTo(2).Because(stats.ToString());
    }

    [Test]
    public async Task ZeroLengthLines_MatchTheGpu()
    {
        var rec = OnWhite(60, 40);
        float x = 10;
        foreach (var cap in new[] { StrokeCap.Butt, StrokeCap.Round, StrokeCap.Square })
        {
            rec.StrokeLine(x, 20, x, 20, StrokeParameters.Solid(8, cap), Blue);
            x += 20;
        }
        await AssertGpuParity(rec, 60, 40);
    }

    [Test]
    public async Task DegenerateLineInstance_HasZeroCoverage_NotNaN()
    {
        var inst = new ShapeInstance { Type = ShapeType.Line, P0 = 5, P1 = 5, P2 = 5, P3 = 5, Q0 = 3, Q1 = 1 };
        inst.SetIdentityFrame();
        float cov = CpuShading.ShapeCoverage(inst, 5.5f, 5.5f, MaskSource.None);
        await Assert.That(float.IsNaN(cov)).IsFalse();
        await Assert.That(cov).IsEqualTo(0f);
    }
}
