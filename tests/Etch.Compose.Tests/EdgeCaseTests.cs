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
    [Timeout(20_000)]
    public async Task MicroscopicDashes_DrawSolid_InsteadOfHanging(CancellationToken cancellation)
    {
        // 1e-5-unit dashes over a 300-unit line would be 15 million dashes.
        var rec = OnWhite(320, 40);
        var dashed = StrokeParameters.Solid(6) with { DashOn = 1e-5f, DashOff = 1e-5f };
        rec.StrokeLine(10, 20, 310, 20, dashed, Blue);
        byte[] rgba = RenderCpu(rec, 320, 40);
        int mid = (20 * 320 + 160) * 4;
        await Assert.That((int)rgba[mid]).IsLessThan(50).Because("drawn solid");
        await AssertGpuParity(rec, 320, 40);
    }

    [Test]
    [Timeout(20_000)]
    public async Task SectorWithAHugeStartAngle_IsTheSameSector(CancellationToken cancellation)
    {
        // A start angle a million radians out walked the angle into range one turn at a time —
        // per pixel, on both backends (a GPU timeout). It must equal the same angle mod 2π.
        double start = 1e6;
        double reduced = Math.IEEERemainder(start, 2 * Math.PI);
        var far = OnWhite(80, 80);
        far.FillSector(40, 40, 30, 10, (float)start, 2f, Blue);
        var near = OnWhite(80, 80);
        near.FillSector(40, 40, 30, 10, (float)reduced, 2f, Blue);
        byte[] a = RenderCpu(far, 80, 80);
        byte[] b = RenderCpu(near, 80, 80);
        int worst = 0;
        for (int i = 0; i < a.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        }
        await Assert.That(worst).IsLessThanOrEqualTo(2);
        await AssertGpuParity(far, 80, 80);
    }

    [Test]
    public async Task NotdefGlyph_IsNotMistakenForASpace()
    {
        // A face without a space maps U+0020 to glyph 0 (.notdef): tofu boxes must still draw.
        await Assert.That(GlyphRunBuilder.IsSpace(faceHasSpace: false, spaceGlyph: 0, glyphId: 0)).IsFalse();
        await Assert.That(GlyphRunBuilder.IsSpace(faceHasSpace: true, spaceGlyph: 0, glyphId: 0)).IsFalse();
        await Assert.That(GlyphRunBuilder.IsSpace(faceHasSpace: true, spaceGlyph: 3, glyphId: 3)).IsTrue();
        await Assert.That(GlyphRunBuilder.IsSpace(faceHasSpace: true, spaceGlyph: 3, glyphId: 0)).IsFalse();
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
