using System;
using System.Runtime.InteropServices;
using Etch.ClipBlendGradient;

namespace Etch.Raster.Cpu.Gradients;

public static class LinearGradientRenderer
{
    public static void Paint(
        Span<Rgba16f> row,
        int xStart,
        int y,
        float x0,
        float y0,
        float x1,
        float y1,
        GradientExtend extendMode,
        ReadOnlySpan<Etch.ClipBlendGradient.Rgba16f> cbgLut)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float lenSq = dx * dx + dy * dy;

        var lut = ExtendHelpers.ReinterpretLut(cbgLut);
        float lutSize = lut.Length - 1;

        for (int i = 0; i < row.Length; i++)
        {
            float px = xStart + i;
            float py = y;

            float t;
            if (lenSq < 0.0001f)
            {
                t = 0.0f;
            }
            else
            {
                t = ((px - x0) * dx + (py - y0) * dy) / lenSq;
            }

            t = ExtendHelpers.Apply(t, extendMode);

            int lutIndex = (int)(t * lutSize);
            if (lutIndex < 0) lutIndex = 0;
            if (lutIndex >= lut.Length) lutIndex = lut.Length - 1;

            var srcColor = lut[lutIndex];
            row[i] = new Rgba16f(srcColor.R, srcColor.G, srcColor.B, srcColor.A);
        }
    }

    public static float SampleT(float px, float py, float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0;
        float dy = y1 - y0;
        float lenSq = dx * dx + dy * dy;

        if (lenSq < 0.0001f)
            return 0.0f;

        return ((px - x0) * dx + (py - y0) * dy) / lenSq;
    }
}
