using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

/// <summary>
/// W3C Compositing and Blending Level 1 §9.17 helper functions for
/// non-separable blend modes (Hue, Saturation, Color, Luminosity).
/// All functions operate on linear-light float RGB in [0,1].
/// </summary>
public static class NonSeparableBlendHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lum(float r, float g, float b)
        => 0.30f * r + 0.59f * g + 0.11f * b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sat(float r, float g, float b)
        => Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ClipColor(ref float r, ref float g, ref float b)
    {
        float l = Lum(r, g, b);
        float n = Math.Min(r, Math.Min(g, b));
        float x = Math.Max(r, Math.Max(g, b));

        if (n < 0.0f)
        {
            float denom = l - n;
            if (denom > 0.0f)
            {
                r = l + (r - l) * l / denom;
                g = l + (g - l) * l / denom;
                b = l + (b - l) * l / denom;
            }
        }

        if (x > 1.0f)
        {
            float denom = x - l;
            if (denom > 0.0f)
            {
                r = l + (r - l) * (1.0f - l) / denom;
                g = l + (g - l) * (1.0f - l) / denom;
                b = l + (b - l) * (1.0f - l) / denom;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetLum(ref float r, ref float g, ref float b, float l)
    {
        float d = l - Lum(r, g, b);
        r += d;
        g += d;
        b += d;
        ClipColor(ref r, ref g, ref b);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetSat(ref float r, ref float g, ref float b, float s)
    {
        // Three-element sort to identify min/mid/max channels
        float c0 = r, c1 = g, c2 = b;
        int i0 = 0, i1 = 1, i2 = 2;

        if (c0 > c1) { (c0, c1) = (c1, c0); (i0, i1) = (i1, i0); }
        if (c1 > c2) { (c1, c2) = (c2, c1); (i1, i2) = (i2, i1); }
        if (c0 > c1) { (c0, c1) = (c1, c0); (i0, i1) = (i1, i0); }

        float min = c0, mid = c1, max = c2;

        if (max > min)
        {
            mid = ((mid - min) * s) / (max - min);
            max = s;
            min = 0.0f;
        }
        else
        {
            mid = max = min = 0.0f;
        }

        Span<float> arr = stackalloc float[3];
        arr[i0] = min;
        arr[i1] = mid;
        arr[i2] = max;
        r = arr[0];
        g = arr[1];
        b = arr[2];
    }
}
