using System;
using Etch.ClipBlendGradient;
using Etch.Raster.Cpu;
using TUnit;

namespace Etch.Raster.Cpu.Tests;

internal sealed class BlendModeTests
{
    [Test]
    public void NormalBlendIdentityOnBlack()
    {
        byte[] coverage = new byte[] { 255, 255 };
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { black, black };

        NormalBlender.Blend(coverage, red, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
        if (Math.Abs((float)row[0].G - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected G=0, got {row[0].G}");
        if (Math.Abs((float)row[0].B - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected B=0, got {row[0].B}");
    }

    [Test]
    public void NormalBlendFiftyPercentAlpha()
    {
        byte[] coverage = new byte[] { 128 };
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 0.5f);
        Rgba16f[] row = new Rgba16f[] { black };

        NormalBlender.Blend(coverage, red, row);

        float coverageF = 128 * (1.0f / 255.0f);
        float srcAlpha = 0.5f * coverageF;
        float resultA = srcAlpha + 1.0f * (1.0f - srcAlpha);
        float expectedR = (1f * srcAlpha + 0f * (1.0f - srcAlpha)) / resultA;
        if (Math.Abs((float)row[0].R - expectedR) > 0.001f)
            throw new InvalidOperationException($"Expected R={expectedR}, got {row[0].R}");
        if (Math.Abs((float)row[0].A - resultA) > 0.001f)
            throw new InvalidOperationException($"Expected A={resultA}, got {row[0].A}");
    }

    [Test]
    public void MultiplyBlendWhiteTimesAny()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f white = Rgba16f.From(1, 1, 1, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        MultiplyBlender.Blend(coverage, white, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
        if (Math.Abs((float)row[0].G - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected G=0, got {row[0].G}");
        if (Math.Abs((float)row[0].B - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected B=0, got {row[0].B}");
    }

    [Test]
    public void MultiplyBlendBlackTimesAny()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        MultiplyBlender.Blend(coverage, black, row);

        if (Math.Abs((float)row[0].R - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected R=0, got {row[0].R}");
        if (Math.Abs((float)row[0].G - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected G=0, got {row[0].G}");
        if (Math.Abs((float)row[0].B - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected B=0, got {row[0].B}");
    }

    [Test]
    public void ScreenBlendBlackScreenPreserves()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        ScreenBlender.Blend(coverage, black, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
        if (Math.Abs((float)row[0].G - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected G=0, got {row[0].G}");
    }

    [Test]
    public void ScreenBlendWhiteScreenProducesWhite()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f white = Rgba16f.From(1, 1, 1, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        ScreenBlender.Blend(coverage, white, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
        if (Math.Abs((float)row[0].G - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected G=1, got {row[0].G}");
        if (Math.Abs((float)row[0].B - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected B=1, got {row[0].B}");
    }

    [Test]
    public void DarkenBlendPicksMinimum()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f blue = Rgba16f.From(0, 0, 1, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { blue };

        DarkenBlender.Blend(coverage, red, row);

        if (Math.Abs((float)row[0].R - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected R=0, got {row[0].R}");
        if (Math.Abs((float)row[0].B - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected B=0, got {row[0].B}");
    }

    [Test]
    public void LightenBlendPicksMaximum()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f blue = Rgba16f.From(0, 0, 1, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { blue };

        LightenBlender.Blend(coverage, red, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
        if (Math.Abs((float)row[0].B - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected B=1, got {row[0].B}");
    }

    [Test]
    public void DifferenceBlendWhiteMinusBlack()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f white = Rgba16f.From(1, 1, 1, 1);
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { black };

        DifferenceBlender.Blend(coverage, white, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
        if (Math.Abs((float)row[0].G - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected G=1, got {row[0].G}");
        if (Math.Abs((float)row[0].B - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected B=1, got {row[0].B}");
    }

    [Test]
    public void ExclusionBlendSameColorProducesBlack()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f red = Rgba16f.From(0.5f, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        ExclusionBlender.Blend(coverage, red, row);

        float expected = 0.5f + 0.5f - 2f * 0.5f * 0.5f;
        if (Math.Abs((float)row[0].R - expected) > 0.001f)
            throw new InvalidOperationException($"Expected R={expected}, got {row[0].R}");
    }

    [Test]
    public void ColorDodgeBlendBlackSrcDoesNothing()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f white = Rgba16f.From(1, 1, 1, 1);
        Rgba16f[] row = new Rgba16f[] { white };

        ColorDodgeBlender.Blend(coverage, black, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
        if (Math.Abs((float)row[0].G - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected G=1, got {row[0].G}");
        if (Math.Abs((float)row[0].B - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected B=1, got {row[0].B}");
    }

    [Test]
    public void ColorBurnBlendWhiteSrcProducesWhite()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f white = Rgba16f.From(1, 1, 1, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        ColorBurnBlender.Blend(coverage, white, row);

        if (Math.Abs((float)row[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Expected R=1, got {row[0].R}");
    }

    [Test]
    public void HardLightBlendSrcHalfGray()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f halfGray = Rgba16f.From(0.5f, 0.5f, 0.5f, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        HardLightBlender.Blend(coverage, halfGray, row);

        float expectedR = 2.0f * 0.5f * 1f;
        if (Math.Abs((float)row[0].R - expectedR) > 0.001f)
            throw new InvalidOperationException($"Expected R={expectedR}, got {row[0].R}");
    }

    [Test]
    public void SoftLightBlendBlackSrc()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        SoftLightBlender.Blend(coverage, black, row);

        if (Math.Abs((float)row[0].R - 0f) > 0.001f)
            throw new InvalidOperationException($"Expected R=0, got {row[0].R}");
    }

    [Test]
    public void OverlayBlendSrcHalfGrayDoubled()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f halfGray = Rgba16f.From(0.5f, 0.5f, 0.5f, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        OverlayBlender.Blend(coverage, halfGray, row);

        float expectedR = 2.0f * 0.5f * 1f;
        if (Math.Abs((float)row[0].R - expectedR) > 0.001f)
            throw new InvalidOperationException($"Expected R={expectedR}, got {row[0].R}");
    }

    [Test]
    public void HueBlendSrcHueDstSatLum()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f blue = Rgba16f.From(0, 0, 1, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        HueBlender.Blend(coverage, blue, row);

        if (Math.Abs((float)row[0].R - 0.2135f) > 0.02f)
            throw new InvalidOperationException($"Expected R≈0.2135, got {row[0].R}");
        if (Math.Abs((float)row[0].B - 1f) > 0.01f)
            throw new InvalidOperationException($"Expected B=1, got {row[0].B}");
    }

    [Test]
    public void SaturationBlendSrcSatDstHueLum()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f gray = Rgba16f.From(0.5f, 0.5f, 0.5f, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        SaturationBlender.Blend(coverage, gray, row);

        if (Math.Abs((float)row[0].R - 0.3f) > 0.02f)
            throw new InvalidOperationException($"Expected R≈0.3, got {row[0].R}");
    }

    [Test]
    public void ColorBlendSrcHueSatDstLum()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f blue = Rgba16f.From(0, 0, 1, 1);
        Rgba16f gray50 = Rgba16f.From(0.5f, 0.5f, 0.5f, 1);
        Rgba16f[] row = new Rgba16f[] { gray50 };

        ColorBlender.Blend(coverage, blue, row);

        if (Math.Abs((float)row[0].B - 1f) > 0.01f)
            throw new InvalidOperationException($"Expected B=1, got {row[0].B}");
        if (Math.Abs((float)row[0].R - 0.4382f) > 0.02f)
            throw new InvalidOperationException($"Expected R≈0.44, got {row[0].R}");
    }

    [Test]
    public void LuminosityBlendSrcLumDstHueSat()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f bright = Rgba16f.From(1, 1, 1, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { red };

        LuminosityBlender.Blend(coverage, bright, row);

        if ((float)row[0].R <= 0.9f)
            throw new InvalidOperationException($"Expected R>0.9, got {row[0].R}");
        if ((float)row[0].G <= 0.9f)
            throw new InvalidOperationException($"Expected G>0.9, got {row[0].G}");
        if ((float)row[0].B <= 0.9f)
            throw new InvalidOperationException($"Expected B>0.9, got {row[0].B}");
    }

    [Test]
    public void BlendModeDispatchAllModesDispatchCorrectly()
    {
        byte[] coverage = new byte[] { 255 };
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f[] rowNormal = new Rgba16f[] { black };
        Rgba16f[] rowMultiply = new Rgba16f[] { black };
        Rgba16f[] rowScreen = new Rgba16f[] { black };
        Rgba16f[] rowOverlay = new Rgba16f[] { black };

        BlendModeDispatch.Blend(BlendMode.Normal, coverage, red, rowNormal);
        BlendModeDispatch.Blend(BlendMode.Multiply, coverage, red, rowMultiply);
        BlendModeDispatch.Blend(BlendMode.Screen, coverage, red, rowScreen);
        BlendModeDispatch.Blend(BlendMode.Overlay, coverage, red, rowOverlay);

        if (Math.Abs((float)rowNormal[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Normal: Expected R=1, got {rowNormal[0].R}");
        if (Math.Abs((float)rowMultiply[0].R - 0f) > 0.001f)
            throw new InvalidOperationException($"Multiply: Expected R=0, got {rowMultiply[0].R}");
        if (Math.Abs((float)rowScreen[0].R - 1f) > 0.001f)
            throw new InvalidOperationException($"Screen: Expected R=1, got {rowScreen[0].R}");
    }

    [Test]
    public void NormalBlendPartialCoverageBlendsCorrectly()
    {
        byte[] coverage = new byte[] { 128, 255 };
        Rgba16f black = Rgba16f.From(0, 0, 0, 1);
        Rgba16f red = Rgba16f.From(1, 0, 0, 1);
        Rgba16f[] row = new Rgba16f[] { black, black };

        NormalBlender.Blend(coverage, red, row);

        float expected128 = 1f * 0.5f + 0f * 0.5f;
        float expected255 = 1f * 1f + 0f * 0f;
        if (Math.Abs((float)row[0].R - expected128) > 0.01f)
            throw new InvalidOperationException($"Expected R[0]={expected128}, got {row[0].R}");
        if (Math.Abs((float)row[1].R - expected255) > 0.001f)
            throw new InvalidOperationException($"Expected R[1]={expected255}, got {row[1].R}");
    }
}
