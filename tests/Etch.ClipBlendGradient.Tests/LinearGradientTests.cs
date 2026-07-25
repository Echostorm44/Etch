using System;
using Etch.ClipBlendGradient;
using Etch.Geometry;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class LinearGradientTests
{
    [Test]
    public void Constructor_StoresValues()
    {
        var start = new Vec2(0, 0);
        var end = new Vec2(256, 0);
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new LinearGradient(start, end, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);

        if (gradient.Start.X != 0 || gradient.Start.Y != 0) throw new InvalidOperationException("Start mismatch");
        if (gradient.End.X != 256 || gradient.End.Y != 0) throw new InvalidOperationException("End mismatch");
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

        var gradient = new LinearGradient(0, 0, 256, 256, stops, GradientExtend.Repeat, GradientInterpolationSpace.Srgb);

        if (gradient.Start.X != 0 || gradient.Start.Y != 0) throw new InvalidOperationException("Start mismatch");
        if (gradient.End.X != 256 || gradient.End.Y != 256) throw new InvalidOperationException("End mismatch");
    }

    [Test]
    public void GradientLut_HorizontalRedBlue_Column128IsHalfPurple()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1.0f, 0.0f, 0.0f, 1.0f)),
            new GradientStop(1.0f, Rgba16f.From(0.0f, 0.0f, 1.0f, 1.0f))
        };

        var gradient = new LinearGradient(0, 0, 256, 0, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        int lutIndex = 128;
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

        var gradientPad = new LinearGradient(0, 0, 256, 0, stops, GradientExtend.Pad, GradientInterpolationSpace.LinearLight);
        var gradientRepeat = new LinearGradient(0, 0, 256, 0, stops, GradientExtend.Repeat, GradientInterpolationSpace.LinearLight);
        var gradientReflect = new LinearGradient(0, 0, 256, 0, stops, GradientExtend.Reflect, GradientInterpolationSpace.LinearLight);

        if (gradientPad.Extend != GradientExtend.Pad) throw new InvalidOperationException("Pad extend mismatch");
        if (gradientRepeat.Extend != GradientExtend.Repeat) throw new InvalidOperationException("Repeat extend mismatch");
        if (gradientReflect.Extend != GradientExtend.Reflect) throw new InvalidOperationException("Reflect extend mismatch");
    }

    [Test]
    public void GradientLut_DegenerateSingleStop_ReturnsConstantColor()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.5f, Rgba16f.From(0.25f, 0.5f, 0.75f, 1.0f))
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        for (int i = 0; i < lut.Length; i++)
        {
            if (Math.Abs(lut[i].RLinear - 0.25f) > 0.001f)
                throw new InvalidOperationException($"All entries should be constant 0.25, entry {i} has {lut[i].RLinear}");
        }
    }
}
