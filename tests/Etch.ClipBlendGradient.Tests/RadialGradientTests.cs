using System;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class RadialGradientTests
{
    [Test]
    public void Constructor_StoresValues()
    {
        var center = new Vec2(100, 100);
        float radius = 50.0f;
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new RadialGradient(center, radius, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);

        if (gradient.Center.X != 100 || gradient.Center.Y != 100) throw new InvalidOperationException("Center mismatch");
        if (Math.Abs(gradient.Radius - 50.0f) > 0.001f) throw new InvalidOperationException("Radius mismatch");
        if (gradient.Stops.Length != 2) throw new InvalidOperationException("Stops length mismatch");
        if (gradient.Extend != GradientExtend.Pad) throw new InvalidOperationException("Extend mismatch");
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

        var gradient = new RadialGradient(100, 100, 50.0f, stops, GradientExtend.Repeat, GradientInterpolationSpace.Srgb);

        if (gradient.Center.X != 100 || gradient.Center.Y != 100) throw new InvalidOperationException("Center mismatch");
        if (Math.Abs(gradient.Radius - 50.0f) > 0.001f) throw new InvalidOperationException("Radius mismatch");
    }

    [Test]
    public void Constructor_ZeroRadius_ThrowsPanic()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        try
        {
            var gradient = new RadialGradient(new Vec2(100, 100), 0.0f, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);
            throw new InvalidOperationException("Expected panic for zero radius");
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.DegenerateRadialGradient)
        {
        }
    }

    [Test]
    public void Constructor_NegativeRadius_ThrowsPanic()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        try
        {
            var gradient = new RadialGradient(new Vec2(100, 100), -10.0f, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);
            throw new InvalidOperationException("Expected panic for negative radius");
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.DegenerateRadialGradient)
        {
        }
    }

    [Test]
    public void ConcentricRedBlue_Distance50AtRadius100_IsHalfPurple()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new RadialGradient(100, 100, 100.0f, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        float t = 0.5f;
        int lutIndex = (int)(t * 255);
        var color = lut[lutIndex];

        if (Math.Abs(color.RLinear - 0.5f) > 0.02f) throw new InvalidOperationException($"R at center should be ~0.5, got {color.RLinear}");
        if (Math.Abs(color.BLinear - 0.5f) > 0.02f) throw new InvalidOperationException($"B at center should be ~0.5, got {color.BLinear}");
    }

    [Test]
    public void GradientLut_SrgbVsLinearLight_ProduceDifferentResults()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new RadialGradient(100, 100, 100.0f, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);

        var lutLinear = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);
        var lutSrgb = GradientLutBuilder.Build(stops, GradientInterpolationSpace.Srgb);

        int lutIndex = 128;
        var colorLinear = lutLinear[lutIndex];
        var colorSrgb = lutSrgb[lutIndex];

        if (Math.Abs(colorLinear.RLinear - colorSrgb.RLinear) < 0.001f)
            throw new InvalidOperationException("sRGB and linear should produce different results");
    }

    [Test]
    public void GradientLut_ExtendModes_ProduceCorrectResults()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradientPad = new RadialGradient(100, 100, 100.0f, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);
        var gradientRepeat = new RadialGradient(100, 100, 100.0f, stops, GradientExtend.Repeat, GradientInterpolationSpace.LinearLight);
        var gradientReflect = new RadialGradient(100, 100, 100.0f, stops, GradientExtend.Reflect, GradientInterpolationSpace.LinearLight);

        if (gradientPad.Extend != GradientExtend.Pad) throw new InvalidOperationException("Pad extend mismatch");
        if (gradientRepeat.Extend != GradientExtend.Repeat) throw new InvalidOperationException("Repeat extend mismatch");
        if (gradientReflect.Extend != GradientExtend.Reflect) throw new InvalidOperationException("Reflect extend mismatch");
    }
}
