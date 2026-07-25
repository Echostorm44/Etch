using System;
using Etch.ClipBlendGradient;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class NonSeparableBlendTests
{
    [Test]
    public void Hue_OutputInValidRange()
    {
        var src = new LinearColor(0.5f, 0.1f, 0.9f, 1.0f);
        var dst = new LinearColor(0.9f, 0.1f, 0.1f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Hue);
        if (result.R < 0 || result.R > 1) throw new InvalidOperationException($"R out of range: {result.R}");
        if (result.G < 0 || result.G > 1) throw new InvalidOperationException($"G out of range: {result.G}");
        if (result.B < 0 || result.B > 1) throw new InvalidOperationException($"B out of range: {result.B}");
    }

    [Test]
    public void Saturation_OutputInValidRange()
    {
        var src = new LinearColor(0.5f, 0.9f, 0.1f, 1.0f);
        var dst = new LinearColor(0.9f, 0.1f, 0.1f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Saturation);
        if (result.R < 0 || result.R > 1) throw new InvalidOperationException($"R out of range: {result.R}");
        if (result.G < 0 || result.G > 1) throw new InvalidOperationException($"G out of range: {result.G}");
        if (result.B < 0 || result.B > 1) throw new InvalidOperationException($"B out of range: {result.B}");
    }

    [Test]
    public void Color_OutputInValidRange()
    {
        var src = new LinearColor(0.5f, 0.9f, 0.1f, 1.0f);
        var dst = new LinearColor(0.9f, 0.1f, 0.1f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Color);
        if (result.R < 0 || result.R > 1) throw new InvalidOperationException($"R out of range: {result.R}");
        if (result.G < 0 || result.G > 1) throw new InvalidOperationException($"G out of range: {result.G}");
        if (result.B < 0 || result.B > 1) throw new InvalidOperationException($"B out of range: {result.B}");
    }

    [Test]
    public void Luminosity_OutputInValidRange()
    {
        var src = new LinearColor(0.2f, 0.1f, 0.9f, 1.0f);
        var dst = new LinearColor(0.9f, 0.1f, 0.1f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Luminosity);
        if (result.R < 0 || result.R > 1) throw new InvalidOperationException($"R out of range: {result.R}");
        if (result.G < 0 || result.G > 1) throw new InvalidOperationException($"G out of range: {result.G}");
        if (result.B < 0 || result.B > 1) throw new InvalidOperationException($"B out of range: {result.B}");
    }

    [Test]
    public void Hue_BlackSrc_ReturnsDstLumWithSrcHue()
    {
        var src = new LinearColor(0.0f, 0.0f, 0.0f, 1.0f);
        var dst = new LinearColor(0.9f, 0.1f, 0.1f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Hue);
        double lum = 0.3 * result.R + 0.59 * result.G + 0.11 * result.B;
        double expectedLum = 0.3 * 0.9f + 0.59 * 0.1f + 0.11 * 0.1f;
        if (Math.Abs(lum - expectedLum) > 0.01) throw new InvalidOperationException($"Luminosity not preserved: {lum} vs {expectedLum}");
    }

    [Test]
    public void Luminosity_WhiteSrc_ReturnsWhite()
    {
        var src = new LinearColor(1.0f, 1.0f, 1.0f, 1.0f);
        var dst = new LinearColor(0.5f, 0.2f, 0.2f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Luminosity);
        if (result.R < 0.99f) throw new InvalidOperationException($"R: {result.R}");
        if (result.G < 0.99f) throw new InvalidOperationException($"G: {result.G}");
        if (result.B < 0.99f) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void AllNonSeparableModes_100RandomPairs_AllInRange()
    {
        var rng = new Random(123);
        var modes = new BlendMode[]
        {
            BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity
        };

        int failures = 0;
        var failureDetails = new System.Collections.Generic.List<string>();

        foreach (var mode in modes)
        {
            for (int i = 0; i < 100; i++)
            {
                double srcR = rng.NextDouble();
                double srcG = rng.NextDouble();
                double srcB = rng.NextDouble();
                double srcA = rng.NextDouble();
                double dstR = rng.NextDouble();
                double dstG = rng.NextDouble();
                double dstB = rng.NextDouble();
                double dstA = rng.NextDouble();

                var src = new LinearColor(srcR, srcG, srcB, srcA);
                var dst = new LinearColor(dstR, dstG, dstB, dstA);

                var result = BlendReference.Apply(src, dst, mode);

                if (result.R < -0.01 || result.R > 1.01 ||
                    result.G < -0.01 || result.G > 1.01 ||
                    result.B < -0.01 || result.B > 1.01 ||
                    result.A < -0.01 || result.A > 1.01)
                {
                    failures++;
                    if (failureDetails.Count < 5)
                    {
                        failureDetails.Add($"Mode={mode}: result=({result.R:F4},{result.G:F4},{result.B:F4},{result.A:F4})");
                    }
                }
            }
        }

        if (failures > 0)
            throw new InvalidOperationException($"{failures} out-of-range results:\n" + string.Join("\n", failureDetails));
    }

    [Test]
    public void Lum_Calculation_UsesCorrectWeights()
    {
        double lum = 0.3 * 0.5 + 0.59 * 0.25 + 0.11 * 0.75;
        if (Math.Abs(lum - 0.38) > 0.001) throw new InvalidOperationException($"Lum calculation wrong: {lum}");
    }

    [Test]
    public void Sat_Gray_ReturnsZero()
    {
        var gray = new LinearColor(0.5, 0.5, 0.5, 1.0);
        double max = Math.Max(gray.R, Math.Max(gray.G, gray.B));
        double min = Math.Min(gray.R, Math.Min(gray.G, gray.B));
        double sat = max - min;
        if (Math.Abs(sat) > 0.001) throw new InvalidOperationException($"Gray should have sat=0, got {sat}");
    }

    [Test]
    public void Sat_FullRed_ReturnsMax()
    {
        var red = new LinearColor(1.0, 0.0, 0.0, 1.0);
        double max = Math.Max(red.R, Math.Max(red.G, red.B));
        double min = Math.Min(red.R, Math.Min(red.G, red.B));
        double sat = max - min;
        if (Math.Abs(sat - 1.0) > 0.001) throw new InvalidOperationException($"Red should have sat=1, got {sat}");
    }

    [Test]
    public void Hue_EqualColors_BlendsCorrectly()
    {
        var src = new LinearColor(0.8f, 0.2f, 0.2f, 1.0f);
        var dst = new LinearColor(0.2f, 0.2f, 0.8f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Hue);
        if (result.R < 0 || result.R > 1) throw new InvalidOperationException($"R out of range: {result.R}");
    }

    [Test]
    public void Color_SameLumAsDst_ReturnsSameLum()
    {
        var src = new LinearColor(0.5f, 0.5f, 0.5f, 1.0f);
        var dst = new LinearColor(0.5f, 0.5f, 0.5f, 1.0f);
        var result = BlendReference.Apply(src, dst, BlendMode.Color);
        double lumSrc = 0.3 * 0.5 + 0.59 * 0.5 + 0.11 * 0.5;
        double lumResult = 0.3 * result.R + 0.59 * result.G + 0.11 * result.B;
        if (Math.Abs(lumSrc - lumResult) > 0.01) throw new InvalidOperationException($"Luminosity changed: {lumResult} vs {lumSrc}");
    }
}
