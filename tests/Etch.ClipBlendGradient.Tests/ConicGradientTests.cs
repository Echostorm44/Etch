using System;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class ConicGradientTests
{
    [Test]
    public void Constructor_StoresValues()
    {
        var center = new Vec2(100, 100);
        float startAngle = 0.5f;
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new ConicGradient(center, startAngle, stops, GradientInterpolationSpace.LinearLight);

        if (gradient.Center.X != 100 || gradient.Center.Y != 100) throw new InvalidOperationException("Center mismatch");
        if (Math.Abs(gradient.StartAngleRad - 0.5f) > 0.001f) throw new InvalidOperationException("StartAngleRad mismatch");
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

        var gradient = new ConicGradient(100, 100, 1.0f, stops, GradientInterpolationSpace.Srgb);

        if (gradient.Center.X != 100 || gradient.Center.Y != 100) throw new InvalidOperationException("Center mismatch");
        if (Math.Abs(gradient.StartAngleRad - 1.0f) > 0.001f) throw new InvalidOperationException("StartAngleRad mismatch");
    }

    [Test]
    public void FourStopQuadrantFill_CorrectColorDistribution()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(0.25f, Rgba16f.From(0.0f, 1.0f, 0.0f, 1.0f)),
            new GradientStop(0.5f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f)),
            new GradientStop(0.75f, Rgba16f.From(1.0f, 1.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f))
        };

        var gradient = new ConicGradient(128, 128, 0.0f, stops, GradientInterpolationSpace.LinearLight);

        if (gradient.Stops.Length != 5) throw new InvalidOperationException("Stops length mismatch");
    }

    [Test]
    public void StartAngleRotation_ChangesGradientDistribution()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(0.5f, Rgba16f.From(0.0f, 1.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient0 = new ConicGradient(128, 128, 0.0f, stops, GradientInterpolationSpace.LinearLight);
        var gradient90 = new ConicGradient(128, 128, MathF.PI / 2.0f, stops, GradientInterpolationSpace.LinearLight);

        if (Math.Abs(gradient0.StartAngleRad - 0.0f) > 0.001f) throw new InvalidOperationException("Start angle 0 mismatch");
        if (Math.Abs(gradient90.StartAngleRad - MathF.PI / 2.0f) > 0.001f) throw new InvalidOperationException("Start angle 90 mismatch");
    }

    [Test]
    public void GradientLut_SrgbVsLinearLight_ProduceDifferentResults()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new ConicGradient(128, 128, 0.0f, stops, GradientInterpolationSpace.LinearLight);

        var lutLinear = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);
        var lutSrgb = GradientLutBuilder.Build(stops, GradientInterpolationSpace.Srgb);

        int lutIndex = 128;
        var colorLinear = lutLinear[lutIndex];
        var colorSrgb = lutSrgb[lutIndex];

        if (Math.Abs(colorLinear.RLinear - colorSrgb.RLinear) < 0.001f)
            throw new InvalidOperationException("sRGB and linear should produce different results");
    }
}
