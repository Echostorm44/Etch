using System;
using System.Runtime.CompilerServices;
using Etch.ClipBlendGradient;

namespace Etch.Raster.Cpu;

public static class ClipBlender
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyClipCoverage(ReadOnlySpan<byte> clipCoverage, Span<byte> stripCoverage)
    {
        if (clipCoverage.Length == 0 || stripCoverage.Length == 0)
            return;

        if (clipCoverage.Length != stripCoverage.Length)
            Panic.Invariant(PanicCodes.SpanLengthMismatch, $"clipCoverage.Length ({clipCoverage.Length}) != stripCoverage.Length ({stripCoverage.Length})");

        for (int i = 0; i < stripCoverage.Length; i++)
        {
            float clipAlpha = clipCoverage[i] * (1.0f / 255.0f);
            int stripped = (int)(stripCoverage[i] * clipAlpha);
            stripCoverage[i] = (byte)Math.Min(255, stripped);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyClipCoverageRgba16f(ReadOnlySpan<Rgba16f> clipMask, Span<byte> stripCoverage, int startX, int width)
    {
        if (clipMask.Length == 0 || stripCoverage.Length == 0)
            return;

        int clipStart = startX;
        int clipEnd = clipStart + width;

        if (clipEnd > clipMask.Length)
            clipEnd = clipMask.Length;

        int j = 0;
        for (int i = clipStart; i < clipEnd && j < stripCoverage.Length; i++, j++)
        {
            float clipAlpha = (float)clipMask[i].R;
            int stripped = (int)(stripCoverage[j] * clipAlpha);
            stripCoverage[j] = (byte)Math.Min(255, stripped);
        }
    }
}
