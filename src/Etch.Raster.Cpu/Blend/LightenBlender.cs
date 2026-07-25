using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

public static class LightenBlender
{
    public static void Blend(ReadOnlySpan<byte> coverage, Rgba16f paint, Span<Rgba16f> row)
    {
        BlendScalar(coverage, paint, row);
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

            float blendedR = Math.Max(srcR, dstR);
            float blendedG = Math.Max(srcG, dstG);
            float blendedB = Math.Max(srcB, dstB);

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
