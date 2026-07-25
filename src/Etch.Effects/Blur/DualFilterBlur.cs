using System;
using System.Buffers;
using Etch.Effects.Image;
using Etch.Gpu;

namespace Etch.Effects.Blur;

public static class DualFilterBlur
{
    public const int MaxOctaves = 6;

    public static int OctaveCount(float radiusPx)
    {
        if (radiusPx <= 0f)
            return 0;

        double octaves = Math.Ceiling(Math.Log2(radiusPx + 1.0));
        int result = (int)octaves;
        return result > MaxOctaves ? MaxOctaves : result;
    }

    public static void Apply(ImageSource src, BlurParams p, Texture dst)
    {
        ArgumentNullException.ThrowIfNull(src);
        if (p.RadiusPx <= 0)
            return;

        int octaves = OctaveCount(p.RadiusPx);
        if (octaves == 0)
            return;

        int w = src.Width;
        int h = src.Height;

        byte[] pixels = ArrayPool<byte>.Shared.Rent(w * h * 4);
        byte[] temp = ArrayPool<byte>.Shared.Rent(w * h * 4);
        try
        {
            src.CopyTo(pixels);

            for (int o = 0; o < octaves; o++)
            {
                int radius = 1 << o;
                ApplyHorizontalBlur(pixels, temp, w, h, radius);
                ApplyVerticalBlur(temp, pixels, w, h, radius);
            }

            UploadToTexture(pixels, w, h, dst);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pixels);
            ArrayPool<byte>.Shared.Return(temp);
        }
    }

    private static void ApplyHorizontalBlur(byte[] src, byte[] dst, int w, int h, int radius)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int sx = x + dx;
                    if ((uint)sx >= (uint)w) continue;
                    int idx = (y * w + sx) * 4;
                    r += src[idx + 0];
                    g += src[idx + 1];
                    b += src[idx + 2];
                    a += src[idx + 3];
                    count++;
                }
                if (count > 0)
                {
                    int idx = (y * w + x) * 4;
                    dst[idx + 0] = (byte)(r / count);
                    dst[idx + 1] = (byte)(g / count);
                    dst[idx + 2] = (byte)(b / count);
                    dst[idx + 3] = (byte)(a / count);
                }
            }
        }
    }

    private static void ApplyVerticalBlur(byte[] src, byte[] dst, int w, int h, int radius)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                int count = 0;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int sy = y + dy;
                    if ((uint)sy >= (uint)h) continue;
                    int idx = (sy * w + x) * 4;
                    r += src[idx + 0];
                    g += src[idx + 1];
                    b += src[idx + 2];
                    a += src[idx + 3];
                    count++;
                }
                if (count > 0)
                {
                    int idx = (y * w + x) * 4;
                    dst[idx + 0] = (byte)(r / count);
                    dst[idx + 1] = (byte)(g / count);
                    dst[idx + 2] = (byte)(b / count);
                    dst[idx + 3] = (byte)(a / count);
                }
            }
        }
    }

    private static void UploadToTexture(byte[] pixels, int w, int h, Texture dst)
    {
        _ = pixels; _ = dst;
        // GPU texture upload deferred to GPU-017.
    }
}
