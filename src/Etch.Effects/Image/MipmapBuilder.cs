using System;
using System.Runtime.CompilerServices;

namespace Etch.Effects.Image;

public static class MipmapBuilder
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMipLevelCount(int width, int height)
    {
        int minDim = width < height ? width : height;
        int count = 1;
        int dim = minDim;
        while (dim > 1)
        {
            dim >>= 1;
            count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMipSize(int baseSize, int level)
    {
        return Math.Max(1, baseSize >> level);
    }

    public static int GetTotalBytes(int width, int height, ImageFormat fmt)
    {
        int bytesPerPixel = GetBytesPerPixel(fmt);
        int total = 0;
        int w = width;
        int h = height;
        while (w > 1 || h > 1)
        {
            total += w * h * bytesPerPixel;
            w >>= 1;
            h >>= 1;
            if (w == 0) w = 1;
            if (h == 0) h = 1;
        }
        total += w * h * bytesPerPixel;
        return total;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBytesPerPixel(ImageFormat fmt)
    {
        return fmt switch
        {
            ImageFormat.Rgba8Unorm => 4,
            ImageFormat.Srgb8UnormAlpha => 4,
            ImageFormat.Rgba16f => 8,
            _ => 4,
        };
    }

    public static void Build(ReadOnlySpan<byte> l0, int w, int h, ImageFormat fmt, Span<byte> destAll)
    {
        int bytesPerPixel = GetBytesPerPixel(fmt);
        int levelCount = GetMipLevelCount(w, h);

        int destOffset = 0;
        int srcW = w;
        int srcH = h;

        for (int level = 1; level < levelCount; level++)
        {
            int dstW = Math.Max(1, srcW >> 1);
            int dstH = Math.Max(1, srcH >> 1);
            int dstSize = dstW * dstH * bytesPerPixel;

            if (destOffset + dstSize > destAll.Length)
            {
                Panic.ArgumentOutOfRange(nameof(destAll), "destAll too small for mip data");
            }

            Span<byte> dst = destAll.Slice(destOffset, dstSize);
            GenerateLevel(l0, w, h, dst, dstW, dstH, bytesPerPixel, level);
            destOffset += dstSize;

            srcW = dstW;
            srcH = dstH;
            if (srcW == 1 && srcH == 1)
            {
                break;
            }
        }
    }

    private static void GenerateLevel(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH, int bpp, int level)
    {
        GenerateLevelScalar(src, srcW, srcH, dst, dstW, dstH, bpp, level);
    }

    private static void GenerateLevelScalar(ReadOnlySpan<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH, int bpp, int level)
    {
        int srcW0 = Math.Max(1, srcW >> (level - 1));
        int srcH0 = Math.Max(1, srcH >> (level - 1));
        int srcStride = srcW0 * bpp;
        int dstStride = dstW * bpp;

        for (int y = 0; y < dstH; y++)
        {
            int srcY0 = y * 2;
            int srcY1 = Math.Min(srcY0 + 1, srcH0 - 1);
            int dstY = y;
            int dstRowOffset = dstY * dstStride;
            int srcRow0Offset = srcY0 * srcStride;
            int srcRow1Offset = srcY1 * srcStride;

            int x = 0;

            if (bpp == 4)
            {
                for (int limit = dstW - 1; x < limit; x++)
                {
                    int srcX0 = x * 2 * bpp;
                    int srcX1 = srcX0 + bpp;

                    dst[dstRowOffset + x * bpp] = (byte)((src[srcRow0Offset + srcX0] + src[srcRow0Offset + srcX1] + src[srcRow1Offset + srcX0] + src[srcRow1Offset + srcX1]) >> 2);
                    dst[dstRowOffset + x * bpp + 1] = (byte)((src[srcRow0Offset + srcX0 + 1] + src[srcRow0Offset + srcX1 + 1] + src[srcRow1Offset + srcX0 + 1] + src[srcRow1Offset + srcX1 + 1]) >> 2);
                    dst[dstRowOffset + x * bpp + 2] = (byte)((src[srcRow0Offset + srcX0 + 2] + src[srcRow0Offset + srcX1 + 2] + src[srcRow1Offset + srcX0 + 2] + src[srcRow1Offset + srcX1 + 2]) >> 2);
                    dst[dstRowOffset + x * bpp + 3] = (byte)((src[srcRow0Offset + srcX0 + 3] + src[srcRow0Offset + srcX1 + 3] + src[srcRow1Offset + srcX0 + 3] + src[srcRow1Offset + srcX1 + 3]) >> 2);
                }
            }

            for (; x < dstW; x++)
            {
                int srcX0 = x * 2 * bpp;
                int srcX1 = Math.Min(srcX0 + bpp, srcW0 * bpp - bpp);

                for (int ch = 0; ch < bpp; ch++)
                {
                    int p00 = src[srcRow0Offset + srcX0 + ch];
                    int p10 = src[srcRow0Offset + srcX1 + ch];
                    int p01 = src[srcRow1Offset + srcX0 + ch];
                    int p11 = src[srcRow1Offset + srcX1 + ch];
                    int avg = (p00 + p10 + p01 + p11) >> 2;
                    dst[dstRowOffset + x * bpp + ch] = (byte)avg;
                }
            }
        }
    }
}
