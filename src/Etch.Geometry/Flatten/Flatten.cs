using System;
using System.Runtime.CompilerServices;

namespace Etch.Geometry.Flatten;

public static class CurveFlattener
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void QuadBez(in QuadBez q, double tolerance, ref FlattenSink sink)
    {
        sink.Accept(q.P0);
        if (QuadFlatness(q) <= tolerance)
        {
            sink.Accept(q.P2);
            return;
        }
        FlattenQuadRecursive(q, tolerance, ref sink);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CubicBez(in CubicBez c, double tolerance, ref FlattenSink sink)
    {
        sink.Accept(c.P0);
        if (CubicFlatness(c) <= tolerance)
        {
            sink.Accept(c.P3);
            return;
        }
        FlattenCubicRecursive(c, tolerance, ref sink);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenQuadRecursive(in QuadBez q, double tolerance, ref FlattenSink sink)
    {
        var (leftQ, rightQ) = q.Subdivide(0.5);
        if (QuadFlatness(leftQ) <= tolerance)
        {
            sink.Accept(leftQ.P2);
        }
        else
        {
            FlattenQuadRecursive(leftQ, tolerance, ref sink);
        }
        if (QuadFlatness(rightQ) <= tolerance)
        {
            sink.Accept(rightQ.P2);
        }
        else
        {
            FlattenQuadRecursive(rightQ, tolerance, ref sink);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenCubicRecursive(in CubicBez c, double tolerance, ref FlattenSink sink)
    {
        var (leftC, rightC) = c.Subdivide(0.5);
        if (CubicFlatness(leftC) <= tolerance)
        {
            sink.Accept(leftC.P3);
        }
        else
        {
            FlattenCubicRecursive(leftC, tolerance, ref sink);
        }
        if (CubicFlatness(rightC) <= tolerance)
        {
            sink.Accept(rightC.P3);
        }
        else
        {
            FlattenCubicRecursive(rightC, tolerance, ref sink);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double QuadFlatness(in QuadBez q)
    {
        double d = DistToLine(q.P1, q.P0, q.P2);
        return d;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CubicFlatness(in CubicBez c)
    {
        double d1 = DistToLine(c.P1, c.P0, c.P3);
        double d2 = DistToLine(c.P2, c.P0, c.P3);
        return Math.Max(d1, d2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DistToLine(Point pt, Point lineStart, Point lineEnd)
    {
        Vec2 line = lineEnd - lineStart;
        double lineLenSq = line.LengthSquared;
        if (lineLenSq < 1e-20) return (pt - lineStart).Length;
        Vec2 diff = pt - lineStart;
        double t = Math.Max(0, Math.Min(1, diff.Dot(line) / lineLenSq));
        Point projection = lineStart + line * t;
        return (pt - projection).Length;
    }

    public static void BezPath(in BezPath path, double tolerance, ref FlattenSink sink)
    {
        bool firstPointEmitted = false;
        Point subpathStart = new Point(0, 0);
        foreach (PathSegment seg in path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    subpathStart = seg.End;
                    sink.Accept(seg.End);
                    firstPointEmitted = true;
                    break;
                case PathVerb.LineTo:
                    sink.Accept(seg.End);
                    firstPointEmitted = true;
                    break;
                case PathVerb.QuadTo:
                    {
                        QuadBez q = new QuadBez(seg.Start, seg.Control0, seg.End);
                        FlattenQuadBezIntoPath(q, tolerance, ref sink, ref firstPointEmitted);
                    }
                    break;
                case PathVerb.CubicTo:
                    {
                        CubicBez c = new CubicBez(seg.Start, seg.Control0, seg.Control1, seg.End);
                        FlattenCubicBezIntoPath(c, tolerance, ref sink, ref firstPointEmitted);
                    }
                    break;
                case PathVerb.Close:
                    if (seg.End != subpathStart)
                    {
                        sink.Accept(subpathStart);
                    }
                    break;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenQuadBezIntoPath(in QuadBez q, double tolerance, ref FlattenSink sink, ref bool firstPointEmitted)
    {
        double dev = QuadFlatness(q);
        if (dev <= tolerance)
        {
            sink.Accept(q.P2);
            firstPointEmitted = true;
            return;
        }

        if (!firstPointEmitted)
        {
            sink.Accept(q.P0);
            firstPointEmitted = true;
        }

        var (leftQ, rightQ) = q.Subdivide(0.5);
        if (QuadFlatness(leftQ) <= tolerance)
        {
            sink.Accept(leftQ.P2);
        }
        else
        {
            sink.Accept(leftQ.P0);
            FlattenQuadBezIntoPathRecursive(leftQ, tolerance, ref sink);
        }
        if (QuadFlatness(rightQ) <= tolerance)
        {
            sink.Accept(rightQ.P2);
        }
        else
        {
            sink.Accept(rightQ.P0);
            FlattenQuadBezIntoPathRecursive(rightQ, tolerance, ref sink);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenQuadBezIntoPathRecursive(in QuadBez q, double tolerance, ref FlattenSink sink)
    {
        var (leftQ, rightQ) = q.Subdivide(0.5);
        if (QuadFlatness(leftQ) <= tolerance)
        {
            sink.Accept(leftQ.P2);
        }
        else
        {
            FlattenQuadBezIntoPathRecursive(leftQ, tolerance, ref sink);
        }
        if (QuadFlatness(rightQ) <= tolerance)
        {
            sink.Accept(rightQ.P2);
        }
        else
        {
            FlattenQuadBezIntoPathRecursive(rightQ, tolerance, ref sink);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenCubicBezIntoPath(in CubicBez c, double tolerance, ref FlattenSink sink, ref bool firstPointEmitted)
    {
        double dev = CubicFlatness(c);
        if (dev <= tolerance)
        {
            sink.Accept(c.P3);
            firstPointEmitted = true;
            return;
        }

        if (!firstPointEmitted)
        {
            sink.Accept(c.P0);
            firstPointEmitted = true;
        }

        var (leftC, rightC) = c.Subdivide(0.5);
        if (CubicFlatness(leftC) <= tolerance)
        {
            sink.Accept(leftC.P3);
        }
        else
        {
            FlattenCubicBezIntoPathRecursive(leftC, tolerance, ref sink);
        }
        if (CubicFlatness(rightC) <= tolerance)
        {
            sink.Accept(rightC.P3);
        }
        else
        {
            FlattenCubicBezIntoPathRecursive(rightC, tolerance, ref sink);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenCubicBezIntoPathRecursive(in CubicBez c, double tolerance, ref FlattenSink sink)
    {
        var (leftC, rightC) = c.Subdivide(0.5);
        if (CubicFlatness(leftC) <= tolerance)
        {
            sink.Accept(leftC.P3);
        }
        else
        {
            FlattenCubicBezIntoPathRecursive(leftC, tolerance, ref sink);
        }
        if (CubicFlatness(rightC) <= tolerance)
        {
            sink.Accept(rightC.P3);
        }
        else
        {
            FlattenCubicBezIntoPathRecursive(rightC, tolerance, ref sink);
        }
    }
}