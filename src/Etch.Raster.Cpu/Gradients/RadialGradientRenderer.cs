using System;
using Etch.ClipBlendGradient;

namespace Etch.Raster.Cpu.Gradients;

public static class RadialGradientRenderer
{
    public static void Paint(
        Span<Rgba16f> row,
        int xStart,
        int y,
        float centerX,
        float centerY,
        float radius,
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
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float t = dist / radius;
            t = ExtendHelpers.Apply(t, extendMode);

            int lutIndex = (int)(t * lutSize);
            if (lutIndex < 0) lutIndex = 0;
            if (lutIndex >= lut.Length) lutIndex = lut.Length - 1;

            var srcColor = lut[lutIndex];
            row[i] = new Rgba16f(srcColor.R, srcColor.G, srcColor.B, srcColor.A);
        }
    }

    public static float SampleT(float px, float py, float centerX, float centerY, float radius)
    {
        float dx = px - centerX;
        float dy = py - centerY;
        return MathF.Sqrt(dx * dx + dy * dy) / radius;
    }
}
