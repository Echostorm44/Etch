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

    // 259 pixels: a whole number of every vectorised block size plus a ragged tail, so a block
    // loop that strides or offsets wrongly cannot hide behind a tidy length.
    private const int ConversionPixelCount = 259;

    // Guard bytes after the destination catch writes past the end of the span (heap corruption
    // that otherwise shows up later as an unrelated failure).
    private const int GuardLength = 64;
    private const byte GuardByte = 0xA5;

    [Test]
    public void DecodeBgra8ToLinearF16MatchesPerChannelReference()
    {
        var src = new byte[ConversionPixelCount * 4];
        for (int i = 0; i < ConversionPixelCount; i++)
        {
            src[i * 4 + 0] = (byte)i;
            src[i * 4 + 1] = (byte)(255 - i);
            src[i * 4 + 2] = (byte)(i / 2);
            src[i * 4 + 3] = (byte)(i * 7);
        }

        var dst = new Rgba16f[ConversionPixelCount];
        Srgb.DecodeBgra8ToLinearF16(src, dst);

        for (int i = 0; i < ConversionPixelCount; i++)
        {
            byte b = src[i * 4 + 0];
            byte g = src[i * 4 + 1];
            byte r = src[i * 4 + 2];
            byte a = src[i * 4 + 3];
            var expected = Rgba16f.From(Srgb.DecodeChannelScalar(r), Srgb.DecodeChannelScalar(g), Srgb.DecodeChannelScalar(b), a * (1.0f / 255.0f));
            var actual = dst[i];
            if (actual.R != expected.R || actual.G != expected.G || actual.B != expected.B || actual.A != expected.A)
            {
                throw new InvalidOperationException($"Decode mismatch at pixel {i}: expected ({expected.R}, {expected.G}, {expected.B}, {expected.A}), got ({actual.R}, {actual.G}, {actual.B}, {actual.A})");
            }
        }
    }

    [Test]
    public void EncodeLinearF16ToBgra8MatchesPerChannelReference()
    {
        Rgba16f[] src = CreateEncodeSource();
        byte[] buffer = CreateGuardedDestination(out Span<byte> dst);

        Srgb.EncodeLinearF16ToBgra8(src, dst);

        AssertGuardIntact(buffer);
        for (int i = 0; i < ConversionPixelCount; i++)
        {
            AssertEncodedChannel(dst, i, 0, Srgb.EncodeChannelScalar((float)src[i].B));
            AssertEncodedChannel(dst, i, 1, Srgb.EncodeChannelScalar((float)src[i].G));
            AssertEncodedChannel(dst, i, 2, Srgb.EncodeChannelScalar((float)src[i].R));
            AssertEncodedChannel(dst, i, 3, (byte)((float)src[i].A * 255.0f + 0.5f));
        }
    }

    [Test]
    public void EncodeLinearF16ToRgba8MatchesPerChannelReference()
    {
        Rgba16f[] src = CreateEncodeSource();
        byte[] buffer = CreateGuardedDestination(out Span<byte> dst);

        Srgb.EncodeLinearF16ToRgba8(src, dst);

        AssertGuardIntact(buffer);
        for (int i = 0; i < ConversionPixelCount; i++)
        {
            AssertEncodedChannel(dst, i, 0, Srgb.EncodeChannelScalar((float)src[i].R));
            AssertEncodedChannel(dst, i, 1, Srgb.EncodeChannelScalar((float)src[i].G));
            AssertEncodedChannel(dst, i, 2, Srgb.EncodeChannelScalar((float)src[i].B));
            AssertEncodedChannel(dst, i, 3, (byte)((float)src[i].A * 255.0f + 0.5f));
        }
    }

    private static Rgba16f[] CreateEncodeSource()
    {
        var src = new Rgba16f[ConversionPixelCount];
        for (int i = 0; i < ConversionPixelCount; i++)
        {
            int v = i & 0xFF;
            src[i] = Rgba16f.From(v / 255.0f, (255 - v) / 255.0f, (v / 2) / 255.0f, ((v * 7) & 0xFF) / 255.0f);
        }
        return src;
    }

    private static byte[] CreateGuardedDestination(out Span<byte> dst)
    {
        var buffer = new byte[ConversionPixelCount * 4 + GuardLength];
        Array.Fill(buffer, GuardByte);
        dst = buffer.AsSpan(0, ConversionPixelCount * 4);
        return buffer;
    }

    private static void AssertGuardIntact(byte[] buffer)
    {
        for (int i = ConversionPixelCount * 4; i < buffer.Length; i++)
        {
            if (buffer[i] != GuardByte)
            {
                throw new InvalidOperationException($"Encode wrote past the destination span (guard byte {i - ConversionPixelCount * 4} overwritten)");
            }
        }
    }

    private static void AssertEncodedChannel(Span<byte> dst, int pixel, int channel, byte expected)
    {
        byte actual = dst[pixel * 4 + channel];
        if (actual != expected)
        {
            throw new InvalidOperationException($"Encode mismatch at pixel {pixel} channel {channel}: expected {expected}, got {actual}");
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