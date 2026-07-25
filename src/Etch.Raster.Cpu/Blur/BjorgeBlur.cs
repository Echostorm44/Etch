using System;
using Etch.Effects.Blur;

namespace Etch.Raster.Cpu.Blur;

public static class BjorgeBlur
{
    public static void Blur(Framebuffer src, Framebuffer dst, float radiusPx, Framebuffer scratchPing, Framebuffer scratchPong)
    {
        if (radiusPx <= 0f)
        {
            if (src.Pixels.Span.Length != dst.Pixels.Span.Length)
            {
                Panic.ArgumentOutOfRange(nameof(dst), "dst must be same size as src for identity blur");
            }
            src.Pixels.Span.CopyTo(dst.Pixels.Span);
            return;
        }

        int octaveCount = DualFilterBlur.OctaveCount(radiusPx);
        if (octaveCount == 0)
        {
            src.Pixels.Span.CopyTo(dst.Pixels.Span);
            return;
        }

        int numPasses = 2 * octaveCount;

        Framebuffer current = src;
        Framebuffer scratchA = scratchPing;
        Framebuffer scratchB = scratchPong;

        for (int pass = 0; pass < numPasses; pass++)
        {
            bool isDownPass = pass < octaveCount;
            bool isLastPass = pass == numPasses - 1;
            bool useScratchAAsDest = pass % 2 == 0;

            Framebuffer dest;
            if (isLastPass)
            {
                dest = dst;
            }
            else if (useScratchAAsDest)
            {
                dest = scratchA;
            }
            else
            {
                dest = scratchB;
            }

            if (isDownPass)
            {
                DownsampleLevel(current, dest);
            }
            else
            {
                UpsampleLevel(current, dest);
            }

            if (!isLastPass)
            {
                current = dest;
            }
        }
    }

    private static void DownsampleLevel(Framebuffer src, Framebuffer dst)
    {
        int srcW = src.Width;
        int srcH = src.Height;
        int dstW = dst.Width;
        int dstH = dst.Height;

        if (dstW != srcW / 2 || dstH != srcH / 2)
        {
            Panic.ArgumentOutOfRange(nameof(dst), "downsample destination must be half source size");
        }

        for (int y = 0; y < dstH; y++)
        {
            int srcY0 = y * 2;
            int srcY1 = Math.Min(srcY0 + 1, srcH - 1);
            Span<Rgba16f> dstRow = dst.RowSpan(y);

            for (int x = 0; x < dstW; x++)
            {
                int srcX0 = x * 2;
                int srcX1 = Math.Min(srcX0 + 1, srcW - 1);

                Half cR = src.RowSpan(srcY0)[srcX0].R;
                Half tlR = src.RowSpan(srcY0)[srcX1].R;
                Half blR = src.RowSpan(srcY1)[srcX0].R;
                Half brR = src.RowSpan(srcY1)[srcX1].R;

                Half cG = src.RowSpan(srcY0)[srcX0].G;
                Half tlG = src.RowSpan(srcY0)[srcX1].G;
                Half blG = src.RowSpan(srcY1)[srcX0].G;
                Half brG = src.RowSpan(srcY1)[srcX1].G;

                Half cB = src.RowSpan(srcY0)[srcX0].B;
                Half tlB = src.RowSpan(srcY0)[srcX1].B;
                Half blB = src.RowSpan(srcY1)[srcX0].B;
                Half brB = src.RowSpan(srcY1)[srcX1].B;

                Half cA = src.RowSpan(srcY0)[srcX0].A;
                Half tlA = src.RowSpan(srcY0)[srcX1].A;
                Half blA = src.RowSpan(srcY1)[srcX0].A;
                Half brA = src.RowSpan(srcY1)[srcX1].A;

                float avgR = ((float)cR + (float)tlR + (float)blR + (float)brR) * 0.25f;
                float avgG = ((float)cG + (float)tlG + (float)blG + (float)brG) * 0.25f;
                float avgB = ((float)cB + (float)tlB + (float)blB + (float)brB) * 0.25f;
                float avgA = ((float)cA + (float)tlA + (float)blA + (float)brA) * 0.25f;

                dstRow[x] = Rgba16f.From(avgR, avgG, avgB, avgA);
            }
        }
    }

