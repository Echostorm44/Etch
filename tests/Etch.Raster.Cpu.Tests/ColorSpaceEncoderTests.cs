using System;
using System;
using System.Threading.Tasks;
using Etch.Gpu;

namespace Etch.Raster.Cpu.Tests;

internal sealed class ColorSpaceEncoderTests
{
    [Test]
    public async Task Encode_Srgb_ProducesCorrectOutputSize()
    {
        var pixels = new Rgba16f[64];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Rgba16f.From(0, 0, 0, 1);

        var output = new byte[64 * 4];
        ColorSpaceEncoder.Encode(pixels, output, ColorSpace.Srgb, 8, 8);

        for (int i = 0; i < 64 * 4; i += 4)
        {
            int r = output[i];
            int g = output[i + 1];
            int b = output[i + 2];
            int a = output[i + 3];
            await Assert.That(r).IsEqualTo(0);
            await Assert.That(g).IsEqualTo(0);
            await Assert.That(b).IsEqualTo(0);
            await Assert.That(a).IsEqualTo(255);
        }
    }

    [Test]
    public async Task Encode_Srgb_WhiteGoesTo255()
    {
        var pixels = new[] { Rgba16f.From(1, 1, 1, 1) };
        var output = new byte[4];
        ColorSpaceEncoder.Encode(pixels, output, ColorSpace.Srgb, 1, 1);

        await Assert.That((int)output[0]).IsEqualTo(255);
        await Assert.That((int)output[1]).IsEqualTo(255);
        await Assert.That((int)output[2]).IsEqualTo(255);
        await Assert.That((int)output[3]).IsEqualTo(255);
    }

    [Test]
    public async Task Encode_ScRgb_ProducesCorrectOutputSize()
    {
        var pixels = new Rgba16f[16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Rgba16f.From(0, 0, 0, 1);

        var output = new byte[16 * 8];
        ColorSpaceEncoder.Encode(pixels, output, ColorSpace.ScRgb, 4, 4);

        await Assert.That(output.Length).IsEqualTo(128);
    }

    [Test]
    public async Task Encode_ScRgb_HalfValueRoundtrip()
    {
        var pixels = new[] { Rgba16f.From(0.5f, 1f, 0.25f, 1f) };
        var output = new byte[8];
        ColorSpaceEncoder.Encode(pixels, output, ColorSpace.ScRgb, 1, 1);

        var r = BitConverter.ToHalf(output.AsSpan(0, 2));
        var g = BitConverter.ToHalf(output.AsSpan(2, 2));
        var b = BitConverter.ToHalf(output.AsSpan(4, 2));
        var a = BitConverter.ToHalf(output.AsSpan(6, 2));

        await Assert.That((float)r).IsEqualTo(0.5f);
        await Assert.That((float)a).IsEqualTo(1f);
    }

    [Test]
    public async Task ColorSpaceFormat_BytesPerPixel_DiffersBySpace()
    {
        await Assert.That(ColorSpaceFormat.BytesPerPixel(ColorSpace.Srgb)).IsEqualTo(4);
        await Assert.That(ColorSpaceFormat.BytesPerPixel(ColorSpace.DisplayP3)).IsEqualTo(4);
        await Assert.That(ColorSpaceFormat.BytesPerPixel(ColorSpace.ScRgb)).IsEqualTo(8);
    }

    [Test]
    public async Task ColorSpaceFormat_GetFormat_MatchesColorSpace()
    {
        await Assert.That(ColorSpaceFormat.GetFormat(ColorSpace.Srgb)).IsEqualTo(TextureFormat.Bgra8UnormSrgb);
        await Assert.That(ColorSpaceFormat.GetFormat(ColorSpace.ScRgb)).IsEqualTo(TextureFormat.Rgba16Float);
    }
}
