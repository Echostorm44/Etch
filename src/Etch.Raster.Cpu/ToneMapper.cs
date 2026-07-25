using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

public static class ToneMapper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyReinhard(Span<Rgba16f> pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            ref var p = ref pixels[i];
            float r = (float)p.R;
            float g = (float)p.G;
            float b = (float)p.B;
            float a = (float)p.A;

            float luminance = 0.2126f * r + 0.7152f * g + 0.0722f * b;

            if (luminance > 1f)
            {
                float scale = (1f + luminance) / (1f + luminance * luminance);

                r = Math.Clamp(r * scale, 0f, 1f);
                g = Math.Clamp(g * scale, 0f, 1f);
                b = Math.Clamp(b * scale, 0f, 1f);
            }
            else
            {
                r = Math.Clamp(r, 0f, 1f);
                g = Math.Clamp(g, 0f, 1f);
                b = Math.Clamp(b, 0f, 1f);
            }

            p = Rgba16f.From(r, g, b, a);
        }
    }
}
