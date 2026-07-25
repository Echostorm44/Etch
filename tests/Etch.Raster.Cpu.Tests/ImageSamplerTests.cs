using System;
using TUnit;
using Etch.Raster.Cpu.Image;
using Etch.Geometry;

namespace Etch.Raster.Cpu.Tests;

internal sealed class ImageExtendTests
{
    [Test]
    public async Task ExtendClamp_ReturnsCorrectValues()
    {
        await Assert.That(Extend.Clamp(-2, 4)).IsEqualTo(0);
        await Assert.That(Extend.Clamp(-1, 4)).IsEqualTo(0);
        await Assert.That(Extend.Clamp(0, 4)).IsEqualTo(0);
        await Assert.That(Extend.Clamp(1, 4)).IsEqualTo(1);
        await Assert.That(Extend.Clamp(2, 4)).IsEqualTo(2);
        await Assert.That(Extend.Clamp(3, 4)).IsEqualTo(3);
        await Assert.That(Extend.Clamp(4, 4)).IsEqualTo(3);
        await Assert.That(Extend.Clamp(5, 4)).IsEqualTo(3);
    }

    [Test]
    public async Task ExtendRepeat_ReturnsCorrectValues()
    {
        await Assert.That(Extend.Repeat(-4, 4)).IsEqualTo(0);
        await Assert.That(Extend.Repeat(-3, 4)).IsEqualTo(1);
        await Assert.That(Extend.Repeat(-2, 4)).IsEqualTo(2);
        await Assert.That(Extend.Repeat(-1, 4)).IsEqualTo(3);
        await Assert.That(Extend.Repeat(0, 4)).IsEqualTo(0);
        await Assert.That(Extend.Repeat(1, 4)).IsEqualTo(1);
        await Assert.That(Extend.Repeat(2, 4)).IsEqualTo(2);
        await Assert.That(Extend.Repeat(3, 4)).IsEqualTo(3);
        await Assert.That(Extend.Repeat(4, 4)).IsEqualTo(0);
    }

    [Test]
    public async Task ExtendMirror_ReturnsCorrectValues()
    {
        await Assert.That(Extend.Mirror(-8, 4)).IsEqualTo(0);
        await Assert.That(Extend.Mirror(-7, 4)).IsEqualTo(1);
        await Assert.That(Extend.Mirror(-6, 4)).IsEqualTo(2);
        await Assert.That(Extend.Mirror(-5, 4)).IsEqualTo(3);
        await Assert.That(Extend.Mirror(-4, 4)).IsEqualTo(3);
        await Assert.That(Extend.Mirror(-3, 4)).IsEqualTo(2);
        await Assert.That(Extend.Mirror(-2, 4)).IsEqualTo(1);
        await Assert.That(Extend.Mirror(-1, 4)).IsEqualTo(0);
        await Assert.That(Extend.Mirror(0, 4)).IsEqualTo(0);
        await Assert.That(Extend.Mirror(1, 4)).IsEqualTo(1);
        await Assert.That(Extend.Mirror(2, 4)).IsEqualTo(2);
        await Assert.That(Extend.Mirror(3, 4)).IsEqualTo(3);
        await Assert.That(Extend.Mirror(4, 4)).IsEqualTo(3);
        await Assert.That(Extend.Mirror(5, 4)).IsEqualTo(2);
        await Assert.That(Extend.Mirror(6, 4)).IsEqualTo(1);
        await Assert.That(Extend.Mirror(7, 4)).IsEqualTo(0);
    }

    [Test]
    public async Task ImageExtendMode_HasExpectedValues()
    {
        await Assert.That((int)ImageExtendMode.Pad).IsEqualTo(0);
        await Assert.That((int)ImageExtendMode.Repeat).IsEqualTo(1);
        await Assert.That((int)ImageExtendMode.Mirror).IsEqualTo(2);
    }

    [Test]
    public async Task SampleCoord_PadMode_UsesClamp()
    {
        await Assert.That(Extend.SampleCoord(-1, 4, ImageExtendMode.Pad)).IsEqualTo(0);
        await Assert.That(Extend.SampleCoord(5, 4, ImageExtendMode.Pad)).IsEqualTo(3);
    }

    [Test]
    public async Task SampleCoord_RepeatMode_UsesRepeat()
    {
        await Assert.That(Extend.SampleCoord(-1, 4, ImageExtendMode.Repeat)).IsEqualTo(3);
        await Assert.That(Extend.SampleCoord(5, 4, ImageExtendMode.Repeat)).IsEqualTo(1);
    }

    [Test]
    public async Task SampleCoord_MirrorMode_UsesMirror()
    {
        await Assert.That(Extend.SampleCoord(-1, 4, ImageExtendMode.Mirror)).IsEqualTo(0);
        await Assert.That(Extend.SampleCoord(5, 4, ImageExtendMode.Mirror)).IsEqualTo(2);
    }
}

internal sealed class BilinearSamplerTests
{
    [Test]
    public async Task BilinearSampler_IdentityTransform_DoesNotCrash()
    {
        var pixels = new byte[16 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 128;
            pixels[i + 2] = 64;
            pixels[i + 3] = 255;
        }

        var dst = new Rgba16f[16];
        var transform = Affine.Identity;

        BilinearSampler.Sample(pixels, 4, 4, dst, 4, transform);

        await Assert.That(dst.Length).IsEqualTo(16);
    }

    [Test]
    public async Task BilinearSampler_ScaleDown_DoesNotCrash()
    {
        var pixels = new byte[64 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 128;
            pixels[i + 2] = 64;
            pixels[i + 3] = 255;
        }

        var dst = new Rgba16f[16];
        var transform = Affine.Scale(0.5);

        BilinearSampler.Sample(pixels, 8, 8, dst, 4, transform);

        await Assert.That(dst.Length).IsEqualTo(16);
    }
}

internal sealed class BicubicSamplerTests
{
    [Test]
    public async Task BicubicSampler_IdentityTransform_DoesNotCrash()
    {
        var pixels = new byte[16 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 128;
            pixels[i + 2] = 64;
            pixels[i + 3] = 255;
        }

        var dst = new Rgba16f[16];
        var transform = Affine.Identity;

        BicubicSampler.Sample(pixels, 4, 4, dst, 4, transform);

        await Assert.That(dst.Length).IsEqualTo(16);
    }

    [Test]
    public async Task BicubicSampler_ScaleDown_DoesNotCrash()
    {
        var pixels = new byte[64 * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
            pixels[i + 1] = 128;
            pixels[i + 2] = 64;
            pixels[i + 3] = 255;
        }

        var dst = new Rgba16f[16];
        var transform = Affine.Scale(0.5);

        BicubicSampler.Sample(pixels, 8, 8, dst, 4, transform);

        await Assert.That(dst.Length).IsEqualTo(16);
    }
}
