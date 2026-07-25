using System;
using System.Runtime.CompilerServices;
using Etch.Geometry;

namespace Etch.Raster.Cpu.Image;

public static class BilinearSampler
{
    public static void Sample(ReadOnlySpan<byte> srcPixels, int srcWidth, int srcHeight, Span<Rgba16f> dst, int dstStride, Affine transform)
    {
        int dstWidth = dstStride;
        int dstHeight = dst.Length / dstStride;

        Affine inverse = transform.Inverse();

        for (int y = 0; y < dstHeight; y++)
        {
            double v = (y + 0.5) / dstHeight;
            int dstIdx = y * dstStride;

            for (int x = 0; x < dstWidth; x++)
            {
                double u = (x + 0.5) / dstWidth;

                Point uv = inverse.Transform(new Point(u, v));
                double srcX = uv.X * srcWidth;
                double srcY = uv.Y * srcHeight;

                int x0 = (int)Math.Floor(srcX - 0.5);
                int y0 = (int)Math.Floor(srcY - 0.5);
                int x1 = x0 + 1;
                int y1 = y0 + 1;

                double fx = srcX - 0.5 - x0;
                double fy = srcY - 0.5 - y0;

                x0 = Extend.Clamp(x0, srcWidth);
                y0 = Extend.Clamp(y0, srcHeight);
                x1 = Extend.Clamp(x1, srcWidth);
                y1 = Extend.Clamp(y1, srcHeight);

                Rgba16f tl = GetPixel(srcPixels, srcWidth, x0, y0);
                Rgba16f tr = GetPixel(srcPixels, srcWidth, x1, y0);
                Rgba16f bl = GetPixel(srcPixels, srcWidth, x0, y1);
                Rgba16f br = GetPixel(srcPixels, srcWidth, x1, y1);

                Rgba16f top = Lerp(tl, tr, fx);
                Rgba16f bottom = Lerp(bl, br, fx);
                Rgba16f result = Lerp(top, bottom, fy);

                dst[dstIdx++] = result;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba16f GetPixel(ReadOnlySpan<byte> pixels, int width, int x, int y)
    {
        int idx = (y * width + x) * 4;
        return Rgba16f.FromLinearBytes(pixels[idx], pixels[idx + 1], pixels[idx + 2], pixels[idx + 3]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba16f Lerp(Rgba16f a, Rgba16f b, double t)
    {
        double oneMinusT = 1.0 - t;
        return Rgba16f.From(
            (float)(oneMinusT * (double)a.R + t * (double)b.R),
            (float)(oneMinusT * (double)a.G + t * (double)b.G),
            (float)(oneMinusT * (double)a.B + t * (double)b.B),
            (float)(oneMinusT * (double)a.A + t * (double)b.A));
    }
}
