using System;
using Etch.ClipBlendGradient;

namespace Etch.Raster.Cpu.Gradients;

public static class SweepGradientRenderer
{
    public static void Paint(
        Span<Rgba16f> row,
        int xStart,
        int y,
        float centerX,
        float centerY,
        float startAngle,
        float endAngle,
        GradientExtend extendMode,
        ReadOnlySpan<Etch.ClipBlendGradient.Rgba16f> cbgLut)
    {
        float range = endAngle - startAngle;
        var lut = ExtendHelpers.ReinterpretLut(cbgLut);
        float lutSize = lut.Length - 1;

        for (int i = 0; i < row.Length; i++)
        {
            float px = xStart + i;
            float py = y;

            float dx = px - centerX;
            float dy = py - centerY;

            float angle = FastAtan2.AngleFromZeroToTwoPi(dy, dx);

            float t;
            if (MathF.Abs(range) < 0.0001f)
            {
                t = 0.0f;
            }
            else
            {
                t = (angle - startAngle) / range;
            }

            t = ExtendHelpers.Apply(t, extendMode);

            int lutIndex = (int)(t * lutSize);
            if (lutIndex < 0) lutIndex = 0;
            if (lutIndex >= lut.Length) lutIndex = lut.Length - 1;

            var srcColor = lut[lutIndex];
            row[i] = new Rgba16f(srcColor.R, srcColor.G, srcColor.B, srcColor.A);
        }
    }

    public static float SampleT(float px, float py, float centerX, float centerY, float startAngle, float endAngle)
    {
        float dx = px - centerX;
        float dy = py - centerY;

        float angle = FastAtan2.AngleFromZeroToTwoPi(dy, dx);
        float range = endAngle - startAngle;

        if (MathF.Abs(range) < 0.0001f)
            return 0.0f;

        return (angle - startAngle) / range;
    }
}
