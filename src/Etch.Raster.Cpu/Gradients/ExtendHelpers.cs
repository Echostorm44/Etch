using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.ClipBlendGradient;

namespace Etch.Raster.Cpu.Gradients;

public static class ExtendHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Apply(float t, GradientExtend extendMode)
    {
        switch (extendMode)
        {
            case GradientExtend.Reflect:
                return 1.0f - MathF.Abs(t - 0.5f) * 2.0f;
            case GradientExtend.Repeat:
                return Fract(t);
            default:
                return t < 0.0f ? 0.0f : t > 1.0f ? 1.0f : t;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Fract(float x)
    {
        return x - MathF.Floor(x);
    }

    public static ReadOnlySpan<Rgba16f> ReinterpretLut(ReadOnlySpan<Etch.ClipBlendGradient.Rgba16f> cbgLut)
    {
        return MemoryMarshal.Cast<Etch.ClipBlendGradient.Rgba16f, Rgba16f>(cbgLut);
    }
}
