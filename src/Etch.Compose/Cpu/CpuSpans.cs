namespace Etch.Compose.Cpu;

/// <summary>
/// Conservative per-row spans where a shape's or clip's coverage is exactly 1 (its interior) or
/// exactly 0 (a border's or ring's hole). Pixels in such a span skip the coverage function; every
/// other pixel evaluates it, so the image is identical to evaluating it everywhere.
/// </summary>
/// <remarks>
/// Spans are shrunk by <see cref="Margin"/> device pixels inside the analytic boundary, where the
/// antialiasing ramp <c>clamp(0.5 − d)</c> is saturated by a full pixel, so float rounding in the
/// coverage functions cannot change the outcome. Only axis-aligned frames (no rotation or
/// reflection) produce spans; everything else takes the per-pixel path.
/// </remarks>
internal static class CpuSpans
{
    /// <summary>Device pixels between a span's end and the analytic edge.</summary>
    public const float Margin = 1.5f;

    /// <summary>Whether <paramref name="inst"/>'s frame is a positive axis-aligned scale and offset.</summary>
    public static bool IsAxisAligned(in ShapeInstance inst)
        => inst.FrameB == 0f && inst.FrameC == 0f && inst.FrameA > 0f && inst.FrameD > 0f;

    /// <summary>
    /// The pixels of row <paramref name="y"/> where <paramref name="inst"/>'s coverage is exactly 1,
    /// as [first, end); empty when there are none or the shape has no interior.
    /// </summary>
    public static void Interior(in ShapeInstance inst, int y, out int first, out int end)
    {
        first = 0;
        end = 0;
        float ly = inst.FrameD * (y + 0.5f) + inst.FrameTy;
        float k = Margin / inst.Scale;
        switch (inst.Type)
        {
            case ShapeType.Rect:
                RectRow(inst, ly, inst.P0 + k, inst.P1 + k, inst.P2 - k, inst.P3 - k, out first, out end);
                return;
            case ShapeType.RoundedRect:
                RoundedRow(inst, ly, inst.P0, inst.P1, inst.P2, inst.P3, inst.Q0, k, out first, out end);
                return;
            case ShapeType.Circle:
                CircleRow(inst, ly, inst.P0, inst.P1, inst.P2 - k, out first, out end);
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// The pixels of row <paramref name="y"/> where <paramref name="inst"/>'s coverage is exactly 0
    /// although they lie inside its raster extent (a border's or ring's hole), as [first, end).
    /// </summary>
    public static void Hole(in ShapeInstance inst, int y, out int first, out int end)
    {
        first = 0;
        end = 0;
        float ly = inst.FrameD * (y + 0.5f) + inst.FrameTy;
        float k = Margin / inst.Scale;
        switch (inst.Type)
        {
            case ShapeType.Border:
                {
                    float w = inst.Q1;
                    RoundedRow(inst, ly, inst.P0 + w, inst.P1 + w, inst.P2 - w, inst.P3 - w, inst.Q2, k, out first, out end);
                    return;
                }
            case ShapeType.Ring:
                CircleRow(inst, ly, inst.P0, inst.P1, inst.P2 - inst.P3 - k, out first, out end);
                return;
            default:
                return;
        }
    }

    /// <summary>
    /// The pixels of row <paramref name="y"/> where <paramref name="clip"/>'s coverage is exactly 1,
    /// as [first, end). Rows outside the hard rect, and mask clips, give an empty span.
    /// </summary>
    public static void ClipInterior(in ClipEntry clip, int y, out int first, out int end)
    {
        first = 0;
        end = 0;
        float py = y + 0.5f;
        if (clip.HasMask != 0 || py < clip.MinY || py >= clip.MaxY)
        {
            return;
        }
        // The hard rect's own test, pixel centre in [Min, Max).
        int a = (int)MathF.Ceiling(clip.MinX - 0.5f);
        int b = (int)MathF.Ceiling(clip.MaxX - 0.5f);
        while (a < b && !(a + 0.5f >= clip.MinX))
        {
            a++;
        }
        while (b > a && !(b - 0.5f < clip.MaxX))
        {
            b--;
        }
        if (clip.HasRound != 0)
        {
            RoundedDeviceRow(py, clip.RoundMinX, clip.RoundMinY, clip.RoundMaxX, clip.RoundMaxY, clip.RoundRadius, Margin, out int ra, out int rb);
            a = Math.Max(a, ra);
            b = Math.Min(b, rb);
        }
        if (b > a)
        {
            first = a;
            end = b;
        }
    }

    // Row of the set { local x : lo.x ≤ x ≤ hi.x } on rows lo.y ≤ ly ≤ hi.y, in device pixels.
    private static void RectRow(in ShapeInstance inst, float ly, float loX, float loY, float hiX, float hiY, out int first, out int end)
    {
        first = 0;
        end = 0;
        if (ly < loY || ly > hiY || hiX < loX)
        {
            return;
        }
        LocalToPixels(inst, loX, hiX, out first, out end);
    }

    // Row of the rounded rect shrunk by k (its { sdf ≤ −k } set), in device pixels.
    private static void RoundedRow(in ShapeInstance inst, float ly, float loX, float loY, float hiX, float hiY, float radius, float k, out int first, out int end)
    {
        first = 0;
        end = 0;
        if (!RoundedHalfWidth(ly, loX, loY, hiX, hiY, radius, k, out float cx, out float half))
        {
            return;
        }
        LocalToPixels(inst, cx - half, cx + half, out first, out end);
    }

    private static void RoundedDeviceRow(float py, float loX, float loY, float hiX, float hiY, float radius, float k, out int first, out int end)
    {
        first = 0;
        end = 0;
        if (!RoundedHalfWidth(py, loX, loY, hiX, hiY, radius, k, out float cx, out float half))
        {
            return;
        }
        first = (int)MathF.Ceiling(cx - half - 0.5f);
        end = (int)MathF.Floor(cx + half - 0.5f) + 1;
    }

    // The inset { sdf ≤ −k } of a rounded rect is a rounded rect shrunk by k with radius
    // max(r − k, 0); its half-width on the row at distance |y − cy| from the centre.
    private static bool RoundedHalfWidth(float y, float loX, float loY, float hiX, float hiY, float radius, float k, out float cx, out float half)
    {
        cx = (loX + hiX) * 0.5f;
        float cy = (loY + hiY) * 0.5f;
        float hx0 = (hiX - loX) * 0.5f;
        float hy0 = (hiY - loY) * 0.5f;
        half = 0f;
        // The SDF is a rounded rect only for 0 ≤ r ≤ the half extents; leave odd radii to the per-pixel path.
        if (!(radius >= 0f) || radius > MathF.Min(hx0, hy0))
        {
            return false;
        }
        float hx = hx0 - k;
        float hy = hy0 - k;
        float r = MathF.Max(radius - k, 0f);
        float dy = MathF.Abs(y - cy);
        if (hx <= 0f || hy <= 0f || dy > hy)
        {
            return false;
        }
        float straight = hy - r;
        if (dy <= straight)
        {
            half = hx;
            return true;
        }
        float e = dy - straight;
        half = hx - r + MathF.Sqrt(MathF.Max(r * r - e * e, 0f));
        return half > 0f;
    }

    private static void CircleRow(in ShapeInstance inst, float ly, float cx, float cy, float radius, out int first, out int end)
    {
        first = 0;
        end = 0;
        float dy = ly - cy;
        if (radius <= 0f || MathF.Abs(dy) >= radius)
        {
            return;
        }
        float half = MathF.Sqrt(radius * radius - dy * dy);
        LocalToPixels(inst, cx - half, cx + half, out first, out end);
    }

    // Local x-range → device pixels whose centres lie inside it.
    private static void LocalToPixels(in ShapeInstance inst, float lo, float hi, out int first, out int end)
    {
        float pxLo = (lo - inst.FrameTx) / inst.FrameA;
        float pxHi = (hi - inst.FrameTx) / inst.FrameA;
        first = (int)MathF.Ceiling(pxLo - 0.5f);
        end = (int)MathF.Floor(pxHi - 0.5f) + 1;
        if (end < first)
        {
            end = first;
        }
    }
}
