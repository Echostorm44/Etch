using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

public static class SaturationBlender
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

            // Saturation: B(Cb, Cs) = SetLum(SetSat(Cb, Sat(Cs)), Lum(Cb))
            float br = dstR, bg = dstG, bb = dstB;
            NonSeparableBlendHelpers.SetSat(ref br, ref bg, ref bb, NonSeparableBlendHelpers.Sat(srcR, srcG, srcB));
            NonSeparableBlendHelpers.SetLum(ref br, ref bg, ref bb, NonSeparableBlendHelpers.Lum(dstR, dstG, dstB));

            float dstAlpha = 1.0f - srcAlpha;
            float resultA = srcAlpha + dstA * dstAlpha;

            if (resultA < 0.0001f)
            {
                row[i] = Rgba16f.Zero;
                continue;
            }

            float r = br * srcAlpha + dstR_premul * dstAlpha;
            float g = bg * srcAlpha + dstG_premul * dstAlpha;
            float b = bb * srcAlpha + dstB_premul * dstAlpha;

            row[i] = Rgba16f.From(r, g, b, resultA);
        }
    }
}
