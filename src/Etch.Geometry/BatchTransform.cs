using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Primitives;

namespace Etch.Geometry;

public static class BatchTransform
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransformPoints(Affine a, ReadOnlySpan<Point> src, Span<Point> dst)
    {
        if (src.Length != dst.Length)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.SpanLengthMismatch,
                $"src.Length ({src.Length}) != dst.Length ({dst.Length})");
        }

        for (int i = 0; i < src.Length; i++)
        {
            double x = src[i].X;
            double y = src[i].Y;
            dst[i] = new Point(a.M00 * x + a.M01 * y + a.M02, a.M10 * x + a.M11 * y + a.M12);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransformVec2(Affine a, ReadOnlySpan<Vec2> src, Span<Vec2> dst)
    {
        if (src.Length != dst.Length)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.SpanLengthMismatch,
                $"src.Length ({src.Length}) != dst.Length ({dst.Length})");
        }

        for (int i = 0; i < src.Length; i++)
        {
            double x = src[i].X;
            double y = src[i].Y;
            dst[i] = new Vec2(a.M00 * x + a.M01 * y, a.M10 * x + a.M11 * y);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void TransformInPlace(Affine a, Span<Point> pts)
    {
        TransformPoints(a, pts, pts);
    }
}
