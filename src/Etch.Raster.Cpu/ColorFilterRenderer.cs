using System;
using System.Runtime.CompilerServices;
using Etch.Scene;

namespace Etch.Raster.Cpu;

public static class ColorFilterRenderer
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyFilter(Span<Rgba16f> pixels, ColorFilter filter)
    {
        if (filter.IsIdentity)
            return;

        for (int i = 0; i < pixels.Length; i++)
        {
            float r = (float)pixels[i].R;
            float g = (float)pixels[i].G;
            float b = (float)pixels[i].B;
            float a = (float)pixels[i].A;

            float rOut = filter.M11 * r + filter.M12 * g + filter.M13 * b + filter.M14 * a + filter.M15;
            float gOut = filter.M21 * r + filter.M22 * g + filter.M23 * b + filter.M24 * a + filter.M25;
            float bOut = filter.M31 * r + filter.M32 * g + filter.M33 * b + filter.M34 * a + filter.M35;
            float aOut = filter.M41 * r + filter.M42 * g + filter.M43 * b + filter.M44 * a + filter.M45;

            pixels[i] = Rgba16f.From(
                Math.Clamp(rOut, 0f, 1f),
                Math.Clamp(gOut, 0f, 1f),
                Math.Clamp(bOut, 0f, 1f),
                Math.Clamp(aOut, 0f, 1f));
        }
    }
}
