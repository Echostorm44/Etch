using System;
using TUnit;

namespace Etch.ClipBlendGradient.Tests;

public sealed class ClipComposerTests
{
    [Test]
    public void IntersectAlphaHalfTimesHalfGivesQuarter()
    {
        byte halfA = 128;
        byte halfB = 128;
        byte result = ClipComposer.IntersectAlpha(halfA, halfB);
        int expected = (int)(0.25f * 255);
        if (Math.Abs(result - expected) > 1)
            throw new InvalidOperationException($"Expected ~{expected}, got {result}");
    }

    [Test]
    public void IntersectAlphaFullTimesFullGivesFull()
    {
        byte full = 255;
        byte result = ClipComposer.IntersectAlpha(full, full);
        if (result != 255)
            throw new InvalidOperationException($"Expected 255, got {result}");
    }

    [Test]
    public void IntersectAlphaZeroTimesAnythingGivesZero()
    {
        byte zero = 0;
        byte result = ClipComposer.IntersectAlpha(zero, 128);
        if (result != 0)
            throw new InvalidOperationException($"Expected 0, got {result}");
    }

    [Test]
    public void DifferenceAlphaFullMinusFullGivesZero()
    {
        byte full = 255;
        byte result = ClipComposer.DifferenceAlpha(full, full);
        if (result != 0)
            throw new InvalidOperationException($"Expected 0, got {result}");
    }

    [Test]
    public void DifferenceAlphaFullMinusZeroGivesFull()
    {
        byte full = 255;
        byte zero = 0;
        byte result = ClipComposer.DifferenceAlpha(full, zero);
        if (result < 254)
            throw new InvalidOperationException($"Expected ~255, got {result}");
    }

    [Test]
    public void DifferenceAlphaHalfMinusHalfGivesQuarter()
    {
        byte half = 128;
        byte result = ClipComposer.DifferenceAlpha(half, half);
        int expected = (int)(0.5f * 0.5f * 255);
        if (Math.Abs(result - expected) > 2)
            throw new InvalidOperationException($"Expected ~{expected}, got {result}");
    }

    [Test]
    public void IntersectTwoDeepMaskAA()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x0001, 0, 0, 0),
        };

        var coverageA = new byte[] { 128 };
        var coverageB = new byte[] { 128 };

        var tileOffsets = new int[] { 0, 1 };

        var bufferA = new ClipMaskBuffer(strips, tileOffsets, coverageA, 1);
        var bufferB = new ClipMaskBuffer(strips, tileOffsets, coverageB, 1);

        var stack = new ClipMaskBuffer[] { bufferA, bufferB };
        var result = ClipComposer.Intersect(stack);

        if (result.StripCount != 1)
            throw new InvalidOperationException($"Expected 1 strip, got {result.StripCount}");

        var coverage = result.CoverageForStrip(in result.Strips[0]);
        byte expected = ClipComposer.IntersectAlpha(128, 128);
        if (Math.Abs(coverage[0] - expected) > 1)
            throw new InvalidOperationException($"Expected ~{expected}, got {coverage[0]}");
    }

    [Test]
    public void IntersectEightDeepNesting()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x0001, 0, 0, 0),
        };

        var tileOffsets = new int[] { 0, 1 };

        var buffers = new ClipMaskBuffer[8];
        for (int i = 0; i < 8; i++)
        {
            var clipCoverage = new byte[] { 128 };
            buffers[i] = new ClipMaskBuffer(strips, tileOffsets, clipCoverage, 1);
        }

        var result = ClipComposer.Intersect(buffers);

        var resultCoverage = result.CoverageForStrip(in result.Strips[0]);
        float expectedFloat = (float)Math.Pow(0.5, 8) * 255;
        int expected = (int)expectedFloat;
        if (Math.Abs(resultCoverage[0] - expected) > 2)
            throw new InvalidOperationException($"Expected ~{expected}, got {resultCoverage[0]}");
    }

    [Test]
    public void ApplyDifferenceFullMinusFullGivesZero()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x0001, 0, 0, 0),
        };

        var coverageBg = new byte[] { 255 };
        var coverageFg = new byte[] { 255 };

        var tileOffsets = new int[] { 0, 1 };

        var bg = new ClipMaskBuffer(strips, tileOffsets, coverageBg, 1);
        var fg = new ClipMaskBuffer(strips, tileOffsets, coverageFg, 1);

        var result = ClipComposer.ApplyDifference(bg, fg);

        var coverage = result.CoverageForStrip(in result.Strips[0]);
        if (coverage[0] != 0)
            throw new InvalidOperationException($"Expected 0, got {coverage[0]}");
    }

    [Test]
    public void EmptyStackReturnsEmptyBuffer()
    {
        var stack = Array.Empty<ClipMaskBuffer>();
        var result = ClipComposer.Intersect(stack);

        if (result.StripCount != 0)
            throw new InvalidOperationException($"Expected 0 strips, got {result.StripCount}");
    }

    [Test]
    public void SingleElementStackReturnsSameBuffer()
    {
        var strips = new ClipStrip[]
        {
            new ClipStrip(0x0001, 0, 0, 0),
        };
        var coverage = new byte[] { 128 };
        var tileOffsets = new int[] { 0, 1 };
        var buffer = new ClipMaskBuffer(strips, tileOffsets, coverage, 1);

        var stack = new ClipMaskBuffer[] { buffer };
        var result = ClipComposer.Intersect(stack);

        if (result.StripCount != 1)
            throw new InvalidOperationException($"Expected 1 strip, got {result.StripCount}");
    }
}
