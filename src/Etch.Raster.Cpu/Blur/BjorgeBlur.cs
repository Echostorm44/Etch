using System;
using System.Buffers;
using System.Numerics;
using Etch.Effects.Blur;

namespace Etch.Raster.Cpu.Blur;

/// <summary>
/// Dual-filter blur (Bjørge, SIGGRAPH 2015): downsample through a chain of half-size levels, then
/// upsample back with a 3x3 tent. The CPU reference for the GPU blur; both use
/// <see cref="BlurTaps"/> and texel-exact addressing, so they agree to rounding.
/// </summary>
public static class BjorgeBlur
{
    /// <summary>Blurs <paramref name="src"/> into <paramref name="dst"/> (same size).</summary>
    public static void Blur(Framebuffer src, Framebuffer dst, float radiusPx)
    {
        if (src.Width != dst.Width || src.Height != dst.Height)
        {
            Panic.ArgumentOutOfRange(nameof(dst), "dst must be the same size as src");
        }

        int octaves = EffectiveOctaves(radiusPx, src.Width, src.Height);
        if (octaves == 0)
        {
            CopyRows(src, dst);
            return;
        }

        // levels[0] is the source; levels[k] is half of levels[k-1]. The down results are consumed
        // on the way back up, so each up pass writes into the level it lands on.
        var levels = new Framebuffer[octaves + 1];
        var rented = new Rgba16f[octaves][];
        levels[0] = src;
        try
        {
            for (int k = 1; k <= octaves; k++)
            {
                int w = Math.Max(1, levels[k - 1].Width / 2);
                int h = Math.Max(1, levels[k - 1].Height / 2);
                rented[k - 1] = ArrayPool<Rgba16f>.Shared.Rent(w * h);
                levels[k] = new Framebuffer(w, h, w, rented[k - 1].AsMemory(0, w * h));
                DownsampleLevel(levels[k - 1], levels[k]);
            }

            for (int k = octaves; k >= 1; k--)
            {
                UpsampleLevel(levels[k], k == 1 ? dst : levels[k - 1]);
            }
        }
        finally
        {
            foreach (var array in rented)
            {
                if (array is not null)
                {
                    ArrayPool<Rgba16f>.Shared.Return(array);
                }
            }
        }
    }

    /// <summary>Octaves run for <paramref name="radiusPx"/> on a <paramref name="width"/> x <paramref name="height"/> image.</summary>
    public static int EffectiveOctaves(float radiusPx, int width, int height) => DualFilterBlur.EffectiveOctaves(radiusPx, width, height);

    private static void CopyRows(Framebuffer src, Framebuffer dst)
    {
        for (int y = 0; y < src.Height; y++)
        {
            src.RowSpan(y).Slice(0, src.Width).CopyTo(dst.RowSpan(y));
        }
    }

    // dst[x, y] = average of src[2x..2x+1, 2y..2y+1], clamped at the edge for odd sizes.
    private static void DownsampleLevel(Framebuffer src, Framebuffer dst)
    {
        int maxX = src.Width - 1;
        int maxY = src.Height - 1;
        for (int y = 0; y < dst.Height; y++)
        {
            var row0 = src.RowSpan(Math.Min(2 * y, maxY));
            var row1 = src.RowSpan(Math.Min(2 * y + 1, maxY));
            var dstRow = dst.RowSpan(y);
            for (int x = 0; x < dst.Width; x++)
            {
                int x0 = Math.Min(2 * x, maxX);
                int x1 = Math.Min(2 * x + 1, maxX);
                dstRow[x] = ToPixel((V(row0[x0]) + V(row0[x1]) + V(row1[x0]) + V(row1[x1])) * BlurTaps.DownWeight);
            }
        }
    }

    // dst[x, y] = tent over the 3x3 source texels around src[x/2, y/2], clamped at the edges.
    private static void UpsampleLevel(Framebuffer src, Framebuffer dst)
    {
        int maxX = src.Width - 1;
        int maxY = src.Height - 1;
        for (int y = 0; y < dst.Height; y++)
        {
            int sy = Math.Min(y / 2, maxY);
            var top = src.RowSpan(Math.Max(sy - 1, 0));
            var mid = src.RowSpan(sy);
            var bottom = src.RowSpan(Math.Min(sy + 1, maxY));
            var dstRow = dst.RowSpan(y);
            for (int x = 0; x < dst.Width; x++)
            {
                int sx = Math.Min(x / 2, maxX);
                int l = Math.Max(sx - 1, 0);
                int r = Math.Min(sx + 1, maxX);

                Vector4 corners = V(top[l]) + V(top[r]) + V(bottom[l]) + V(bottom[r]);
                Vector4 edges = V(top[sx]) + V(mid[l]) + V(mid[r]) + V(bottom[sx]);
                dstRow[x] = ToPixel(corners * BlurTaps.UpCornerWeight + edges * BlurTaps.UpEdgeWeight + V(mid[sx]) * BlurTaps.UpCenterWeight);
            }
        }
    }

    // Sums are kept in 32-bit float and rounded to half once per texel, like the GPU shaders.
    private static Vector4 V(Rgba16f p) => new((float)p.R, (float)p.G, (float)p.B, (float)p.A);

    private static Rgba16f ToPixel(Vector4 v) => Rgba16f.From(v.X, v.Y, v.Z, v.W);
}
