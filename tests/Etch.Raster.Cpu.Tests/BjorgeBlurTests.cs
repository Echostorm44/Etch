using System;
using TUnit;
using Etch.Raster.Cpu;
using Etch.Raster.Cpu.Blur;
using Etch.Effects.Blur;

namespace Etch.Raster.Cpu.Tests;

public sealed class BjorgeBlurTests
{
    [Test]
    public async Task Blur_Identity_Radius0_CopiesSrcToDst()
    {
        int width = 4;
        int height = 4;
        var srcPixels = new Rgba16f[width * height];
        for (int i = 0; i < srcPixels.Length; i++)
        {
            srcPixels[i] = Rgba16f.From(0.5f, 0.25f, 0.75f, 1f);
        }

        var src = new Framebuffer(width, height, width, srcPixels);
        var dstPixels = new Rgba16f[width * height];
        var dst = new Framebuffer(width, height, width, dstPixels);
        var scratchPing = new Framebuffer(width / 2, height / 2, width / 2, new Rgba16f[(width / 2) * (height / 2)]);
        var scratchPong = new Framebuffer(width / 2, height / 2, width / 2, new Rgba16f[(width / 2) * (height / 2)]);

        BjorgeBlur.Blur(src, dst, 0f, scratchPing, scratchPong);

        for (int i = 0; i < srcPixels.Length; i++)
        {
            float diffR = MathF.Abs((float)dstPixels[i].R - (float)srcPixels[i].R);
            float diffG = MathF.Abs((float)dstPixels[i].G - (float)srcPixels[i].G);
            float diffB = MathF.Abs((float)dstPixels[i].B - (float)srcPixels[i].B);
            await Assert.That(diffR < 0.001f).IsTrue();
            await Assert.That(diffG < 0.001f).IsTrue();
            await Assert.That(diffB < 0.001f).IsTrue();
        }
    }

    [Test]
    public async Task Blur_Radius1_ExecutesWithoutError()
    {
        int width = 4;
        int height = 4;
        var srcPixels = new Rgba16f[width * height];
        for (int i = 0; i < srcPixels.Length; i++)
        {
            srcPixels[i] = Rgba16f.From(0.5f, 0.25f, 0.75f, 1f);
        }

        var src = new Framebuffer(width, height, width, srcPixels);
        var dstPixels = new Rgba16f[width * height];
        var dst = new Framebuffer(width, height, width, dstPixels);
        var scratchPing = new Framebuffer(width / 2, height / 2, width / 2, new Rgba16f[(width / 2) * (height / 2)]);
        var scratchPong = new Framebuffer(width / 2, height / 2, width / 2, new Rgba16f[(width / 2) * (height / 2)]);

        int octaveCount = DualFilterBlur.OctaveCount(1f);
        await Assert.That(octaveCount).IsEqualTo(1);

        BjorgeBlur.Blur(src, dst, 1f, scratchPing, scratchPong);
    }

    [Test]
    public async Task BlurTaps_HasCorrectDownsampleWeights()
    {
        await Assert.That(BlurTaps.DownCenterWeight).IsEqualTo(4f / 17f);
        await Assert.That(BlurTaps.DownCornerWeight).IsEqualTo(1f / 17f);
    }

    [Test]
    public async Task BlurTaps_HasCorrectUpsampleWeights()
    {
        await Assert.That(BlurTaps.UpCenterWeight).IsEqualTo(4f / 17f);
        await Assert.That(BlurTaps.UpEdgeWeight).IsEqualTo(2f / 17f);
        await Assert.That(BlurTaps.UpCornerWeight).IsEqualTo(1f / 17f);
    }

    [Test]
    public async Task Blur_NegativeRadius_CopiesSrcToDst()
    {
        int width = 4;
        int height = 4;
        var srcPixels = new Rgba16f[width * height];
        for (int i = 0; i < srcPixels.Length; i++)
        {
            srcPixels[i] = Rgba16f.From(0.5f, 0.25f, 0.75f, 1f);
        }

        var src = new Framebuffer(width, height, width, srcPixels);
        var dstPixels = new Rgba16f[width * height];
        var dst = new Framebuffer(width, height, width, dstPixels);
        var scratchPing = new Framebuffer(width / 2, height / 2, width / 2, new Rgba16f[(width / 2) * (height / 2)]);
        var scratchPong = new Framebuffer(width / 2, height / 2, width / 2, new Rgba16f[(width / 2) * (height / 2)]);

        BjorgeBlur.Blur(src, dst, -5f, scratchPing, scratchPong);

        for (int i = 0; i < srcPixels.Length; i++)
        {
            float diffR = MathF.Abs((float)dstPixels[i].R - (float)srcPixels[i].R);
            float diffG = MathF.Abs((float)dstPixels[i].G - (float)srcPixels[i].G);
            float diffB = MathF.Abs((float)dstPixels[i].B - (float)srcPixels[i].B);
            await Assert.That(diffR < 0.001f).IsTrue();
            await Assert.That(diffG < 0.001f).IsTrue();
            await Assert.That(diffB < 0.001f).IsTrue();
        }
    }

    [Test]
    public async Task OctaveCount_Radius0_Returns0()
    {
        int count = DualFilterBlur.OctaveCount(0f);
        await Assert.That(count == 0).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius1_Returns1()
    {
        int count = DualFilterBlur.OctaveCount(1f);
        await Assert.That(count == 1).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius3_Returns2()
    {
        int count = DualFilterBlur.OctaveCount(3f);
        await Assert.That(count == 2).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius7_Returns3()
    {
        int count = DualFilterBlur.OctaveCount(7f);
        await Assert.That(count == 3).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius15_Returns4()
    {
        int count = DualFilterBlur.OctaveCount(15f);
        await Assert.That(count == 4).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius31_Returns5()
    {
        int count = DualFilterBlur.OctaveCount(31f);
        await Assert.That(count == 5).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius63_Returns6()
    {
        int count = DualFilterBlur.OctaveCount(63f);
        await Assert.That(count == 6).IsTrue();
    }

    [Test]
    public async Task OctaveCount_Radius64_Returns6Clamped()
    {
        int count = DualFilterBlur.OctaveCount(64f);
        await Assert.That(count == 6).IsTrue();
    }
}