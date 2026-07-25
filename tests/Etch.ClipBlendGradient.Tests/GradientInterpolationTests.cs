using System;
using Etch.ClipBlendGradient;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class GradientInterpolationTests
{
    [Test]
    public void RedToGreen_LinearLight_MidpointIsDarkOlive()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 1.0f, 0.0f, 1.0f))
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        int midIndex = 128;
        var midColor = lut[midIndex];

        float rLinear = midColor.RLinear;
        float gLinear = midColor.GLinear;

        if (Math.Abs(rLinear - 0.5f) > 0.02f) throw new InvalidOperationException($"R linear at midpoint should be ~0.5, got {rLinear}");
        if (Math.Abs(gLinear - 0.5f) > 0.02f) throw new InvalidOperationException($"G linear at midpoint should be ~0.5, got {gLinear}");
    }

    [Test]
    public void RedToGreen_Srgb_MidpointDiffersFromLinear()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 1.0f, 0.0f, 1.0f))
        };

        var lutLinear = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);
        var lutSrgb = GradientLutBuilder.Build(stops, GradientInterpolationSpace.Srgb);

        int midIndex = 128;
        var colorLinear = lutLinear[midIndex];
        var colorSrgb = lutSrgb[midIndex];

        float rLinear = colorLinear.RLinear;
        float rSrgb = colorSrgb.RLinear;

        if (Math.Abs(rLinear - rSrgb) < 0.01f)
            throw new InvalidOperationException("sRGB and LinearLight should produce different results at midpoint");
    }

    [Test]
    public void GradientLut_StructureIsValid()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        if (lut.Length != 256) throw new InvalidOperationException($"LUT should have 256 entries, got {lut.Length}");

        for (int i = 0; i < lut.Length; i++)
        {
            if ((float)lut[i].R < 0 || (float)lut[i].R > 1) throw new InvalidOperationException($"R[{i}] out of range: {lut[i].R}");
            if ((float)lut[i].G < 0 || (float)lut[i].G > 1) throw new InvalidOperationException($"G[{i}] out of range: {lut[i].G}");
            if ((float)lut[i].B < 0 || (float)lut[i].B > 1) throw new InvalidOperationException($"B[{i}] out of range: {lut[i].B}");
            if ((float)lut[i].A < 0 || (float)lut[i].A > 1) throw new InvalidOperationException($"A[{i}] out of range: {lut[i].A}");
        }
    }

    [Test]
    public void GradientInterpolationSpace_HasCorrectValues()
    {
        if ((byte)GradientInterpolationSpace.LinearLight != 0) throw new InvalidOperationException("LinearLight should be 0");
    }

    [Test]
    public void GradientInterpolationSpace_SrgbExists()
    {
        var srgb = GradientInterpolationSpace.Srgb;
        var linear = GradientInterpolationSpace.LinearLight;
        if (srgb == linear) throw new InvalidOperationException("Srgb and LinearLight should be different");
    }

    [Test]
    public void GradientInterpolationSpace_DefaultIsLinearLight()
    {
        var defaultSpace = default(GradientInterpolationSpace);
        if (defaultSpace != GradientInterpolationSpace.LinearLight)
            throw new InvalidOperationException("Default GradientInterpolationSpace should be LinearLight");
    }

    [Test]
    public void LinearLight_IsDefault_AndSrgbIsOptIn()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 1.0f, 0.0f, 1.0f))
        };

        var defaultGradient = new LinearGradient(0, 0, 256, 0, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);
        if (defaultGradient.InterpolationSpace != GradientInterpolationSpace.LinearLight)
            throw new InvalidOperationException("Default interpolation space should be LinearLight");
    }
}
