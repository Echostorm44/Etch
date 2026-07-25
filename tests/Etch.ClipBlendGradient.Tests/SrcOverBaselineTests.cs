using System;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class SrcOverBaselineTests
{
    [Test]
    public async Task FullyOpaqueSrc_ReturnsSrcExactly()
    {
        var src = Rgba16f.From(0.25f, 0.50f, 0.75f, 1.0f);
        var dst = Rgba16f.From(0.10f, 0.20f, 0.30f, 0.40f);

        var result = SrcOverReference.Composite(src, dst);

        await Assert.That(result.R).IsEqualTo(src.R);
        await Assert.That(result.G).IsEqualTo(src.G);
        await Assert.That(result.B).IsEqualTo(src.B);
        await Assert.That(result.A).IsEqualTo(src.A);
    }

    [Test]
    public async Task FullyTransparentSrc_ReturnsDstExactly()
    {
        var src = Rgba16f.From(0.25f, 0.50f, 0.75f, 0.0f);
        var dst = Rgba16f.From(0.10f, 0.20f, 0.30f, 0.40f);

        var result = SrcOverReference.Composite(src, dst);

        await Assert.That(result.R).IsEqualTo(dst.R);
        await Assert.That(result.G).IsEqualTo(dst.G);
        await Assert.That(result.B).IsEqualTo(dst.B);
        await Assert.That(result.A).IsEqualTo(dst.A);
    }

    [Test]
    public void All256SrcDstCombinations_CpuMatchesReference()
    {
        int failures = 0;
        string failureDetails = "";

        for (int srcVal = 0; srcVal <= 255; srcVal += 17)
        {
            for (int dstVal = 0; dstVal <= 255; dstVal += 17)
            {
                for (int alphaVal = 0; alphaVal <= 255; alphaVal += 17)
                {
                    float srcLevel = srcVal / 255f;
                    float dstLevel = dstVal / 255f;
                    float alphaLevel = alphaVal / 255f;

                    var src = Rgba16f.From(srcLevel, srcLevel, srcLevel, alphaLevel);
                    var dst = Rgba16f.From(dstLevel, dstLevel, dstLevel, dstLevel > 0 ? 0.5f : 0f);
                    if (dstVal > 0) dst = Rgba16f.From(dstLevel, dstLevel, dstLevel, 0.5f);

                    var result = SrcOverReference.Composite(src, dst);

                    float srcA = alphaLevel;
                    float dstA = (float)dst.A;
                    float expectedA = srcA + dstA * (1.0f - srcA);

                    if (expectedA > 0.0001f)
                    {
                        float invSrcA = 1.0f - srcA;
                        float expectedR = (srcLevel * srcA + dstLevel * dstA * invSrcA) / expectedA;
                        float expectedG = expectedR;
                        float expectedB = expectedR;

                        float resultR = (float)result.R;
                        float resultG = (float)result.G;
                        float resultB = (float)result.B;

                        int diffR = PixelDiff(resultR, expectedR);
                        int diffG = PixelDiff(resultG, expectedG);
                        int diffB = PixelDiff(resultB, expectedB);

                        if (diffR > 0 || diffG > 0 || diffB > 0)
                        {
                            failures++;
                            if (failures <= 3)
                            {
                                failureDetails += $"src=({srcLevel:F2},{srcLevel:F2},{srcLevel:F2},{srcA:F2}) dst=({dstLevel:F2},{dstLevel:F2},{dstLevel:F2},{dstA:F2}) expected=({expectedR:F4},{expectedG:F4},{expectedB:F4}) got=({resultR:F4},{resultG:F4},{resultB:F4})\n";
                            }
                        }
                    }
                }
            }
        }

        if (failures > 0)
        {
            throw new InvalidOperationException($"{failures} combinations failed:\n{failureDetails}");
        }
    }

    [Test]
    public void AllCombinations_AlphaIsCorrect()
    {
        for (int srcA = 0; srcA <= 255; srcA += 51)
        {
            for (int dstA = 0; dstA <= 255; dstA += 51)
            {
                var src = Rgba16f.From(0.5f, 0.5f, 0.5f, srcA / 255f);
                var dst = Rgba16f.From(0.5f, 0.5f, 0.5f, dstA / 255f);

                var result = SrcOverReference.Composite(src, dst);

                float expectedA = (srcA / 255f) + (dstA / 255f) * (1.0f - srcA / 255f);
                float resultA = (float)result.A;

                float diff = Math.Abs(resultA - expectedA);
                if (diff > 0.01f)
                    throw new InvalidOperationException($"Alpha mismatch: srcA={srcA} dstA={dstA} expected={expectedA:F4} got={resultA:F4}");
            }
        }
    }

    [Test]
    public void ZeroDestination_ReturnsSourceWithPreMultiplication()
    {
        var src = Rgba16f.From(0.25f, 0.50f, 0.75f, 0.8f);
        var dst = Rgba16f.Zero;

        var result = SrcOverReference.Composite(src, dst);

        float diffR = Math.Abs((float)result.R - 0.25f);
        float diffG = Math.Abs((float)result.G - 0.50f);
        float diffB = Math.Abs((float)result.B - 0.75f);
        float diffA = Math.Abs((float)result.A - 0.8f);

        if (diffR > 0.001f || diffG > 0.001f || diffB > 0.001f || diffA > 0.001f)
            throw new InvalidOperationException($"Mismatch: R({diffR:F6}) G({diffG:F6}) B({diffB:F6}) A({diffA:F6})");
    }

    [Test]
    public void PremultipliedVariant_ProducesCorrectResult()
    {
        var src = Rgba16f.From(0.2f, 0.4f, 0.6f, 0.5f);
        var dst = Rgba16f.From(0.3f, 0.3f, 0.3f, 0.7f);

        var result = SrcOverReference.Composite(src, dst);

        float srcA = 0.5f;
        float dstA = 0.7f;
        float resultA = srcA + dstA * (1.0f - srcA);
        float invSrcA = 1.0f - srcA;
        float expectedR = (0.2f * srcA + 0.3f * dstA * invSrcA) / resultA;
        float expectedG = (0.4f * srcA + 0.3f * dstA * invSrcA) / resultA;
        float expectedB = (0.6f * srcA + 0.3f * dstA * invSrcA) / resultA;

        float diffR = Math.Abs((float)result.R - expectedR);
        float diffG = Math.Abs((float)result.G - expectedG);
        float diffB = Math.Abs((float)result.B - expectedB);
        float diffA = Math.Abs((float)result.A - resultA);

        if (diffR > 0.001f || diffG > 0.001f || diffB > 0.001f || diffA > 0.001f)
            throw new InvalidOperationException($"Mismatch: R({diffR:F6}) G({diffG:F6}) B({diffB:F6}) A({diffA:F6})");
    }

    private static int PixelDiff(float a, float b)
    {
        return (int)(Math.Abs(a - b) * 255);
    }
}
