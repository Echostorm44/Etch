using System;
using System.Runtime.CompilerServices;
using Etch.Geometry;

namespace Etch.Raster.Cpu.Image;

public static class BicubicSampler
{
    private const int LutSize = 1024;
    private const int LutSizeMinusOne = LutSize - 1;
    private const float LutScale = LutSizeMinusOne;

    private static readonly float[] Weights = new float[LutSize * 4];

    static BicubicSampler()
    {
        const float B = 1f / 3f;
        const float C = 1f / 3f;

        for (int i = 0; i < LutSize; i++)
        {
            float x = (float)i / LutScale;
            Weights[i * 4 + 0] = MitchellNetravali(Math.Abs(x + 1f), B, C);
            Weights[i * 4 + 1] = MitchellNetravali(Math.Abs(x), B, C);
            Weights[i * 4 + 2] = MitchellNetravali(Math.Abs(x - 1f), B, C);
            Weights[i * 4 + 3] = MitchellNetravali(Math.Abs(x - 2f), B, C);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float MitchellNetravali(float x, float B, float C)
    {
        if (x < 1f)
        {
            float x2 = x * x;
            float x3 = x2 * x;
            return (12f - 9f * B - 6f * C) * x3 + (-18f + 12f * B + 6f * C) * x2 + (6f - 2f * B);
        }
        else if (x < 2f)
        {
            float x2 = x * x;
            float x3 = x2 * x;
            return (-B - 6f * C) * x3 + (6f * B + 30f * C) * x2 + (-12f * B - 48f * C) * x + (8f * B + 24f * C);
        }
        return 0f;
    }

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
                double srcX = uv.X * srcWidth - 0.5;
                double srcY = uv.Y * srcHeight - 0.5;

                double fx = srcX - Math.Floor(srcX);
                double fy = srcY - Math.Floor(srcY);

                int xInt = (int)Math.Floor(srcX);
                int yInt = (int)Math.Floor(srcY);

                int xOffsets0 = Extend.Clamp(xInt - 1, srcWidth);
                int xOffsets1 = Extend.Clamp(xInt, srcWidth);
                int xOffsets2 = Extend.Clamp(xInt + 1, srcWidth);
                int xOffsets3 = Extend.Clamp(xInt + 2, srcWidth);

                int yOffsets0 = Extend.Clamp(yInt - 1, srcHeight);
                int yOffsets1 = Extend.Clamp(yInt, srcHeight);
                int yOffsets2 = Extend.Clamp(yInt + 1, srcHeight);
                int yOffsets3 = Extend.Clamp(yInt + 2, srcHeight);

                int lutIdxX = (int)(fx * LutScale);
                int lutIdxY = (int)(fy * LutScale);
                lutIdxX = lutIdxX < 0 ? 0 : lutIdxX > LutSizeMinusOne ? LutSizeMinusOne : lutIdxX;
                lutIdxY = lutIdxY < 0 ? 0 : lutIdxY > LutSizeMinusOne ? LutSizeMinusOne : lutIdxY;

                float wx0 = Weights[lutIdxX * 4 + 0];
                float wx1 = Weights[lutIdxX * 4 + 1];
                float wx2 = Weights[lutIdxX * 4 + 2];
                float wx3 = Weights[lutIdxX * 4 + 3];

                float wy0 = Weights[lutIdxY * 4 + 0];
                float wy1 = Weights[lutIdxY * 4 + 1];
                float wy2 = Weights[lutIdxY * 4 + 2];
                float wy3 = Weights[lutIdxY * 4 + 3];

                Rgba16f c00 = GetPixel(srcPixels, srcWidth, xOffsets0, yOffsets0);
                Rgba16f c01 = GetPixel(srcPixels, srcWidth, xOffsets1, yOffsets0);
                Rgba16f c02 = GetPixel(srcPixels, srcWidth, xOffsets2, yOffsets0);
                Rgba16f c03 = GetPixel(srcPixels, srcWidth, xOffsets3, yOffsets0);

                Rgba16f c10 = GetPixel(srcPixels, srcWidth, xOffsets0, yOffsets1);
                Rgba16f c11 = GetPixel(srcPixels, srcWidth, xOffsets1, yOffsets1);
                Rgba16f c12 = GetPixel(srcPixels, srcWidth, xOffsets2, yOffsets1);
                Rgba16f c13 = GetPixel(srcPixels, srcWidth, xOffsets3, yOffsets1);

                Rgba16f c20 = GetPixel(srcPixels, srcWidth, xOffsets0, yOffsets2);
                Rgba16f c21 = GetPixel(srcPixels, srcWidth, xOffsets1, yOffsets2);
                Rgba16f c22 = GetPixel(srcPixels, srcWidth, xOffsets2, yOffsets2);
                Rgba16f c23 = GetPixel(srcPixels, srcWidth, xOffsets3, yOffsets2);

                Rgba16f c30 = GetPixel(srcPixels, srcWidth, xOffsets0, yOffsets3);
                Rgba16f c31 = GetPixel(srcPixels, srcWidth, xOffsets1, yOffsets3);
                Rgba16f c32 = GetPixel(srcPixels, srcWidth, xOffsets2, yOffsets3);
                Rgba16f c33 = GetPixel(srcPixels, srcWidth, xOffsets3, yOffsets3);

                float r = 0f, g = 0f, b = 0f, a = 0f;

                r += wx0 * (wy0 * (float)c00.R + wy1 * (float)c10.R + wy2 * (float)c20.R + wy3 * (float)c30.R);
                r += wx1 * (wy0 * (float)c01.R + wy1 * (float)c11.R + wy2 * (float)c21.R + wy3 * (float)c31.R);
                r += wx2 * (wy0 * (float)c02.R + wy1 * (float)c12.R + wy2 * (float)c22.R + wy3 * (float)c32.R);
                r += wx3 * (wy0 * (float)c03.R + wy1 * (float)c13.R + wy2 * (float)c23.R + wy3 * (float)c33.R);

                g += wx0 * (wy0 * (float)c00.G + wy1 * (float)c10.G + wy2 * (float)c20.G + wy3 * (float)c30.G);
                g += wx1 * (wy0 * (float)c01.G + wy1 * (float)c11.G + wy2 * (float)c21.G + wy3 * (float)c31.G);
                g += wx2 * (wy0 * (float)c02.G + wy1 * (float)c12.G + wy2 * (float)c22.G + wy3 * (float)c32.G);
                g += wx3 * (wy0 * (float)c03.G + wy1 * (float)c13.G + wy2 * (float)c23.G + wy3 * (float)c33.G);

                b += wx0 * (wy0 * (float)c00.B + wy1 * (float)c10.B + wy2 * (float)c20.B + wy3 * (float)c30.B);
                b += wx1 * (wy0 * (float)c01.B + wy1 * (float)c11.B + wy2 * (float)c21.B + wy3 * (float)c31.B);
                b += wx2 * (wy0 * (float)c02.B + wy1 * (float)c12.B + wy2 * (float)c22.B + wy3 * (float)c32.B);
                b += wx3 * (wy0 * (float)c03.B + wy1 * (float)c13.B + wy2 * (float)c23.B + wy3 * (float)c33.B);

                a += wx0 * (wy0 * (float)c00.A + wy1 * (float)c10.A + wy2 * (float)c20.A + wy3 * (float)c30.A);
                a += wx1 * (wy0 * (float)c01.A + wy1 * (float)c11.A + wy2 * (float)c21.A + wy3 * (float)c31.A);
                a += wx2 * (wy0 * (float)c02.A + wy1 * (float)c12.A + wy2 * (float)c22.A + wy3 * (float)c32.A);
                a += wx3 * (wy0 * (float)c03.A + wy1 * (float)c13.A + wy2 * (float)c23.A + wy3 * (float)c33.A);

                dst[dstIdx++] = Rgba16f.From(r, g, b, a);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Rgba16f GetPixel(ReadOnlySpan<byte> pixels, int width, int x, int y)
    {
        int idx = (y * width + x) * 4;
        return Rgba16f.FromLinearBytes(pixels[idx], pixels[idx + 1], pixels[idx + 2], pixels[idx + 3]);
    }
}
