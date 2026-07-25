using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

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

    public static void DecodeBgra8ToLinearF16(ReadOnlySpan<byte> src, Span<Rgba16f> dst)
    {
        if (src.Length != dst.Length * 4)
        {
            Panic.ArgumentOutOfRange(nameof(src), "src must be 4 * dst.Length bytes");
        }

        int count = dst.Length;
        if (Avx2.IsSupported && count >= 16)
        {
            DecodeAvx2(src, dst, count);
        }
        else if (AdvSimd.IsSupported && count >= 8)
        {
            DecodeNeon(src, dst, count);
        }
        else
        {
            DecodeScalar(src, dst, count);
        }
    }

    public static void EncodeLinearF16ToBgra8(ReadOnlySpan<Rgba16f> src, Span<byte> dst)
    {
        if (dst.Length != src.Length * 4)
        {
            Panic.ArgumentOutOfRange(nameof(dst), "dst must be 4 * src.Length bytes");
        }

        int count = src.Length;
        if (Avx2.IsSupported && count >= 16)
        {
            EncodeAvx2(src, dst, count);
        }
        else if (AdvSimd.IsSupported && count >= 8)
        {
            EncodeNeon(src, dst, count);
        }
        else
        {
            EncodeScalar(src, dst, count);
        }
    }

    public static void EncodeLinearF16ToRgba8(ReadOnlySpan<Rgba16f> src, Span<byte> dst)
    {
        if (dst.Length != src.Length * 4)
        {
            Panic.ArgumentOutOfRange(nameof(dst), "dst must be 4 * src.Length bytes");
        }

        int count = src.Length;
        if (Avx2.IsSupported && count >= 16)
        {
            EncodeAvx2Rgba(src, dst, count);
        }
        else if (AdvSimd.IsSupported && count >= 8)
        {
            EncodeNeonRgba(src, dst, count);
        }
        else
        {
            EncodeScalarRgba(src, dst, count);
        }
    }

    private static unsafe void DecodeScalar(ReadOnlySpan<byte> src, Span<Rgba16f> dst, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int idx = i * 4;
            float b = DecodeLutF[src[idx + 0]];
            float g = DecodeLutF[src[idx + 1]];
            float r = DecodeLutF[src[idx + 2]];
            float a = src[idx + 3] * (1.0f / 255.0f);
            dst[i] = Rgba16f.From(r, g, b, a);
        }
    }

    private static unsafe void EncodeScalar(ReadOnlySpan<Rgba16f> src, Span<byte> dst, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int idx = i * 4;
            float r = (float)src[i].R;
            float g = (float)src[i].G;
            float b = (float)src[i].B;
            float a = (float)src[i].A;
            dst[idx + 0] = EncodeChannelScalar(b);
            dst[idx + 1] = EncodeChannelScalar(g);
            dst[idx + 2] = EncodeChannelScalar(r);
            dst[idx + 3] = (byte)(a * 255.0f + 0.5f);
        }
    }

    private static unsafe void DecodeAvx2(ReadOnlySpan<byte> src, Span<Rgba16f> dst, int count)
    {
        fixed (byte* srcPtr = src)
        fixed (Rgba16f* dstPtr = dst)
        {
            int i = 0;
            for (; i + 16 <= count; i += 16)
            {
                int srcIdx = i * 4;

                var bRaw0 = Avx2.LoadVector128(srcPtr + srcIdx + 0);
                var gRaw0 = Avx2.LoadVector128(srcPtr + srcIdx + 4);
                var rRaw0 = Avx2.LoadVector128(srcPtr + srcIdx + 8);
                var aRaw0 = Avx2.LoadVector128(srcPtr + srcIdx + 12);

                var bRaw1 = Avx2.LoadVector128(srcPtr + srcIdx + 64);
                var gRaw1 = Avx2.LoadVector128(srcPtr + srcIdx + 68);
                var rRaw1 = Avx2.LoadVector128(srcPtr + srcIdx + 72);
                var aRaw1 = Avx2.LoadVector128(srcPtr + srcIdx + 76);

                DecodeBlockAvx2(bRaw0, gRaw0, rRaw0, aRaw0, dstPtr + i);
                DecodeBlockAvx2(bRaw1, gRaw1, rRaw1, aRaw1, dstPtr + i + 8);
            }

            if (i < count)
            {
                DecodeScalar(src.Slice(i * 4), dst.Slice(i), count - i);
            }
        }
    }

    private static unsafe void EncodeAvx2(ReadOnlySpan<Rgba16f> src, Span<byte> dst, int count)
    {
        fixed (Rgba16f* srcPtr = src)
        fixed (byte* dstPtr = dst)
        {
            int i = 0;
            for (; i + 16 <= count; i += 16)
            {
                int dstIdx = i * 4;
                EncodeBlockAvx2(srcPtr + i, dstPtr + dstIdx);
                EncodeBlockAvx2(srcPtr + i + 8, dstPtr + dstIdx + 32);
            }

            if (i < count)
            {
                EncodeScalar(src.Slice(i), dst.Slice(i * 4), count - i);
            }
        }
    }

    private static unsafe void DecodeNeon(ReadOnlySpan<byte> src, Span<Rgba16f> dst, int count)
    {
        fixed (byte* srcPtr = src)
        fixed (Rgba16f* dstPtr = dst)
        {
            int i = 0;
            for (; i + 8 <= count; i += 8)
            {
                int srcIdx = i * 4;

                var bRaw0 = AdvSimd.LoadVector128(srcPtr + srcIdx + 0);
                var gRaw0 = AdvSimd.LoadVector128(srcPtr + srcIdx + 4);
                var rRaw0 = AdvSimd.LoadVector128(srcPtr + srcIdx + 8);
                var aRaw0 = AdvSimd.LoadVector128(srcPtr + srcIdx + 12);

                var bRaw1 = AdvSimd.LoadVector128(srcPtr + srcIdx + 32);
                var gRaw1 = AdvSimd.LoadVector128(srcPtr + srcIdx + 36);
                var rRaw1 = AdvSimd.LoadVector128(srcPtr + srcIdx + 40);
                var aRaw1 = AdvSimd.LoadVector128(srcPtr + srcIdx + 44);

                DecodeBlockNeon(bRaw0, gRaw0, rRaw0, aRaw0, dstPtr + i);
                DecodeBlockNeon(bRaw1, gRaw1, rRaw1, aRaw1, dstPtr + i + 4);
            }

            if (i < count)
            {
                DecodeScalar(src.Slice(i * 4), dst.Slice(i), count - i);
            }
        }
    }

    private static unsafe void EncodeNeon(ReadOnlySpan<Rgba16f> src, Span<byte> dst, int count)
    {
        fixed (Rgba16f* srcPtr = src)
        fixed (byte* dstPtr = dst)
        {
            int i = 0;
            for (; i + 8 <= count; i += 8)
            {
                int dstIdx = i * 4;
                EncodeBlockNeon(srcPtr + i, dstPtr + dstIdx);
                EncodeBlockNeon(srcPtr + i + 4, dstPtr + dstIdx + 64);
            }

            if (i < count)
            {
                EncodeScalar(src.Slice(i), dst.Slice(i * 4), count - i);
            }
        }
    }

    private static unsafe void DecodeBlockAvx2(Vector128<byte> bRaw, Vector128<byte> gRaw, Vector128<byte> rRaw, Vector128<byte> aRaw, Rgba16f* dst)
    {
        for (int j = 0; j < 8; j++)
        {
            byte b = bRaw.GetElement(j);
            byte g = gRaw.GetElement(j);
            byte r = rRaw.GetElement(j);
            byte a = aRaw.GetElement(j);
            dst[j] = Rgba16f.From(DecodeLutF[r], DecodeLutF[g], DecodeLutF[b], a * (1.0f / 255.0f));
        }
    }

    private static unsafe void EncodeBlockAvx2(Rgba16f* src, byte* dst)
    {
        for (int j = 0; j < 8; j++)
        {
            int idx = j * 4;
            float r = (float)src[j].R;
            float g = (float)src[j].G;
            float b = (float)src[j].B;
            float a = (float)src[j].A;
            dst[idx + 0] = EncodeChannelScalar(b);
            dst[idx + 1] = EncodeChannelScalar(g);
            dst[idx + 2] = EncodeChannelScalar(r);
            dst[idx + 3] = (byte)(a * 255.0f + 0.5f);
        }
    }

    private static unsafe void DecodeBlockNeon(Vector128<byte> bRaw, Vector128<byte> gRaw, Vector128<byte> rRaw, Vector128<byte> aRaw, Rgba16f* dst)
    {
        for (int j = 0; j < 4; j++)
        {
            byte b = bRaw.GetElement(j);
            byte g = gRaw.GetElement(j);
            byte r = rRaw.GetElement(j);
            byte a = aRaw.GetElement(j);
            dst[j] = Rgba16f.From(DecodeLutF[r], DecodeLutF[g], DecodeLutF[b], a * (1.0f / 255.0f));
        }
    }

    private static unsafe void EncodeBlockNeon(Rgba16f* src, byte* dst)
    {
        for (int j = 0; j < 4; j++)
        {
            int idx = j * 4;
            float r = (float)src[j].R;
            float g = (float)src[j].G;
            float b = (float)src[j].B;
            float a = (float)src[j].A;
            dst[idx + 0] = EncodeChannelScalar(b);
            dst[idx + 1] = EncodeChannelScalar(g);
            dst[idx + 2] = EncodeChannelScalar(r);
            dst[idx + 3] = (byte)(a * 255.0f + 0.5f);
        }
    }

    private static unsafe void EncodeScalarRgba(ReadOnlySpan<Rgba16f> src, Span<byte> dst, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int idx = i * 4;
            float r = (float)src[i].R;
            float g = (float)src[i].G;
            float b = (float)src[i].B;
            float a = (float)src[i].A;
            dst[idx + 0] = EncodeChannelScalar(b);
            dst[idx + 1] = EncodeChannelScalar(g);
            dst[idx + 2] = EncodeChannelScalar(r);
            dst[idx + 3] = (byte)(a * 255.0f + 0.5f);
        }
    }

    private static unsafe void EncodeAvx2Rgba(ReadOnlySpan<Rgba16f> src, Span<byte> dst, int count)
    {
        fixed (Rgba16f* srcPtr = src)
        fixed (byte* dstPtr = dst)
        {
            int i = 0;
            for (; i + 16 <= count; i += 16)
            {
                int dstIdx = i * 4;
                EncodeBlockAvx2Rgba(srcPtr + i, dstPtr + dstIdx);
                EncodeBlockAvx2Rgba(srcPtr + i + 8, dstPtr + dstIdx + 32);
            }

            if (i < count)
            {
                EncodeScalarRgba(src.Slice(i), dst.Slice(i * 4), count - i);
            }
        }
    }

    private static unsafe void EncodeNeonRgba(ReadOnlySpan<Rgba16f> src, Span<byte> dst, int count)
    {
        fixed (Rgba16f* srcPtr = src)
        fixed (byte* dstPtr = dst)
        {
            int i = 0;
            for (; i + 8 <= count; i += 8)
            {
                int dstIdx = i * 4;
                EncodeBlockNeonRgba(srcPtr + i, dstPtr + dstIdx);
                EncodeBlockNeonRgba(srcPtr + i + 4, dstPtr + dstIdx + 64);
            }

            if (i < count)
            {
                EncodeScalarRgba(src.Slice(i), dst.Slice(i * 4), count - i);
            }
        }
    }

    private static unsafe void EncodeBlockAvx2Rgba(Rgba16f* src, byte* dst)
    {
        for (int j = 0; j < 8; j++)
        {
            int idx = j * 4;
            float r = (float)src[j].R;
            float g = (float)src[j].G;
            float b = (float)src[j].B;
            float a = (float)src[j].A;
            dst[idx + 0] = EncodeChannelScalar(b);
            dst[idx + 1] = EncodeChannelScalar(g);
            dst[idx + 2] = EncodeChannelScalar(r);
            dst[idx + 3] = (byte)(a * 255.0f + 0.5f);
        }
    }

    private static unsafe void EncodeBlockNeonRgba(Rgba16f* src, byte* dst)
    {
        for (int j = 0; j < 4; j++)
        {
            int idx = j * 4;
            float r = (float)src[j].R;
            float g = (float)src[j].G;
            float b = (float)src[j].B;
            float a = (float)src[j].A;
            dst[idx + 0] = EncodeChannelScalar(b);
            dst[idx + 1] = EncodeChannelScalar(g);
            dst[idx + 2] = EncodeChannelScalar(r);
            dst[idx + 3] = (byte)(a * 255.0f + 0.5f);
        }
    }
}