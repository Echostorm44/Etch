using System;
using Etch.ClipBlendGradient;

namespace Etch.Raster.Cpu.Gradients;

public static class ConicGradientRenderer
{
    private const float TwoPi = 2.0f * MathF.PI;

    public static void Paint(
        Span<Rgba16f> row,
        int xStart,
        int y,
        float centerX,
        float centerY,
        GradientExtend extendMode,
        ReadOnlySpan<Etch.ClipBlendGradient.Rgba16f> cbgLut)
    {
        var lut = ExtendHelpers.ReinterpretLut(cbgLut);
        float lutSize = lut.Length - 1;

        for (int i = 0; i < row.Length; i++)
        {
            float px = xStart + i;
            float py = y;

            float dx = px - centerX;
            float dy = py - centerY;

            float angle = FastAtan2.AngleFromZeroToTwoPi(dy, dx);
            float t = angle / TwoPi;

            t = ExtendHelpers.Apply(t, extendMode);

            int lutIndex = (int)(t * lutSize);
            if (lutIndex < 0) lutIndex = 0;
            if (lutIndex >= lut.Length) lutIndex = lut.Length - 1;

            var srcColor = lut[lutIndex];
            row[i] = new Rgba16f(srcColor.R, srcColor.G, srcColor.B, srcColor.A);
        }
    }

    public static float SampleAngle(float px, float py, float centerX, float centerY)
    {
        float dx = px - centerX;
        float dy = py - centerY;
        return FastAtan2.AngleFromZeroToTwoPi(dy, dx);
    }
}
