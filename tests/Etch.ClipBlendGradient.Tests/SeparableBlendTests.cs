using System;
using Etch.ClipBlendGradient;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class SeparableBlendTests
{
    [Test]
    public void Multiply_WhiteSrc_ReturnsDst()
    {
        var src = new LinearColor(1.0, 1.0, 1.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Multiply);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.25) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Multiply_BlackSrc_ReturnsBlack()
    {
        var src = new LinearColor(0.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Multiply);
        if (Math.Abs(result.R - 0.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Multiply_50Percent_ReturnsHalf()
    {
        var src = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var dst = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Multiply);
        if (Math.Abs(result.R - 0.25) > 0.001) throw new InvalidOperationException($"R: {result.R}");
    }

    [Test]
    public void Screen_BlackSrc_ReturnsDst()
    {
        var src = new LinearColor(0.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Screen);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.25) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Screen_WhiteSrc_ReturnsWhite()
    {
        var src = new LinearColor(1.0, 1.0, 1.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Screen);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 1.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 1.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Darken_TakesMinimum()
    {
        var src = new LinearColor(0.5, 0.75, 0.25, 1.0);
        var dst = new LinearColor(0.25, 0.5, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Darken);
        if (Math.Abs(result.R - 0.25) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.5) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.25) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Lighten_TakesMaximum()
    {
        var src = new LinearColor(0.5, 0.75, 0.25, 1.0);
        var dst = new LinearColor(0.25, 0.5, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Lighten);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.75) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Difference_SameColor_ReturnsBlack()
    {
        var src = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Difference);
        if (Math.Abs(result.R - 0.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Difference_Complementary_ReturnsWhite()
    {
        var src = new LinearColor(1.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.0, 1.0, 0.0, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Difference);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 1.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void Exclusion_Complementary_ReturnsMax()
    {
        var src = new LinearColor(1.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.0, 1.0, 0.0, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Exclusion);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 1.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
    }

    [Test]
    public void Exclusion_SameColor_ReturnsCorrect()
    {
        var src = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var dst = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Exclusion);
        double expected = 0.5 + 0.5 - 2 * 0.5 * 0.5;
        if (Math.Abs(result.R - expected) > 0.001) throw new InvalidOperationException($"R: {result.R} expected {expected}");
    }

    [Test]
    public void ColorDodge_DivisionByZero_ReturnsWhite()
    {
        var src = new LinearColor(1.0, 1.0, 1.0, 1.0);
        var dst = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.ColorDodge);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 1.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 1.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void ColorDodge_SrcZero_ReturnsDstUnchanged()
    {
        var src = new LinearColor(0.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.ColorDodge);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.25) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void ColorBurn_DivisionByZero_ReturnsBlack()
    {
        var src = new LinearColor(0.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.ColorBurn);
        if (Math.Abs(result.R - 0.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void ColorBurn_SrcOne_ReturnsDstUnchanged()
    {
        var src = new LinearColor(1.0, 1.0, 1.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.ColorBurn);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.25) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void HardLight_SrcLtHalf_UsesMultiply()
    {
        var src = new LinearColor(0.25, 0.25, 0.25, 1.0);
        var dst = new LinearColor(0.8, 0.2, 0.2, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.HardLight);
        double expectedR = 2.0 * 0.25 * 0.8;
        if (Math.Abs(result.R - expectedR) > 0.001) throw new InvalidOperationException($"R: {result.R} expected {expectedR}");
    }

    [Test]
    public void HardLight_SrcGeHalf_UsesScreen()
    {
        var src = new LinearColor(0.75, 0.75, 0.75, 1.0);
        var dst = new LinearColor(0.2, 0.8, 0.8, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.HardLight);
        double expectedR = 1.0 - 2.0 * (1.0 - 0.75) * (1.0 - 0.2);
        if (Math.Abs(result.R - expectedR) > 0.001) throw new InvalidOperationException($"R: {result.R} expected {expectedR}");
    }

    [Test]
    public void SoftLight_DstNearZero_NoNaNOrInf()
    {
        var src = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var dst = new LinearColor(0.001, 0.001, 0.001, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.SoftLight);
        if (double.IsNaN(result.R) || double.IsInfinity(result.R)) throw new InvalidOperationException($"R is NaN or Inf");
        if (double.IsNaN(result.G) || double.IsInfinity(result.G)) throw new InvalidOperationException($"G is NaN or Inf");
        if (double.IsNaN(result.B) || double.IsInfinity(result.B)) throw new InvalidOperationException($"B is NaN or Inf");
        if (result.R < 0 || result.R > 1) throw new InvalidOperationException($"R out of range: {result.R}");
    }

    [Test]
    public void SoftLight_DstNearOne_NoNaNOrInf()
    {
        var src = new LinearColor(0.5, 0.5, 0.5, 1.0);
        var dst = new LinearColor(0.999, 0.999, 0.999, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.SoftLight);
        if (double.IsNaN(result.R) || double.IsInfinity(result.R)) throw new InvalidOperationException($"R is NaN or Inf");
        if (double.IsNaN(result.G) || double.IsInfinity(result.G)) throw new InvalidOperationException($"G is NaN or Inf");
        if (double.IsNaN(result.B) || double.IsInfinity(result.B)) throw new InvalidOperationException($"B is NaN or Inf");
    }

    [Test]
    public void Overlay_FormulaMatchesW3C()
    {
        var src = new LinearColor(1.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.0, 0.0, 0.0, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Overlay);
        if (Math.Abs(result.R - 0.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
    }

    [Test]
    public void AllSeparableModes_100RandomPairs_AllInRange()
    {
        var rng = new Random(42);
        var modes = new BlendMode[]
        {
            BlendMode.Multiply, BlendMode.Screen, BlendMode.Overlay,
            BlendMode.Darken, BlendMode.Lighten, BlendMode.ColorDodge,
            BlendMode.ColorBurn, BlendMode.HardLight, BlendMode.SoftLight,
            BlendMode.Difference, BlendMode.Exclusion
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
                        failureDetails.Add($"Mode={mode}: src=({srcR:F2},{srcG:F2},{srcB:F2}) dst=({dstR:F2},{dstG:F2},{dstB:F2}) result=({result.R:F4},{result.G:F4},{result.B:F4},{result.A:F4})");
                    }
                }
            }
        }

        if (failures > 0)
            throw new InvalidOperationException($"{failures} out-of-range results:\n" + string.Join("\n", failureDetails));
    }
}
