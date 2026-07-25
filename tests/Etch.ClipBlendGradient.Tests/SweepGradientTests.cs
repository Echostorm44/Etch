using System;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class SweepGradientTests
{
    [Test]
    public void Constructor_StoresValues()
    {
        var center = new Vec2(100, 100);
        float startAngle = 0.0f;
        float endAngle = MathF.PI;
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new SweepGradient(center, startAngle, endAngle, stops, GradientInterpolationSpace.LinearLight);

        if (gradient.Center.X != 100 || gradient.Center.Y != 100) throw new InvalidOperationException("Center mismatch");
        if (Math.Abs(gradient.StartAngleRad - 0.0f) > 0.001f) throw new InvalidOperationException("StartAngleRad mismatch");
        if (Math.Abs(gradient.EndAngleRad - MathF.PI) > 0.001f) throw new InvalidOperationException("EndAngleRad mismatch");
        if (gradient.Stops.Length != 2) throw new InvalidOperationException("Stops length mismatch");
        if (gradient.InterpolationSpace != GradientInterpolationSpace.LinearLight) throw new InvalidOperationException("InterpolationSpace mismatch");
    }

    [Test]
    public void Constructor_WithDoubleArgs_StoresValues()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new SweepGradient(100, 100, 0.0f, MathF.PI / 2.0f, stops, GradientInterpolationSpace.Srgb);

        if (gradient.Center.X != 100 || gradient.Center.Y != 100) throw new InvalidOperationException("Center mismatch");
        if (Math.Abs(gradient.StartAngleRad - 0.0f) > 0.001f) throw new InvalidOperationException("StartAngleRad mismatch");
        if (Math.Abs(gradient.EndAngleRad - MathF.PI / 2.0f) > 0.001f) throw new InvalidOperationException("EndAngleRad mismatch");
    }

    [Test]
    public void Constructor_EqualAngles_ThrowsPanic()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        try
        {
            var gradient = new SweepGradient(new Vec2(100, 100), 1.0f, 1.0f, stops, GradientInterpolationSpace.LinearLight);
            throw new InvalidOperationException("Expected panic for equal angles");
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.DegenerateSweepGradient)
        {
        }
    }

    [Test]
    public void Constructor_EndAngleLessThanStart_ThrowsPanic()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        try
        {
            var gradient = new SweepGradient(new Vec2(100, 100), 1.0f, 0.5f, stops, GradientInterpolationSpace.LinearLight);
            throw new InvalidOperationException("Expected panic when endAngle < startAngle");
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.DegenerateSweepGradient)
        {
        }
    }

    [Test]
    public void GradientLut_SrgbVsLinearLight_ProduceDifferentResults()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new SweepGradient(128, 128, 0.0f, MathF.PI, stops, GradientInterpolationSpace.LinearLight);

        var lutLinear = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);
        var lutSrgb = GradientLutBuilder.Build(stops, GradientInterpolationSpace.Srgb);

        int lutIndex = 128;
        var colorLinear = lutLinear[lutIndex];
        var colorSrgb = lutSrgb[lutIndex];

        if (Math.Abs(colorLinear.RLinear - colorSrgb.RLinear) < 0.001f)
            throw new InvalidOperationException("sRGB and linear should produce different results");
    }

    [Test]
    public void RangeIsValid_PositiveDifference()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new SweepGradient(128, 128, 0.0f, MathF.PI / 2.0f, stops, GradientInterpolationSpace.LinearLight);

        float range = gradient.EndAngleRad - gradient.StartAngleRad;
        if (range <= 0) throw new InvalidOperationException("Range should be positive");
    }
}
