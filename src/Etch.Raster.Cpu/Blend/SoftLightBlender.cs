using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

public static class SoftLightBlender
{
    public static void Blend(ReadOnlySpan<byte> coverage, Rgba16f paint, Span<Rgba16f> row)
    {
        BlendScalar(coverage, paint, row);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SoftLightChannel(float s, float d)
    {
        if (d < 0.25f)
            return ((16.0f * d - 12.0f) * d + 4.0f) * d * s;
        if (d < 0.5f)
            return s - (1.0f - 2.0f * d) * s * (1.0f - s);
        return s + (2.0f * d - 1.0f) * (D(s) - s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float D(float x)
    {
        return x <= 0.25f ? ((16.0f * x - 12.0f) * x + 4.0f) * x : MathF.Sqrt(x);
    }

    public static void BlendScalar(ReadOnlySpan<byte> coverage, Rgba16f paint, Span<Rgba16f> row)
    {
        float srcR = (float)paint.R;
        float srcG = (float)paint.G;
        float srcB = (float)paint.B;
        float srcA = (float)paint.A;

        int count = Math.Min(coverage.Length, row.Length);

        for (int i = 0; i < count; i++)
        {
            float coverageF = coverage[i] * (1.0f / 255.0f);
            float srcAlpha = srcA * coverageF;

            float dstR_premul = (float)row[i].R;
            float dstG_premul = (float)row[i].G;
            float dstB_premul = (float)row[i].B;
            float dstA = (float)row[i].A;

            float invDstA = dstA > 0.0001f ? 1.0f / dstA : 0.0f;
            float dstR = dstR_premul * invDstA;
            float dstG = dstG_premul * invDstA;
            float dstB = dstB_premul * invDstA;

            float blendedR = SoftLightChannel(srcR, dstR);
            float blendedG = SoftLightChannel(srcG, dstG);
            float blendedB = SoftLightChannel(srcB, dstB);

            float dstAlpha = 1.0f - srcAlpha;
            float resultA = srcAlpha + dstA * dstAlpha;

            if (resultA < 0.0001f)
            {
                row[i] = Rgba16f.Zero;
                continue;
            }

            row[i] = Rgba16f.From(
                blendedR * srcAlpha + dstR_premul * dstAlpha,
                blendedG * srcAlpha + dstG_premul * dstAlpha,
                blendedB * srcAlpha + dstB_premul * dstAlpha,
                resultA);
        }
    }
}
