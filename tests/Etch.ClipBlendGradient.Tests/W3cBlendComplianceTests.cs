using System;
using System.Collections.Generic;
using Etch.ClipBlendGradient;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public class W3cBlendComplianceTests
{
    private static readonly (BlendMode Mode, double SR, double SG, double SB, double SA, double DR, double DG, double DB, double DA, double ER, double EG, double EB, double EA)[] Vectors = new[]
    {
        // Normal blend - tests src-over compositing
        (BlendMode.Normal, 1.0, 0.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0),
        (BlendMode.Normal, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 1.0, 0.5, 0.5, 0.5, 1.0),

        // Multiply blend
        (BlendMode.Multiply, 1.0, 0.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0),
        (BlendMode.Multiply, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0, 1.0, 0.0, 0.0, 1.0),
        (BlendMode.Multiply, 0.5, 0.5, 0.5, 1.0, 0.5, 0.5, 0.5, 1.0, 0.25, 0.25, 0.25, 1.0),

        // Screen blend
        (BlendMode.Screen, 1.0, 0.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0, 1.0, 1.0, 0.0, 1.0),
        (BlendMode.Screen, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0),
        (BlendMode.Screen, 0.5, 0.5, 0.5, 1.0, 0.5, 0.5, 0.5, 1.0, 0.75, 0.75, 0.75, 1.0),

        // Darken blend
        (BlendMode.Darken, 1.0, 0.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0),
        (BlendMode.Darken, 0.5, 0.5, 0.5, 1.0, 0.75, 0.25, 0.5, 1.0, 0.5, 0.25, 0.5, 1.0),

        // Lighten blend
        (BlendMode.Lighten, 1.0, 0.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0, 1.0, 1.0, 0.0, 1.0),
        (BlendMode.Lighten, 0.5, 0.5, 0.5, 1.0, 0.75, 0.25, 0.5, 1.0, 0.75, 0.5, 0.5, 1.0),

        // Difference blend
        (BlendMode.Difference, 1.0, 0.0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0, 1.0, 1.0, 0.0, 1.0),
        (BlendMode.Difference, 0.5, 0.5, 0.5, 1.0, 0.5, 0.5, 0.5, 1.0, 0.0, 0.0, 0.0, 1.0),

        // Exclusion blend - same as Difference formula but different behavior
        (BlendMode.Exclusion, 0.5, 0.5, 0.5, 1.0, 0.5, 0.5, 0.5, 1.0, 0.5, 0.5, 0.5, 1.0),

        // Non-separable blend modes — W3C Compositing §9.17 (Lum/Sat/SetLum/SetSat/ClipColor)
        // Hue: SetLum(SetSat(Cs, Sat(Cb)), Lum(Cb))
        (BlendMode.Hue, 0.5, 0.1, 0.9, 1.0, 0.9, 0.1, 0.1, 1.0, 0.5320, 0.1320, 0.9320, 1.0),
        // Saturation: SetLum(SetSat(Cb, Sat(Cs)), Lum(Cb))
        (BlendMode.Saturation, 0.5, 0.9, 0.1, 1.0, 0.9, 0.1, 0.1, 1.0, 0.9, 0.1, 0.1, 1.0),
        // Color: SetLum(Cs, Lum(Cb))
        (BlendMode.Color, 0.5, 0.9, 0.1, 1.0, 0.9, 0.1, 0.1, 1.0, 0.2297, 0.4595, 0.0000, 1.0),
        // Luminosity: SetLum(Cb, Lum(Cs))
        (BlendMode.Luminosity, 0.2, 0.1, 0.9, 1.0, 0.9, 0.1, 0.1, 1.0, 0.7267, 0.0000, 0.0000, 1.0),
    };

    [Test]
    public void BlendModeNormal_IdentityOnOpaqueBlack()
    {
        var src = new LinearColor(1.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.0, 0.0, 0.0, 0.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Normal);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeMultiply_WhiteSrcPreservesDst()
    {
        var src = new LinearColor(1.0, 1.0, 1.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Multiply);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.25) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeMultiply_BlackSrcProducesBlack()
    {
        var src = new LinearColor(0.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Multiply);
        if (Math.Abs(result.R - 0.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeScreen_BlackSrcPreservesDst()
    {
        var src = new LinearColor(0.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Screen);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.25) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeScreen_WhiteSrcProducesWhite()
    {
        var src = new LinearColor(1.0, 1.0, 1.0, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Screen);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 1.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 1.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeDifference_SameColorProducesBlack()
    {
        var src = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var dst = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Difference);
        if (Math.Abs(result.R - 0.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeDifference_ComplementaryColors()
    {
        var src = new LinearColor(1.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.0, 1.0, 0.0, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Difference);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 1.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeExclusion_ComplementaryColors()
    {
        var src = new LinearColor(1.0, 0.0, 0.0, 1.0);
        var dst = new LinearColor(0.0, 1.0, 0.0, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Exclusion);
        if (Math.Abs(result.R - 1.0) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 1.0) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.0) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeDarken_TakesMinimumOfEachChannel()
    {
        var src = new LinearColor(0.5, 0.75, 0.25, 1.0);
        var dst = new LinearColor(0.25, 0.5, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Darken);
        if (Math.Abs(result.R - 0.25) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.5) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.25) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendModeLighten_TakesMaximumOfEachChannel()
    {
        var src = new LinearColor(0.5, 0.75, 0.25, 1.0);
        var dst = new LinearColor(0.25, 0.5, 0.75, 1.0);
        var result = BlendReference.Apply(src, dst, BlendMode.Lighten);
        if (Math.Abs(result.R - 0.5) > 0.001) throw new InvalidOperationException($"R: {result.R}");
        if (Math.Abs(result.G - 0.75) > 0.001) throw new InvalidOperationException($"G: {result.G}");
        if (Math.Abs(result.B - 0.75) > 0.001) throw new InvalidOperationException($"B: {result.B}");
    }

    [Test]
    public void BlendReferenceApply_IsPure()
    {
        var src = new LinearColor(0.5, 0.25, 0.75, 1.0);
        var dst = new LinearColor(0.25, 0.5, 0.75, 1.0);

        var result1 = BlendReference.Apply(src, dst, BlendMode.Multiply);
        var result2 = BlendReference.Apply(src, dst, BlendMode.Multiply);

        if (Math.Abs(result1.R - result2.R) > 0.0001) throw new InvalidOperationException($"R: {result1.R} vs {result2.R}");
        if (Math.Abs(result1.G - result2.G) > 0.0001) throw new InvalidOperationException($"G: {result1.G} vs {result2.G}");
        if (Math.Abs(result1.B - result2.B) > 0.0001) throw new InvalidOperationException($"B: {result1.B} vs {result2.B}");
        if (Math.Abs(result1.A - result2.A) > 0.0001) throw new InvalidOperationException($"A: {result1.A} vs {result2.A}");
    }

    [Test]
    public void AllW3cVectors_PassToTolerance()
    {
        double tolerance = 0.01;
        int failedCount = 0;
        var failures = new List<string>();

        foreach (var v in Vectors)
        {
            var src = new LinearColor(v.SR, v.SG, v.SB, v.SA);
            var dst = new LinearColor(v.DR, v.DG, v.DB, v.DA);
            var expected = new LinearColor(v.ER, v.EG, v.EB, v.EA);

            var result = BlendReference.Apply(src, dst, v.Mode);

            double rDiff = Math.Abs(result.R - expected.R);
            double gDiff = Math.Abs(result.G - expected.G);
            double bDiff = Math.Abs(result.B - expected.B);
            double aDiff = Math.Abs(result.A - expected.A);

            if (rDiff > tolerance || gDiff > tolerance || bDiff > tolerance || aDiff > tolerance)
            {
                failedCount++;
                if (failures.Count < 5)
                {
                    failures.Add($"Mode={v.Mode}: expected=({expected.R:F4},{expected.G:F4},{expected.B:F4},{expected.A:F4}) got=({result.R:F4},{result.G:F4},{result.B:F4},{result.A:F4})");
                }
            }
        }

        if (failedCount > 0)
        {
            throw new InvalidOperationException($"Failed {failedCount}/{Vectors.Length} vectors:\n" + string.Join("\n", failures));
        }
    }
}
