using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Etch.Geometry;

namespace Etch.Correctness.Tests.Differential;

/// <summary>
/// Hausdorff distance between two polylines. Used to compare flattening outputs.
/// </summary>
internal static class Hausdorff
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(ReadOnlySpan<Point> polyA, ReadOnlySpan<Point> polyB)
    {
        if (polyA.Length == 0 || polyB.Length == 0)
            return double.MaxValue;

        double maxMinDist = 0.0;

        for (int i = 0; i < polyA.Length; i++)
        {
            double minDist = double.MaxValue;
            for (int j = 0; j < polyB.Length; j++)
            {
                double d = DistancePointToSegment(polyA[i], polyB[j], polyB[(j + 1) % polyB.Length]);
                if (d < minDist) minDist = d;
            }
            if (minDist > maxMinDist) maxMinDist = minDist;
        }

        for (int j = 0; j < polyB.Length; j++)
        {
            double minDist = double.MaxValue;
            for (int i = 0; i < polyA.Length; i++)
            {
                double d = DistancePointToSegment(polyB[j], polyA[i], polyA[(i + 1) % polyA.Length]);
                if (d < minDist) minDist = d;
            }
            if (minDist > maxMinDist) maxMinDist = minDist;
        }

        return maxMinDist;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DistancePointToSegment(Point pt, Point segStart, Point segEnd)
    {
        Vec2 ab = segEnd - segStart;
        double abLenSq = ab.LengthSquared;
        if (abLenSq < 1e-20) return pt.DistanceTo(segStart);
        Vec2 ap = pt - segStart;
        double t = Math.Max(0, Math.Min(1, ap.Dot(ab) / abLenSq));
        Point closest = segStart + ab * t;
        return pt.DistanceTo(closest);
    }
}
