using System.Runtime.CompilerServices;
using Etch.Scene;

namespace Etch.Compose.Coverage;

/// <summary>
/// Exact-area antialiased polygon rasterizer. Every edge deposits its signed area into a per-pixel
/// accumulation buffer (the "signed area / cover" formulation used by font-rs, stb_truetype 2 and
/// Vello's fine rasterizer); a row-wise prefix sum then gives each pixel's winding-weighted
/// coverage. Coverage is exact for any pixel crossed by non-overlapping edges, in both axes —
/// unlike a one-sample-per-row scanline, thin horizontal features are not lost.
/// </summary>
/// <remarks>
/// Fill rules map the accumulated value <c>a</c>: non-zero → <c>min(|a|, 1)</c>; even-odd → a
/// triangle wave of <c>|a|</c> with period 2. Both are exact away from pixels where edges of
/// different contours overlap, and the standard approximation inside them.
/// </remarks>
public sealed class CoverageRasterizer
{
    private float[] accumulation = new float[1024];
    private int width;
    private int height;
    private int stride;

    /// <summary>Mask width in pixels.</summary>
    public int Width => width;

    /// <summary>Mask height in pixels.</summary>
    public int Height => height;

    /// <summary>Clears the accumulation buffer for a mask of the given size.</summary>
    public void Reset(int maskWidth, int maskHeight)
    {
        if (maskWidth <= 0 || maskHeight <= 0)
        {
            Panic.Invariant(PanicCodes.InvalidSurfaceSize, "Coverage mask dimensions must be positive");
        }
        width = maskWidth;
        height = maskHeight;
        // Two spare columns: an edge clamped to the right border deposits into column width.
        stride = maskWidth + 2;
        int needed = stride * maskHeight;
        if (accumulation.Length < needed)
        {
            accumulation = new float[Math.Max(needed, accumulation.Length * 2)];
        }
        accumulation.AsSpan(0, needed).Clear();
    }

    /// <summary>
    /// Adds every contour of <paramref name="path"/> as a closed polygon, offset by
    /// (<paramref name="offsetX"/>, <paramref name="offsetY"/>) into mask space.
    /// </summary>
    public void AddPath(FlatPath path, float offsetX, float offsetY)
    {
        ArgumentNullException.ThrowIfNull(path);
        var xs = path.X;
        var ys = path.Y;
        foreach (var contour in path.Contours)
        {
            if (contour.Count < 2)
            {
                continue;
            }
            int end = contour.Start + contour.Count;
            float px = xs[end - 1] + offsetX;
            float py = ys[end - 1] + offsetY;
            for (int i = contour.Start; i < end; i++)
            {
                float x = xs[i] + offsetX;
                float y = ys[i] + offsetY;
                AddLine(px, py, x, y);
                px = x;
                py = y;
            }
        }
    }

    /// <summary>Adds one directed edge in mask coordinates (pixel (0,0) spans [0,1]²).</summary>
    public void AddLine(float x0, float y0, float x1, float y1)
    {
        if (y0 == y1 || !float.IsFinite(x0) || !float.IsFinite(y0) || !float.IsFinite(x1) || !float.IsFinite(y1))
        {
            return;
        }

        float dir = 1f;
        if (y0 > y1)
        {
            dir = -1f;
            (x0, x1) = (x1, x0);
            (y0, y1) = (y1, y0);
        }

        // Clip vertically to the mask: an edge outside contributes nothing to any pixel inside.
        if (y1 <= 0f || y0 >= height)
        {
            return;
        }
        float dxdy = (x1 - x0) / (y1 - y0);
        if (y0 < 0f)
        {
            x0 -= y0 * dxdy;
            y0 = 0f;
        }
        if (y1 > height)
        {
            x1 -= (y1 - height) * dxdy;
            y1 = height;
        }

        float x = x0;
        int yStart = (int)y0;
        int yEnd = Math.Min(height, (int)MathF.Ceiling(y1));
        var acc = accumulation;
        for (int y = yStart; y < yEnd; y++)
        {
            int row = y * stride;
            float rowTop = Math.Max(y, y0);
            float rowBottom = Math.Min(y + 1, y1);
            float dy = rowBottom - rowTop;
            float xNext = x + dxdy * dy;
            float d = dy * dir;

            // Edges left of the mask still change the winding of every pixel to their right: clamp
            // them onto the left border. Right of the mask, onto the spare column.
            float xa = Math.Clamp(x, 0f, width);
            float xb = Math.Clamp(xNext, 0f, width);
            float lo = Math.Min(xa, xb);
            float hi = Math.Max(xa, xb);
            int loFloor = (int)lo;
            int hiCeil = (int)MathF.Ceiling(hi);

            if (hiCeil <= loFloor + 1)
            {
                // Entirely within one pixel column: area split by the average x.
                float mid = 0.5f * (xa + xb) - loFloor;
                acc[row + loFloor] += d - d * mid;
                acc[row + loFloor + 1] += d * mid;
            }
            else
            {
                float s = 1f / (hi - lo);
                float loFrac = lo - loFloor;
                float a0 = 0.5f * s * (1f - loFrac) * (1f - loFrac);
                float hiFrac = hi - hiCeil + 1f;
                float am = 0.5f * s * hiFrac * hiFrac;
                acc[row + loFloor] += d * a0;
                if (hiCeil == loFloor + 2)
                {
                    acc[row + loFloor + 1] += d * (1f - a0 - am);
                }
                else
                {
                    float a1 = s * (1.5f - loFrac);
                    acc[row + loFloor + 1] += d * (a1 - a0);
                    for (int xi = loFloor + 2; xi < hiCeil - 1; xi++)
                    {
                        acc[row + xi] += d * s;
                    }
                    float a2 = a1 + (hiCeil - loFloor - 3) * s;
                    acc[row + hiCeil - 1] += d * (1f - a2 - am);
                }
                acc[row + hiCeil] += d * am;
            }
            x = xNext;
        }
    }

    /// <summary>
    /// Resolves the accumulated areas to 8-bit coverage, row-major with stride <see cref="Width"/>.
    /// Coverage rounds to nearest (0.5 → 128).
    /// </summary>
    public void Resolve(Span<byte> coverage, FillRule rule)
    {
        if (coverage.Length < width * height)
        {
            Panic.Invariant(PanicCodes.BufferOverflow, "Coverage destination smaller than the mask");
        }
        var acc = accumulation;
        bool evenOdd = rule == FillRule.EvenOdd;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            int dst = y * width;
            float sum = 0f;
            for (int x = 0; x < width; x++)
            {
                sum += acc[row + x];
                coverage[dst + x] = ToByte(evenOdd ? EvenOdd(sum) : NonZero(sum));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float NonZero(float a) => Math.Min(Math.Abs(a), 1f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float EvenOdd(float a)
    {
        float v = Math.Abs(a);
        v -= 2f * MathF.Floor(v * 0.5f);
        return v > 1f ? 2f - v : v;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ToByte(float c) => (byte)(Math.Clamp(c, 0f, 1f) * 255f + 0.5f);
}
