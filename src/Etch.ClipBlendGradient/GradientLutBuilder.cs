using System;

namespace Etch.ClipBlendGradient;

public static class GradientLutBuilder
{
    public const int LutSize = 256;

    public static Rgba16f[] Build(ReadOnlySpan<GradientStop> stops, GradientInterpolationSpace space)
    {
        if (stops.Length == 0)
        {
            var result = new Rgba16f[LutSize];
            for (int i = 0; i < LutSize; i++)
                result[i] = Rgba16f.Zero;
            return result;
        }

        GradientStop[] sortedStops = SortStops(stops);

        if (sortedStops.Length == 1)
        {
            var result = new Rgba16f[LutSize];
            var color = sortedStops[0].Color;
            for (int i = 0; i < LutSize; i++)
                result[i] = color;
            return result;
        }

        var lut = new Rgba16f[LutSize];

        for (int i = 0; i < LutSize; i++)
        {
            float t = i / (float)(LutSize - 1);
            lut[i] = SampleAt(sortedStops, t, space);
        }

        return lut;
    }

    public static void BuildInto(ReadOnlySpan<GradientStop> stops, GradientInterpolationSpace space, Span<Rgba16f> lut)
    {
        if (stops.Length == 0)
        {
            for (int i = 0; i < lut.Length; i++)
                lut[i] = Rgba16f.Zero;
            return;
        }

        GradientStop[] sortedStops = SortStops(stops);

        if (sortedStops.Length == 1)
        {
            var color = sortedStops[0].Color;
            for (int i = 0; i < lut.Length; i++)
                lut[i] = color;
            return;
        }

        for (int i = 0; i < lut.Length; i++)
        {
            float t = i / (float)(lut.Length - 1);
            lut[i] = SampleAt(sortedStops, t, space);
        }
    }

    private static GradientStop[] SortStops(ReadOnlySpan<GradientStop> stops)
    {
        var sorted = new GradientStop[stops.Length];
        for (int i = 0; i < stops.Length; i++)
            sorted[i] = stops[i];

        for (int i = 1; i < sorted.Length; i++)
        {
            var key = sorted[i];
            int j = i - 1;
            while (j >= 0 && sorted[j].Position > key.Position)
            {
                sorted[j + 1] = sorted[j];
                j--;
            }
            sorted[j + 1] = key;
        }

        return sorted;
    }

    private static Rgba16f SampleAt(GradientStop[] stops, float t, GradientInterpolationSpace space)
    {
        GradientExtend extend = GradientExtend.Pad;

        t = Extend(extend, t);

        if (t <= stops[0].Position)
            return stops[0].Color;

        if (t >= stops[stops.Length - 1].Position)
            return stops[stops.Length - 1].Color;

        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (t >= stops[i].Position && t < stops[i + 1].Position)
            {
                float localT = (t - stops[i].Position) / (stops[i + 1].Position - stops[i].Position);

                Rgba16f c0 = stops[i].Color;
                Rgba16f c1 = stops[i + 1].Color;

                float r, g, b, a;

                if (space == GradientInterpolationSpace.Srgb)
                {
                    float r0Srgb = LinearToSrgb(c0.RLinear);
                    float g0Srgb = LinearToSrgb(c0.GLinear);
                    float b0Srgb = LinearToSrgb(c0.BLinear);
                    float r1Srgb = LinearToSrgb(c1.RLinear);
                    float g1Srgb = LinearToSrgb(c1.GLinear);
                    float b1Srgb = LinearToSrgb(c1.BLinear);

                    float rSrgb = r0Srgb + localT * (r1Srgb - r0Srgb);
                    float gSrgb = g0Srgb + localT * (g1Srgb - g0Srgb);
                    float bSrgb = b0Srgb + localT * (b1Srgb - b0Srgb);

                    r = SrgbToLinear(rSrgb);
                    g = SrgbToLinear(gSrgb);
                    b = SrgbToLinear(bSrgb);
                }
                else
                {
                    r = c0.RLinear + localT * (c1.RLinear - c0.RLinear);
                    g = c0.GLinear + localT * (c1.GLinear - c0.GLinear);
                    b = c0.BLinear + localT * (c1.BLinear - c0.BLinear);
                }

                a = c0.ALinear + localT * (c1.ALinear - c0.ALinear);

                return Rgba16f.From(r, g, b, a);
            }
        }

        return stops[stops.Length - 1].Color;
    }

    private static float Extend(GradientExtend extend, float t)
    {
        return t;
    }

    private static float SrgbToLinear(float srgb)
    {
        if (srgb <= 0.04045f)
            return srgb / 12.92f;
        float a = 0.055f;
        float x = (srgb + a) / (1.0f + a);
        return x * x * x;
    }

    private static float LinearToSrgb(float linear)
    {
        if (linear <= 0.0031308f)
            return linear * 12.92f;
        float a = 0.055f;
        return (1.0f + a) * MathF.Pow(linear, 1.0f / 3.0f) - a;
    }
}