    private static void UpsampleLevel(Framebuffer src, Framebuffer dst)
    {
        int srcW = src.Width;
        int srcH = src.Height;
        int dstW = dst.Width;
        int dstH = dst.Height;

        if (dstW != srcW * 2 || dstH != srcH * 2)
        {
            Panic.ArgumentOutOfRange(nameof(dst), "upsample destination must be double source size");
        }

        float cW = BlurTaps.UpCenterWeight;
        float eW = BlurTaps.UpEdgeWeight;
        float cC = BlurTaps.UpCornerWeight;

        for (int y = 0; y < dstH; y++)
        {
            int srcY = y / 2;
            int srcY1 = Math.Min(srcH - 1, srcY + 1);
            int srcY0 = Math.Max(0, srcY - 1);
            Span<Rgba16f> dstRow = dst.RowSpan(y);

            for (int x = 0; x < dstW; x++)
            {
                int srcX = x / 2;
                int srcX1 = Math.Min(srcW - 1, srcX + 1);
                int srcX0 = Math.Max(0, srcX - 1);

                float tlR = (float)src.RowSpan(srcY0)[srcX0].R;
                float tR = (float)src.RowSpan(srcY0)[srcX].R;
                float trR = (float)src.RowSpan(srcY0)[srcX1].R;
                float lR = (float)src.RowSpan(srcY)[srcX0].R;
                float cR = (float)src.RowSpan(srcY)[srcX].R;
                float rR = (float)src.RowSpan(srcY)[srcX1].R;
                float blR = (float)src.RowSpan(srcY1)[srcX0].R;
                float bR = (float)src.RowSpan(srcY1)[srcX].R;
                float brR = (float)src.RowSpan(srcY1)[srcX1].R;

                float tlG = (float)src.RowSpan(srcY0)[srcX0].G;
                float tG = (float)src.RowSpan(srcY0)[srcX].G;
                float trG = (float)src.RowSpan(srcY0)[srcX1].G;
                float lG = (float)src.RowSpan(srcY)[srcX0].G;
                float cG = (float)src.RowSpan(srcY)[srcX].G;
                float rG = (float)src.RowSpan(srcY)[srcX1].G;
                float blG = (float)src.RowSpan(srcY1)[srcX0].G;
                float bG = (float)src.RowSpan(srcY1)[srcX].G;
                float brG = (float)src.RowSpan(srcY1)[srcX1].G;

                float tlB = (float)src.RowSpan(srcY0)[srcX0].B;
                float tB = (float)src.RowSpan(srcY0)[srcX].B;
                float trB = (float)src.RowSpan(srcY0)[srcX1].B;
                float lB = (float)src.RowSpan(srcY)[srcX0].B;
                float cB = (float)src.RowSpan(srcY)[srcX].B;
                float rB = (float)src.RowSpan(srcY)[srcX1].B;
                float blB = (float)src.RowSpan(srcY1)[srcX0].B;
                float bB = (float)src.RowSpan(srcY1)[srcX].B;
                float brB = (float)src.RowSpan(srcY1)[srcX1].B;

                float tlA = (float)src.RowSpan(srcY0)[srcX0].A;
                float tA = (float)src.RowSpan(srcY0)[srcX].A;
                float trA = (float)src.RowSpan(srcY0)[srcX1].A;
                float lA = (float)src.RowSpan(srcY)[srcX0].A;
                float cA = (float)src.RowSpan(srcY)[srcX].A;
                float rA = (float)src.RowSpan(srcY)[srcX1].A;
                float blA = (float)src.RowSpan(srcY1)[srcX0].A;
                float bA = (float)src.RowSpan(srcY1)[srcX].A;
                float brA = (float)src.RowSpan(srcY1)[srcX1].A;

                float resultR = (tlR + trR + blR + brR) * cC + (tR + lR + rR + bR) * eW + cR * cW;
                float resultG = (tlG + trG + blG + brG) * cC + (tG + lG + rG + bG) * eW + cG * cW;
                float resultB = (tlB + trB + blB + brB) * cC + (tB + lB + rB + bB) * eW + cB * cW;
                float resultA = (tlA + trA + blA + brA) * cC + (tA + lA + rA + bA) * eW + cA * cW;

                dstRow[x] = Rgba16f.From(resultR, resultG, resultB, resultA);
            }
        }
    }
}
