using System;
using System.Runtime.CompilerServices;

namespace Etch.Geometry;

public static class PathArithmetic
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ApproximateLength(BezPath path, double flattenTolerance = 0.25)
    {
        Span<Point> scratch = stackalloc Point[1024];
        return ApproximateLength(path, flattenTolerance, scratch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ApproximateLength(BezPath path, double flattenTolerance, Span<Point> scratch)
    {
        int count = FlattenInto(path, flattenTolerance, scratch);
        return PolylineLength(scratch[..count]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ApproximateLength(in CubicBez c, double flattenTolerance = 0.25)
    {
        Span<Point> scratch = stackalloc Point[1024];
        return ApproximateLength(c, flattenTolerance, scratch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ApproximateLength(in CubicBez c, double flattenTolerance, Span<Point> scratch)
    {
        int count = FlattenInto(c, flattenTolerance, scratch);
        return PolylineLength(scratch[..count]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ApproximateLength(in QuadBez q, double flattenTolerance = 0.25)
    {
        Span<Point> scratch = stackalloc Point[1024];
        return ApproximateLength(q, flattenTolerance, scratch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ApproximateLength(in QuadBez q, double flattenTolerance, Span<Point> scratch)
    {
        int count = FlattenInto(q, flattenTolerance, scratch);
        return PolylineLength(scratch[..count]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Point SampleAtLength(BezPath path, double arcLength, double flattenTolerance = 0.25)
    {
        Span<Point> scratch = stackalloc Point[1024];
        return SampleAtLength(path, arcLength, flattenTolerance, scratch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Point SampleAtLength(BezPath path, double arcLength, double flattenTolerance, Span<Point> scratch)
    {
        int count = FlattenInto(path, flattenTolerance, scratch);
        return SamplePointAtLength(scratch[..count], arcLength);
    }

    public static void SampleAtLengthsSorted(BezPath path, ReadOnlySpan<double> sortedLengths, Span<Point> output, double flattenTolerance = 0.25)
    {
        if (sortedLengths.Length != output.Length)
            Etch.Panic.Invariant(Etch.PanicCodes.SpanLengthMismatch, $"sortedLengths.Length ({sortedLengths.Length}) != output.Length ({output.Length})");

        for (int i = 1; i < sortedLengths.Length; i++)
        {
            if (sortedLengths[i] < sortedLengths[i - 1])
            {
                Etch.Panic.Invariant(Etch.PanicCodes.UnsortedLengths, $"sortedLengths[{i}]={sortedLengths[i]} < sortedLengths[{i - 1}]={sortedLengths[i - 1]}");
            }
        }

        Span<Point> scratch = stackalloc Point[1024];
        int count = FlattenInto(path, flattenTolerance, scratch);
        double totalLength = PolylineLength(scratch[..count]);

        for (int i = 0; i < sortedLengths.Length; i++)
        {
            double length = sortedLengths[i];
            if (length <= 0)
            {
                output[i] = scratch[0];
            }
            else if (length >= totalLength)
            {
                output[i] = scratch[count - 1];
            }
            else
            {
                output[i] = SamplePointAtLength(scratch[..count], length);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FlattenInto(BezPath path, double tolerance, Span<Point> scratch)
    {
        var sink = new Flatten.FlattenSink(scratch);
        Flatten.CurveFlattener.BezPath(path, tolerance, ref sink);
        return sink.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FlattenInto(in CubicBez c, double tolerance, Span<Point> scratch)
    {
        var sink = new Flatten.FlattenSink(scratch);
        Flatten.CurveFlattener.CubicBez(c, tolerance, ref sink);
        return sink.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FlattenInto(in QuadBez q, double tolerance, Span<Point> scratch)
    {
        var sink = new Flatten.FlattenSink(scratch);
        Flatten.CurveFlattener.QuadBez(q, tolerance, ref sink);
        return sink.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double PolylineLength(ReadOnlySpan<Point> pts)
    {
        if (pts.Length < 2)
            return 0;

        double sum = 0;
        for (int i = 1; i < pts.Length; i++)
        {
            sum += pts[i - 1].DistanceTo(pts[i]);
        }
        return sum;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point SamplePointAtLength(ReadOnlySpan<Point> pts, double length)
    {
        if (pts.Length < 2 || length <= 0)
            return pts[0];

        double cumLength = 0;
        for (int i = 1; i < pts.Length; i++)
        {
            double segLen = pts[i - 1].DistanceTo(pts[i]);
            if (cumLength + segLen >= length)
            {
                double t = (length - cumLength) / segLen;
                return pts[i - 1] + (pts[i] - pts[i - 1]) * t;
            }
            cumLength += segLen;
        }

        return pts[pts.Length - 1];
    }
}
