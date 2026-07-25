using System;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class GradientLutBuilderTests
{
    [Test]
    public void TwoStopsRedToBlueLutMidpoint()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1, 0, 0, 1)),
            new GradientStop(1.0f, Rgba16f.From(0, 0, 1, 1)),
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        var mid = lut[128];

        if (Math.Abs((float)mid.R - 0.5f) > 0.01f)
            throw new InvalidOperationException($"Expected R=0.5, got {mid.R}");
        if (Math.Abs((float)mid.G - 0.0f) > 0.01f)
            throw new InvalidOperationException($"Expected G=0, got {mid.G}");
        if (Math.Abs((float)mid.B - 0.5f) > 0.01f)
            throw new InvalidOperationException($"Expected B=0.5, got {mid.B}");
        if (Math.Abs((float)mid.A - 1.0f) > 0.01f)
            throw new InvalidOperationException($"Expected A=1, got {mid.A}");
    }

    [Test]
    public void UnsortedStopsAreSorted()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.8f, Rgba16f.From(0, 0, 1, 1)),
            new GradientStop(0.2f, Rgba16f.From(0, 1, 0, 1)),
            new GradientStop(0.5f, Rgba16f.From(1, 0, 0, 1)),
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        var mid = lut[128];

        if (Math.Abs((float)mid.R - 1.0f) > 0.01f)
            throw new InvalidOperationException($"Expected R=1 (red at 0.5), got {mid.R}");
    }

    [Test]
    public void DegenerateSingleStopProducesConstantLut()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.5f, Rgba16f.From(0.25f, 0.75f, 0.5f, 1.0f)),
        };

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        for (int i = 0; i < lut.Length; i++)
        {
            if (Math.Abs((float)lut[i].R - 0.25f) > 0.001f)
                throw new InvalidOperationException($"Expected R=0.25 at {i}, got {lut[i].R}");
            if (Math.Abs((float)lut[i].G - 0.75f) > 0.001f)
                throw new InvalidOperationException($"Expected G=0.75 at {i}, got {lut[i].G}");
        }
    }

    [Test]
    public void EmptyStopsProducesBlackLut()
    {
        var stops = Array.Empty<GradientStop>();

        var lut = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);

        for (int i = 0; i < lut.Length; i++)
        {
            if ((float)lut[i].R != 0f || (float)lut[i].G != 0f || (float)lut[i].B != 0f || (float)lut[i].A != 0f)
                throw new InvalidOperationException($"Expected black at {i}, got ({lut[i].R},{lut[i].G},{lut[i].B},{lut[i].A})");
        }
    }

    [Test]
    public void SrgbInterpolationDiffersFromLinearLight()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(0, 0, 0, 1)),
            new GradientStop(1.0f, Rgba16f.From(1, 1, 1, 1)),
        };

        var lutLinear = GradientLutBuilder.Build(stops, GradientInterpolationSpace.LinearLight);
        var lutSrgb = GradientLutBuilder.Build(stops, GradientInterpolationSpace.Srgb);

        bool differs = false;
        for (int i = 1; i < lutLinear.Length - 1; i++)
        {
            if (Math.Abs((float)lutLinear[i].R - (float)lutSrgb[i].R) > 0.001f)
            {
                differs = true;
                break;
            }
        }

        if (!differs)
            throw new InvalidOperationException("Expected sRGB and linear-light LUTs to differ");
    }

    [Test]
    public void BuildIntoReusesProvidedSpan()
    {
        var stops = new GradientStop[]
        {
            new GradientStop(0.0f, Rgba16f.From(1, 0, 0, 1)),
            new GradientStop(1.0f, Rgba16f.From(0, 0, 1, 1)),
        };

        var lut = new Rgba16f[256];
        GradientLutBuilder.BuildInto(stops, GradientInterpolationSpace.LinearLight, lut);

        if (Math.Abs((float)lut[128].R - 0.5f) > 0.01f)
            throw new InvalidOperationException($"Expected R=0.5, got {lut[128].R}");
    }

    [Test]
    public void GradientStopSizeIs12()
    {
        if (System.Runtime.InteropServices.Marshal.SizeOf<GradientStop>() != 12)
            throw new InvalidOperationException($"Expected sizeof(GradientStop)=12, got {System.Runtime.InteropServices.Marshal.SizeOf<GradientStop>()}");
    }
}
