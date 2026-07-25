using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

public static class NormalBlender
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

            float dstR = (float)row[i].R;
            float dstG = (float)row[i].G;
            float dstB = (float)row[i].B;
            float dstA = (float)row[i].A;

            float dstAlpha = 1.0f - srcAlpha;

            row[i] = Rgba16f.From(
                srcR * srcAlpha + dstR * dstAlpha,
                srcG * srcAlpha + dstG * dstAlpha,
                srcB * srcAlpha + dstB * dstAlpha,
                srcAlpha + dstA * dstAlpha);
        }
    }
}
