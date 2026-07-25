using System;
using Etch.Raster.Cpu;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class SrgbTests
{
    [Test]
    public void DecodeChannelScalarRoundtrip()
    {
        for (int i = 0; i <= 255; i++)
        {
            byte b = (byte)i;
            float linear = Srgb.DecodeChannelScalar(b);
            byte back = Srgb.EncodeChannelScalar(linear);
            int diff = Math.Abs(back - b);
            if (diff > 1)
                throw new InvalidOperationException($"Roundtrip error for {b}: got {back}, diff={diff}");
        }
    }

    [Test]
    public void DecodeBgra8ToLinearF16MatchesScalar()
    {
        var src = new byte[256 * 4];
        for (int i = 0; i < 256; i++)
        {
            src[i * 4 + 0] = (byte)i;
            src[i * 4 + 1] = (byte)(255 - i);
            src[i * 4 + 2] = (byte)(i / 2);
            src[i * 4 + 3] = 255;
        }

        var dstSimd = new Rgba16f[256];
        var dstScalar = new Rgba16f[256];

        Srgb.DecodeBgra8ToLinearF16(src, dstScalar);
        Srgb.DecodeBgra8ToLinearF16(src, dstSimd);

        for (int i = 0; i < 256; i++)
        {
            var ps = dstScalar[i];
            var pm = dstSimd[i];
            if (Math.Abs((float)ps.R - (float)pm.R) > 0.0001f ||
                Math.Abs((float)ps.G - (float)pm.G) > 0.0001f ||
                Math.Abs((float)ps.B - (float)pm.B) > 0.0001f ||
                Math.Abs((float)ps.A - (float)pm.A) > 0.0001f)
            {
                throw new InvalidOperationException($"SIMD/scalar mismatch at {i}");
            }
        }
    }

    [Test]
    public void EncodeLinearF16ToBgra8MatchesScalar()
    {
        var src = new Rgba16f[256];
        for (int i = 0; i < 256; i++)
        {
            src[i] = Rgba16f.From(i / 255.0f, (255 - i) / 255.0f, (i / 2) / 255.0f, 1.0f);
        }

        var dstSimd = new byte[256 * 4];
        var dstScalar = new byte[256 * 4];

        Srgb.EncodeLinearF16ToBgra8(src, dstScalar);
        Srgb.EncodeLinearF16ToBgra8(src, dstSimd);

        for (int i = 0; i < 256 * 4; i++)
        {
            if (dstSimd[i] != dstScalar[i])
                throw new InvalidOperationException($"SIMD/scalar mismatch at byte {i}: SIMD={dstSimd[i]}, Scalar={dstScalar[i]}");
        }
    }

    [Test]
    public void FullColorCubeRoundtripMaxError1Per255()
    {
        int errors = 0;
        int maxError = 0;

        for (int r = 0; r <= 255; r++)
        {
            for (int g = 0; g <= 255; g++)
            {
                for (int b = 0; b <= 255; b++)
                {
                    byte rBack = Srgb.EncodeChannelScalar(Srgb.DecodeChannelScalar((byte)r));
                    byte gBack = Srgb.EncodeChannelScalar(Srgb.DecodeChannelScalar((byte)g));
                    byte bBack = Srgb.EncodeChannelScalar(Srgb.DecodeChannelScalar((byte)b));

                    int rErr = Math.Abs(rBack - r);
                    int gErr = Math.Abs(gBack - g);
                    int bErr = Math.Abs(bBack - b);

                    if (rErr > 1 || gErr > 1 || bErr > 1)
                    {
                        errors++;
                        maxError = Math.Max(maxError, Math.Max(rErr, Math.Max(gErr, bErr)));
                    }
                }
            }
        }

        if (errors > 0)
            throw new InvalidOperationException($"Color cube errors: {errors}, maxError={maxError}");
    }
}