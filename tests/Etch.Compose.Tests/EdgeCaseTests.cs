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
    public async Task GpuImageTextures_AreFreedWhenReleasedOrTrimmed()
    {
        using var harness = ParityHarness.TryCreate(ParityScenes.Width, ParityScenes.Height, ParityAdapter.Reference, out string reason);
        if (harness is null)
        {
            Skip.Test($"Needs the reference adapter: {reason}");
            return;
        }
        var parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };
        harness.RenderGpu(ParityScenes.Build("images", 1f), ParityScenes.Width, ParityScenes.Height, parameters);
        int images = harness.Gpu.ImageTextureCount;
        await Assert.That(images).IsEqualTo(2);
        harness.Gpu.ReleaseImage(1);
        await Assert.That(harness.Gpu.ImageTextureCount).IsEqualTo(1);
        harness.RenderGpu(ParityScenes.Build("clips", 1f), ParityScenes.Width, ParityScenes.Height, parameters);
        await Assert.That(harness.Gpu.Masks.PageCount).IsGreaterThan(0);
        harness.Gpu.Trim();
        await Assert.That(harness.Gpu.ImageTextureCount).IsEqualTo(0);
        await Assert.That(harness.Gpu.Masks.PageCount).IsEqualTo(0);
        // Everything comes back on the next frame.
        var (gpu, cpu) = harness.Render(ParityScenes.Build("clips", 1f), ParityScenes.Width, ParityScenes.Height, parameters);
        await Assert.That(ParityStats.Compare(gpu, cpu, ParityScenes.Width, ParityScenes.Height).Max).IsLessThanOrEqualTo(4);
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

    // A deterministic, detailed opaque pattern: neighbouring texels differ, so a seam or an
    // off-by-one tile offset shows.
    private static ComposeImage Pattern(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                pixels[i] = (byte)(x * 255 / Math.Max(1, width - 1));
                pixels[i + 1] = (byte)(y * 7 + (h >> 28));
                pixels[i + 2] = (byte)(h >> 8);
                pixels[i + 3] = 255;
            }
        }
        return new ComposeImage(pixels, width, height);
    }

    [Test]
    public async Task ImagesLargerThanATexture_DrawTiled_AsTheWholeImageWould()
    {
        const int Size = 200;
        using var harness = ParityHarness.TryCreate(Size, Size, ParityAdapter.Reference, out string reason);
        if (harness is null)
        {
            Skip.Test($"Needs the reference adapter: {reason}");
            return;
        }
        var image = Pattern(150, 100);
        var rec = OnWhite(Size, Size);
        rec.Image(1, image, 5, 5, 150, 100, 1f);
        rec.SetTransform(Affine.Translate(100, 90) * Affine.Rotate(0.4) * Affine.Scale(0.83, 1.21));
        rec.Image(1, image, -60, -40, 120, 80, 0.8f);
        var parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };

        byte[] whole = harness.RenderGpu(rec, Size, Size, parameters);
        await Assert.That(harness.Gpu.ImageTileCount(1)).IsEqualTo(1);

        // The same frame with 40-px textures: 4 × 3 tiles, seams everywhere.
        harness.Gpu.ReleaseImage(1);
        harness.Gpu.MaxImageTextureSize = 40;
        var (tiled, cpu) = harness.Render(rec, Size, Size, parameters);
        await Assert.That(harness.Gpu.ImageTileCount(1)).IsEqualTo(12);
        var againstWhole = ParityStats.Compare(tiled, whole, Size, Size);
        await Assert.That(againstWhole.Max).IsLessThanOrEqualTo(1).Because(againstWhole.ToString());
        var againstCpu = ParityStats.Compare(tiled, cpu, Size, Size);
        await Assert.That(againstCpu.Max).IsLessThanOrEqualTo(4).Because(againstCpu.ToString());
        await Assert.That(againstCpu.P999).IsLessThanOrEqualTo(2).Because(againstCpu.ToString());
    }

    [Test]
    [Arguments(ParityAdapter.Reference)]
    [Arguments(ParityAdapter.Hardware)]
    public async Task ATallScreenshot_DrawsAtFullResolution_AcrossTheTextureLimit(ParityAdapter adapter)
    {
        // 600 × 9000: taller than the 8192 texture limit. The window shows rows 8090..8290 at 1:1,
        // across the seam between the two tiles (rows 0..8189 and 8190..8999).
        const int Size = 200;
        using var harness = ParityHarness.TryCreate(Size, Size, adapter, out string reason);
        if (harness is null)
        {
            Skip.Test($"Needs the {adapter} adapter: {reason}");
            return;
        }
        var image = Pattern(600, 9000);
        var rec = OnWhite(Size, Size);
        rec.SetTransform(Affine.Translate(-200, -8090));
        rec.Image(7, image, 0, 0, 600, 9000, 1f);
        var parameters = new ComposeParameters { TextGamma = 1.5f, LightWeight = 1f };

        // The image's pixels at 1:1, high-contrast down to black: the filter's subtexel rounding
        // is what differs between samplers here, so this also holds every GPU to the CPU.
        var (gpu, cpu) = harness.Render(rec, Size, Size, parameters);
        await Assert.That(harness.Gpu.ImageTileCount(7)).IsEqualTo(2);
        var stats = ParityStats.Compare(gpu, cpu, Size, Size);
        await Assert.That(stats.Max).IsLessThanOrEqualTo(4).Because(stats.ToString());
        // At 1:1 the original pixels come through (an image rescaled to fit would not).
        int seamRow = 8190 - 8090;
        for (int y = seamRow - 2; y <= seamRow + 2; y++)
        {
            int at = (y * Size + 50) * 4;
            int source = ((8090 + y) * 600 + 250) * 4;
            for (int c = 0; c < 3; c++)
            {
                await Assert.That(Math.Abs(gpu[at + c] - image.Pixels[source + c])).IsLessThanOrEqualTo(1).Because($"row {y}");
            }
        }
    }

    [Test]
    public async Task GlyphsTallerThanTheAtlasShelf_DrawAsOutlines()
    {
        // 200-px text: 'H' is ~140 px tall, more than a 128-px atlas shelf holds. Such glyphs were
        // dropped (mono) or fell back to a monochrome silhouette (colour).
        const int Width = 420;
        const int Height = 260;
        var rec = OnWhite(Width, Height);
        rec.Glyphs(ParityScenes.Run("H", 10, 200, new ComposeColor(0, 0, 0, 1), 1f, 200));
        rec.Glyphs(ParityScenes.EmojiRun("\U0001F600", 200, 220, 1f, 1f, 200));
        byte[] rgba = RenderCpu(rec, Width, Height);

        // The H's left stem, well inside it: black.
        int stem = (150 * Width + 45) * 4;
        await Assert.That((int)rgba[stem]).IsLessThan(40).Because("the H is drawn");
        // The emoji: yellow pixels (a silhouette would be the run's white/black).
        int yellow = 0;
        for (int y = 40; y < 240; y++)
        {
            for (int x = 200; x < 410; x++)
            {
                int i = (y * Width + x) * 4;
                if (rgba[i] > 200 && rgba[i + 1] > 150 && rgba[i + 2] < 100)
                {
                    yellow++;
                }
            }
        }
        await Assert.That(yellow).IsGreaterThan(5000).Because("the emoji is drawn in colour");
        await AssertGpuParity(rec, Width, Height);
    }

    [Test]
    [Arguments(1)]
    [Arguments(8)]
    public async Task ShadowWithNaNSigma_HasNoCoverage_InsteadOfThrowing(int vectorWidth)
    {
        // The builder never emits one (NaN geometry is dropped earlier), but the shading functions
        // must not throw on it: MathF.Sign(NaN) throws, and the erf used it.
        var inst = new ShapeInstance { Type = ShapeType.Shadow, P0 = 0, P1 = 0, P2 = 20, P3 = 20, Q0 = 2, Q1 = float.NaN };
        inst.SetIdentityFrame();
        float cov = CpuShading.ShapeCoverage(inst, 5.5f, 5.5f, MaskSource.None);
        await Assert.That(cov > 0f).IsFalse();
        var row = new float[19];
        CpuShadow.Row(inst, 5, 0, row, vectorWidth);
        await Assert.That(row.All(c => !(c > 0f))).IsTrue();
    }
}