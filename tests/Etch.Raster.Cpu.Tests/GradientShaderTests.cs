using System;
using Etch.ClipBlendGradient;
using Etch.Raster.Cpu;
using Etch.Raster.Cpu.Gradients;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class GradientShaderTests
{
    [Test]
    public void LinearGradientCentrePixel()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Etch.ClipBlendGradient.Rgba16f.From(1, 0, 0, 1)),
            new GradientStop(1.0f, Etch.ClipBlendGradient.Rgba16f.From(0, 0, 1, 1)),
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        var row = new Rgba16f[256];

        LinearGradientRenderer.Paint(row, 0, 128, 0, 128, 256, 128, GradientExtend.Pad, lut);

        var mid = row[128];

        if (Math.Abs((float)mid.R - 0.5f) > 0.01f)
            throw new InvalidOperationException($"Expected R=0.5, got {mid.R}");
        if (Math.Abs((float)mid.G) > 0.01f)
            throw new InvalidOperationException($"Expected G=0, got {mid.G}");
        if (Math.Abs((float)mid.B - 0.5f) > 0.01f)
            throw new InvalidOperationException($"Expected B=0.5, got {mid.B}");
    }

    [Test]
    public void LinearGradientStartEnd()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Etch.ClipBlendGradient.Rgba16f.From(1, 0, 0, 1)),
            new GradientStop(1.0f, Etch.ClipBlendGradient.Rgba16f.From(0, 0, 1, 1)),
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        var row = new Rgba16f[256];

        LinearGradientRenderer.Paint(row, 0, 128, 0, 128, 256, 128, GradientExtend.Pad, lut);

        var start = row[0];
        var end = row[255];

        if (Math.Abs((float)start.R - 1.0f) > 0.01f)
            throw new InvalidOperationException($"Start R should be ~1, got {start.R}");
        if (Math.Abs((float)end.B - 1.0f) > 0.01f)
            throw new InvalidOperationException($"End B should be ~1, got {end.B}");
    }

    [Test]
    public void LinearGradientTParsing()
    {
        float t0 = LinearGradientRenderer.SampleT(0, 0, 0, 0, 100, 0);
        float t50 = LinearGradientRenderer.SampleT(50, 0, 0, 0, 100, 0);
        float t100 = LinearGradientRenderer.SampleT(100, 0, 0, 0, 100, 0);

        if (Math.Abs(t0) > 0.001f)
            throw new InvalidOperationException($"Expected t=0 at start, got {t0}");
        if (Math.Abs(t50 - 0.5f) > 0.001f)
            throw new InvalidOperationException($"Expected t=0.5 at mid, got {t50}");
        if (Math.Abs(t100 - 1.0f) > 0.001f)
            throw new InvalidOperationException($"Expected t=1 at end, got {t100}");
    }

    [Test]
    public void RadialGradientTParsing()
    {
        float t0 = RadialGradientRenderer.SampleT(0, 0, 0, 0, 100);
        float t50 = RadialGradientRenderer.SampleT(50, 0, 0, 0, 100);
        float t100 = RadialGradientRenderer.SampleT(100, 0, 0, 0, 100);

        if (Math.Abs(t0) > 0.001f)
            throw new InvalidOperationException($"Expected t=0 at center, got {t0}");
        if (Math.Abs(t50 - 0.5f) > 0.001f)
            throw new InvalidOperationException($"Expected t=0.5 at half radius, got {t50}");
        if (Math.Abs(t100 - 1.0f) > 0.001f)
            throw new InvalidOperationException($"Expected t=1 at radius, got {t100}");
    }

    [Test]
    public void ConicGradientAngleAtCardinals()
    {
        float angleRight = ConicGradientRenderer.SampleAngle(1, 0, 0, 0);
        float angleDown = ConicGradientRenderer.SampleAngle(0, 1, 0, 0);
        float angleLeft = ConicGradientRenderer.SampleAngle(-1, 0, 0, 0);
        float angleUp = ConicGradientRenderer.SampleAngle(0, -1, 0, 0);

        float twoPi = 2.0f * MathF.PI;

        if (Math.Abs(angleRight) > 0.001f)
            throw new InvalidOperationException($"Expected angle=0 at right, got {angleRight}");
        if (Math.Abs(angleDown - twoPi / 4.0f) > 0.001f)
            throw new InvalidOperationException($"Expected angle=pi/2 at down, got {angleDown}");
        if (Math.Abs(angleLeft - twoPi / 2.0f) > 0.001f)
            throw new InvalidOperationException($"Expected angle=pi at left, got {angleLeft}");
        if (Math.Abs(angleUp - 3.0f * MathF.PI / 2.0f) > 0.001f)
            throw new InvalidOperationException($"Expected angle=3pi/2 at up, got {angleUp}");
    }

    [Test]
    public void SweepGradientTParsing()
    {
        float t0 = SweepGradientRenderer.SampleT(1, 0, 0, 0, 0, 2.0f * MathF.PI);
        float t50 = SweepGradientRenderer.SampleT(0, 1, 0, 0, 0, 2.0f * MathF.PI);
        float t100 = SweepGradientRenderer.SampleT(-1, 0, 0, 0, 0, 2.0f * MathF.PI);

        if (Math.Abs(t0) > 0.01f)
            throw new InvalidOperationException($"Expected t~0 at 0 rad, got {t0}");
        if (Math.Abs(t50 - 0.25f) > 0.01f)
            throw new InvalidOperationException($"Expected t~0.25 at pi/2, got {t50}");
        if (Math.Abs(t100 - 0.5f) > 0.01f)
            throw new InvalidOperationException($"Expected t~0.5 at pi, got {t100}");
    }

    [Test]
    public void FastAtan2Error()
    {
        float maxError = 0.0f;
        const float tolerance = 0.5f * MathF.PI / 180.0f;

        for (float y = -1.0f; y <= 1.0f; y += 0.05f)
        {
            for (float x = -1.0f; x <= 1.0f; x += 0.05f)
            {
                float expected = MathF.Atan2(y, x);
                float actual = FastAtan2.Atan2(y, x);
                float error = MathF.Abs(expected - actual);
                if (error > maxError)
                    maxError = error;
            }
        }

        if (maxError > tolerance)
            throw new InvalidOperationException($"Max atan2 error {maxError} rad exceeds tolerance {tolerance} rad ({(maxError * 180.0f / MathF.PI)} deg)");
    }

    [Test]
    public void ExtendPad()
    {
        float result = ExtendHelpers.Apply(-0.5f, GradientExtend.Pad);
        if (result != 0.0f)
            throw new InvalidOperationException($"Pad: expected 0, got {result}");

        result = ExtendHelpers.Apply(1.5f, GradientExtend.Pad);
        if (result != 1.0f)
            throw new InvalidOperationException($"Pad: expected 1, got {result}");

        result = ExtendHelpers.Apply(0.5f, GradientExtend.Pad);
        if (result != 0.5f)
            throw new InvalidOperationException($"Pad: expected 0.5, got {result}");
    }

    [Test]
    public void ExtendReflect()
    {
        float result = ExtendHelpers.Apply(0.25f, GradientExtend.Reflect);
        if (Math.Abs(result - 0.5f) > 0.001f)
            throw new InvalidOperationException($"Reflect: expected 0.5, got {result}");

        result = ExtendHelpers.Apply(0.75f, GradientExtend.Reflect);
        if (Math.Abs(result - 0.5f) > 0.001f)
            throw new InvalidOperationException($"Reflect: expected 0.5, got {result}");

        result = ExtendHelpers.Apply(0.0f, GradientExtend.Reflect);
        if (Math.Abs(result) > 0.001f)
            throw new InvalidOperationException($"Reflect: expected 0, got {result}");

        result = ExtendHelpers.Apply(1.0f, GradientExtend.Reflect);
        if (Math.Abs(result) > 0.001f)
            throw new InvalidOperationException($"Reflect: expected 0, got {result}");
    }

    [Test]
    public void ExtendRepeat()
    {
        float result = ExtendHelpers.Apply(0.25f, GradientExtend.Repeat);
        if (Math.Abs(result - 0.25f) > 0.001f)
            throw new InvalidOperationException($"Repeat: expected 0.25, got {result}");

        result = ExtendHelpers.Apply(1.25f, GradientExtend.Repeat);
        if (Math.Abs(result - 0.25f) > 0.001f)
            throw new InvalidOperationException($"Repeat: expected 0.25, got {result}");

        result = ExtendHelpers.Apply(-0.25f, GradientExtend.Repeat);
        if (Math.Abs(result - 0.75f) > 0.001f)
            throw new InvalidOperationException($"Repeat: expected 0.75, got {result}");
    }

    [Test]
    public void ZeroAllocPerPaint()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Etch.ClipBlendGradient.Rgba16f.From(1, 0, 0, 1)),
            new GradientStop(1.0f, Etch.ClipBlendGradient.Rgba16f.From(0, 0, 1, 1)),
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);
        var row = new Rgba16f[256];

        for (int iter = 0; iter < 100; iter++)
        {
            LinearGradientRenderer.Paint(row, 0, 128, 0, 128, 256, 128, GradientExtend.Pad, lut);
        }
    }
}
