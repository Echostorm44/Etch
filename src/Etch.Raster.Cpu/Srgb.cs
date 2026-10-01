using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

public static partial class Srgb
{
    private static readonly float[] DecodeLutF = new float[256];
    private static readonly Half[] EncodeLutH = new Half[256];

    static Srgb()
    {
        for (int i = 0; i < 256; i++)
        {
            DecodeLutF[i] = DecodeChannelScalar((byte)i);
            EncodeLutH[i] = (Half)(i / 255.0f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DecodeChannelScalar(byte srgb)
    {
        float c = srgb * (1.0f / 255.0f);
        if (c <= 0.04045f)
        {
            return c * (1.0f / 12.92f);
        }

        float c2 = MathF.Pow((c + 0.055f) * (1.0f / 1.055f), 2.4f);
        return c2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte EncodeChannelScalar(float linear)
    {
        float c = MathF.Max(0.0f, MathF.Min(1.0f, linear));
        if (c <= 0.0031308f)
        {
            return (byte)(c * 12.92f * 255.0f + 0.5f);
        }

        return (byte)((1.055f * MathF.Pow(c, 1.0f / 2.4f) - 0.055f) * 255.0f + 0.5f);
    }

    // The bulk conversions are per-channel LUT lookups and pow calls with no vector form worth
    // having; they used to dispatch to "AVX2"/"NEON" paths that were the same scalar work behind
    // wrong block offsets (garbage decode on x64, writes past the end of dst on ARM64).
    public static void DecodeBgra8ToLinearF16(ReadOnlySpan<byte> src, Span<Rgba16f> dst)
    {
        if (src.Length != dst.Length * 4)
        {
            Panic.ArgumentOutOfRange(nameof(src), "src must be 4 * dst.Length bytes");
        }

        for (int i = 0; i < dst.Length; i++)
        {
            int idx = i * 4;
            float b = DecodeLutF[src[idx + 0]];
            float g = DecodeLutF[src[idx + 1]];
            float r = DecodeLutF[src[idx + 2]];
            float a = src[idx + 3] * (1.0f / 255.0f);
            dst[i] = Rgba16f.From(r, g, b, a);
        }
    }

    public static void EncodeLinearF16ToBgra8(ReadOnlySpan<Rgba16f> src, Span<byte> dst)
    {
        if (dst.Length != src.Length * 4)
        {
            Panic.ArgumentOutOfRange(nameof(dst), "dst must be 4 * src.Length bytes");
        }

        for (int i = 0; i < src.Length; i++)
        {
            int idx = i * 4;
            dst[idx + 0] = EncodeChannelScalar((float)src[i].B);
            dst[idx + 1] = EncodeChannelScalar((float)src[i].G);
            dst[idx + 2] = EncodeChannelScalar((float)src[i].R);
            dst[idx + 3] = EncodeAlpha((float)src[i].A);
        }
    }

    public static void EncodeLinearF16ToRgba8(ReadOnlySpan<Rgba16f> src, Span<byte> dst)
    {
        if (dst.Length != src.Length * 4)
        {
            Panic.ArgumentOutOfRange(nameof(dst), "dst must be 4 * src.Length bytes");
        }

        for (int i = 0; i < src.Length; i++)
        {
            int idx = i * 4;
            dst[idx + 0] = EncodeChannelScalar((float)src[i].R);
            dst[idx + 1] = EncodeChannelScalar((float)src[i].G);
            dst[idx + 2] = EncodeChannelScalar((float)src[i].B);
            dst[idx + 3] = EncodeAlpha((float)src[i].A);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte EncodeAlpha(float alpha)
    {
        return (byte)(alpha * 255.0f + 0.5f);
    }
}
