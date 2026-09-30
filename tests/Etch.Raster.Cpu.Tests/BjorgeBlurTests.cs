using System;
using TUnit;
using Etch.Raster.Cpu;
using Etch.Raster.Cpu.Blur;
using Etch.Effects.Blur;

namespace Etch.Raster.Cpu.Tests;

public sealed class BjorgeBlurTests
{
    private static Framebuffer Filled(int width, int height, float r, float g, float b, float a)
    {
        var pixels = new Rgba16f[width * height];
        Array.Fill(pixels, Rgba16f.From(r, g, b, a));
        return new Framebuffer(width, height, width, pixels);
    }

    private static Framebuffer Empty(int width, int height) =>
        new(width, height, width, new Rgba16f[width * height]);

    [Test]
    public async Task Blur_Radius0_CopiesSrcToDst()
    {
        var src = Filled(4, 4, 0.5f, 0.25f, 0.75f, 1f);
        var dst = Empty(4, 4);

        BjorgeBlur.Blur(src, dst, 0f);

        await Assert.That(MaxDifference(src, dst)).IsLessThan(0.001f);
    }

    [Test]
    public async Task Blur_NegativeRadius_CopiesSrcToDst()
    {
        var src = Filled(4, 4, 0.5f, 0.25f, 0.75f, 1f);
        var dst = Empty(4, 4);

        BjorgeBlur.Blur(src, dst, -5f);

        await Assert.That(MaxDifference(src, dst)).IsLessThan(0.001f);
    }

    // Every kernel sums to 1, so a flat image must come back unchanged at any radius. With the
    // former 1/17 normalisation each up pass lost 1/17 of the brightness.
    [Test]
    [Arguments(64, 64, 15f)]
    [Arguments(37, 23, 7f)]
    [Arguments(200, 120, 63f)]
    public async Task Blur_FlatImage_StaysFlat(int width, int height, float radius)
    {
        var src = Filled(width, height, 0.5f, 0.25f, 0.75f, 1f);
        var dst = Empty(width, height);

        BjorgeBlur.Blur(src, dst, radius);

        await Assert.That(MaxDifference(src, dst)).IsLessThan(0.002f);
    }

    // More than two octaves used to need scratch buffers of two different sizes at once.
    [Test]
    public async Task Blur_SixOctaves_RunsOnSmallImage()
    {
        var src = Filled(96, 96, 1f, 1f, 1f, 1f);
        var dst = Empty(96, 96);

        BjorgeBlur.Blur(src, dst, 63f);

        await Assert.That(BjorgeBlur.EffectiveOctaves(63f, 96, 96)).IsEqualTo(6);
    }

    [Test]
    public async Task EffectiveOctaves_StopsAtOneTexel()
    {
        await Assert.That(BjorgeBlur.EffectiveOctaves(63f, 8, 8)).IsEqualTo(3);
        await Assert.That(BjorgeBlur.EffectiveOctaves(63f, 1, 100)).IsEqualTo(0);
    }

    [Test]
    public async Task Blur_PointSpreadsAndKeepsEnergy()
    {
        var src = Empty(64, 64);
        src.RowSpan(32)[32] = Rgba16f.From(1f, 1f, 1f, 1f);
        var dst = Empty(64, 64);

        BjorgeBlur.Blur(src, dst, 7f);

        float total = 0f;
        int lit = 0;
        for (int y = 0; y < 64; y++)
        {
            foreach (var p in dst.RowSpan(y))
            {
                total += (float)p.R;
                lit += (float)p.R > 0f ? 1 : 0;
            }
        }
        await Assert.That(lit).IsGreaterThan(16);
        await Assert.That(MathF.Abs(total - 1f)).IsLessThan(0.05f);
    }

    [Test]
    public async Task BlurTaps_KernelsSumToOne()
    {
        await Assert.That(4 * BlurTaps.DownWeight).IsEqualTo(1f);
        float up = 4 * BlurTaps.UpCornerWeight + 4 * BlurTaps.UpEdgeWeight + BlurTaps.UpCenterWeight;
        await Assert.That(up).IsEqualTo(1f);
    }

    [Test]
    [Arguments(0f, 0)]
    [Arguments(1f, 1)]
    [Arguments(3f, 2)]
    [Arguments(7f, 3)]
    [Arguments(15f, 4)]
    [Arguments(31f, 5)]
    [Arguments(63f, 6)]
    [Arguments(64f, 6)]
    public async Task OctaveCount_MatchesLog2Radius(float radius, int expected)
    {
        await Assert.That(DualFilterBlur.OctaveCount(radius)).IsEqualTo(expected);
    }

    private static float MaxDifference(Framebuffer a, Framebuffer b)
    {
        float max = 0f;
        for (int y = 0; y < a.Height; y++)
        {
            var rowA = a.RowSpan(y);
            var rowB = b.RowSpan(y);
            for (int x = 0; x < a.Width; x++)
            {
                max = MathF.Max(max, MathF.Abs((float)rowA[x].R - (float)rowB[x].R));
                max = MathF.Max(max, MathF.Abs((float)rowA[x].G - (float)rowB[x].G));
                max = MathF.Max(max, MathF.Abs((float)rowA[x].B - (float)rowB[x].B));
                max = MathF.Max(max, MathF.Abs((float)rowA[x].A - (float)rowB[x].A));
            }
        }
        return max;
    }
}
