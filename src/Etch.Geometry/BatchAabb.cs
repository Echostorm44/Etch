using System;
using System.Runtime.CompilerServices;

namespace Etch.Geometry;

public static class BatchAabb
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect OfPoints(ReadOnlySpan<Point> pts)
    {
        if (pts.IsEmpty)
            return Rect.Empty;

        double minX = pts[0].X;
        double maxX = pts[0].X;
        double minY = pts[0].Y;
        double maxY = pts[0].Y;

        for (int i = 1; i < pts.Length; i++)
        {
            double x = pts[i].X;
            double y = pts[i].Y;
            if (x < minX) minX = x;
            else if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            else if (y > maxY) maxY = y;
        }

        return new Rect(minX, minY, maxX, maxY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rect OfPointsTransformed(Affine a, ReadOnlySpan<Point> pts)
    {
        if (pts.IsEmpty)
            return Rect.Empty;

        double x0 = pts[0].X;
        double y0 = pts[0].Y;
        double minX = a.M00 * x0 + a.M01 * y0 + a.M02;
        double maxX = minX;
        double minY = a.M10 * x0 + a.M11 * y0 + a.M12;
        double maxY = minY;

        for (int i = 1; i < pts.Length; i++)
        {
            double x = pts[i].X;
            double y = pts[i].Y;
            double tx = a.M00 * x + a.M01 * y + a.M02;
            double ty = a.M10 * x + a.M11 * y + a.M12;
            if (tx < minX) minX = tx;
            else if (tx > maxX) maxX = tx;
            if (ty < minY) minY = ty;
            else if (ty > maxY) maxY = ty;
        }

        return new Rect(minX, minY, maxX, maxY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OfCurves(ReadOnlySpan<CubicBez> curves, Span<Rect> outAabbs)
    {
        if (curves.Length != outAabbs.Length)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.SpanLengthMismatch,
                $"curves.Length ({curves.Length}) != outAabbs.Length ({outAabbs.Length})");
        }

        for (int i = 0; i < curves.Length; i++)
        {
            outAabbs[i] = curves[i].Aabb();
        }
    }
}
