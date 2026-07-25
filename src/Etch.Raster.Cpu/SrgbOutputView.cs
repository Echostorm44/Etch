using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu;

public static class SrgbOutputView
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static float LinearToSrgb(float c)
    {
        if (c <= 0.0031308f)
        {
            return c * 12.92f;
        }

        return 1.055f * MathF.Pow(c, 1.0f / 2.4f) - 0.055f;
    }

    public static void Encode(Framebuffer src, Span<byte> bgra8Out, bool premultiplied)
    {
        if (bgra8Out.Length < src.Width * src.Height * 4)
        {
            Panic.ArgumentOutOfRange(nameof(bgra8Out), "Output buffer too small");
        }

        for (int y = 0; y < src.Height; y++)
        {
            var row = src.RowSpan(y);
            int baseIdx = y * src.Width * 4;

            for (int x = 0; x < src.Width; x++)
            {
                var pixel = row[x];
                float r = (float)pixel.R;
                float g = (float)pixel.G;
                float b = (float)pixel.B;
                float a = (float)pixel.A;

                if (premultiplied && a > 0)
                {
                    float invA = 1.0f / a;
                    r *= invA;
                    g *= invA;
                    b *= invA;
                }

                r = LinearToSrgb(r);
                g = LinearToSrgb(g);
                b = LinearToSrgb(b);

                int idx = baseIdx + x * 4;
                bgra8Out[idx + 0] = (byte)(b * 255.0f + 0.5f);
                bgra8Out[idx + 1] = (byte)(g * 255.0f + 0.5f);
                bgra8Out[idx + 2] = (byte)(r * 255.0f + 0.5f);
                bgra8Out[idx + 3] = (byte)(a * 255.0f + 0.5f);
            }
        }
    }
}