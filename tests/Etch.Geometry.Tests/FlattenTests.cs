using System;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class FlattenTests
{
    [Test]
    public void QuadBez_EndpointExactness()
    {
        var q = new QuadBez(new Point(0, 0), new Point(1, 2), new Point(3, 0));
        Span<Point> buf = stackalloc Point[128];
        var sink = new FlattenSink(buf);
        CurveFlattener.QuadBez(q, 0.25, ref sink);

        if (sink.Count <= 1) throw new InvalidOperationException("Expected more than 1 segment");
        if (buf[0].X != 0.0 || buf[0].Y != 0.0)
            throw new InvalidOperationException($"First point mismatch: expected (0,0), got ({buf[0].X},{buf[0].Y})");
        if (buf[sink.Count - 1].X != 3.0 || buf[sink.Count - 1].Y != 0.0)
            throw new InvalidOperationException($"Last point mismatch: expected (3,0), got ({buf[sink.Count - 1].X},{buf[sink.Count - 1].Y})");
    }

    [Test]
    public void CubicBez_EndpointExactness()
    {
        var c = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));
        Span<Point> buf = stackalloc Point[128];
        var sink = new FlattenSink(buf);
        CurveFlattener.CubicBez(c, 0.25, ref sink);

        if (sink.Count <= 1) throw new InvalidOperationException("Expected more than 1 segment");
        if (buf[0].X != 0.0 || buf[0].Y != 0.0)
            throw new InvalidOperationException($"First point mismatch: expected (0,0), got ({buf[0].X},{buf[0].Y})");
        if (buf[sink.Count - 1].X != 3.0 || buf[sink.Count - 1].Y != 0.0)
            throw new InvalidOperationException($"Last point mismatch: expected (3,0), got ({buf[sink.Count - 1].X},{buf[sink.Count - 1].Y})");
    }

    [Test]
    public void QuadBez_MonotonicRefinement()
    {
        var q = new QuadBez(new Point(0, 0), new Point(1, 2), new Point(3, 0));

        int c1 = CountQuadAt(q, 0.5);
        int c2 = CountQuadAt(q, 0.25);
        int c3 = CountQuadAt(q, 0.125);

        if (c2 < c1) throw new InvalidOperationException($"c2({c2}) should be >= c1({c1})");
        if (c3 < c2) throw new InvalidOperationException($"c3({c3}) should be >= c2({c2})");
        if (c2 <= c1) throw new InvalidOperationException($"c2({c2}) should be > c1({c1})");
        if (c3 <= c2) throw new InvalidOperationException($"c3({c3}) should be > c2({c2})");
    }

    [Test]
    public void CubicBez_MonotonicRefinement()
    {
        var c = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));

        int c1 = CountCubicAt(c, 0.5);
        int c2 = CountCubicAt(c, 0.25);
        int c3 = CountCubicAt(c, 0.125);

        if (c2 < c1) throw new InvalidOperationException($"c2({c2}) should be >= c1({c1})");
        if (c3 < c2) throw new InvalidOperationException($"c3({c3}) should be >= c2({c2})");
    }

    [Test]
    public void CubicBez_StraightLine_OneSegment()
    {
        var c = new CubicBez(new Point(0, 0), new Point(1, 0), new Point(2, 0), new Point(3, 0));
        Span<Point> buf = stackalloc Point[128];
        var sink = new FlattenSink(buf);
        CurveFlattener.CubicBez(c, 0.1, ref sink);

        if (sink.Count != 2)
            throw new InvalidOperationException($"Straight line should produce 1 segment (2 points), got {sink.Count}");
    }

    [Test]
    public void QuadBez_StraightLine_OneSegment()
    {
        var q = new QuadBez(new Point(0, 0), new Point(1.5, 0), new Point(3, 0));
        Span<Point> buf = stackalloc Point[128];
        var sink = new FlattenSink(buf);
        CurveFlattener.QuadBez(q, 0.1, ref sink);

        if (sink.Count != 2)
            throw new InvalidOperationException($"Straight line should produce 1 segment (2 points), got {sink.Count}");
    }

    [Test]
    public void BezPath_DispatchesPerVerb()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(1, 1));
        builder.QuadTo(new Point(2, 2), new Point(3, 1));
        builder.CubicTo(new Point(4, 0), new Point(5, 1), new Point(6, 0));
        builder.Close();
        var path = builder.Build();

        Span<Point> buf = stackalloc Point[1024];
        var sink = new FlattenSink(buf);
        CurveFlattener.BezPath(path, 0.25, ref sink);

        if (sink.Count <= 1)
            throw new InvalidOperationException($"BezPath should produce more than 1 point, got {sink.Count}");
    }

    [Test]
    public void FlattenSink_Overflow_PanicsWithoutAutoflush()
    {
        Span<Point> tiny = stackalloc Point[1];
        var sink = new FlattenSink(tiny, autoflush: false);
        sink.Accept(new Point(0, 0));

        bool threw = false;
        try
        {
            sink.Accept(new Point(1, 1));
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.FlattenSinkOverflow)
        {
            threw = true;
        }

        if (!threw)
            throw new InvalidOperationException("Expected ET-P-0308 panic when sink overflows without autoflush");
    }

    [Test]
    public void FlattenSink_Overflow_SilentWithAutoflush()
    {
        Span<Point> tiny = stackalloc Point[1];
        var sink = new FlattenSink(tiny, autoflush: true);
        sink.Accept(new Point(0, 0));
        sink.Accept(new Point(1, 1));
        sink.Accept(new Point(2, 2));

        if (sink.Count != 1)
            throw new InvalidOperationException($"Autoflush sink should hold 1 point, got {sink.Count}");
    }

    [Test]
    public void QuadBez_ToleranceBound()
    {
        var q = new QuadBez(new Point(0, 0), new Point(1, 2), new Point(3, 0));
        CheckToleranceBoundQuad(q, 0.5);
        CheckToleranceBoundQuad(q, 0.25);
        CheckToleranceBoundQuad(q, 0.125);
    }

    [Test]
    public void CubicBez_ToleranceBound()
    {
        var c = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));
        CheckToleranceBoundCubic(c, 0.5);
        CheckToleranceBoundCubic(c, 0.25);
        CheckToleranceBoundCubic(c, 0.125);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountQuadAt(QuadBez q, double tolerance)
    {
        Span<Point> buf = stackalloc Point[1024];
        var sink = new FlattenSink(buf);
        CurveFlattener.QuadBez(q, tolerance, ref sink);
        return sink.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountCubicAt(CubicBez c, double tolerance)
    {
        Span<Point> buf = stackalloc Point[1024];
        var sink = new FlattenSink(buf);
        CurveFlattener.CubicBez(c, tolerance, ref sink);
        return sink.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckToleranceBoundQuad(QuadBez q, double tolerance)
    {
        Span<Point> buf = stackalloc Point[2048];
        var sink = new FlattenSink(buf);
        CurveFlattener.QuadBez(q, tolerance, ref sink);

        if (sink.Count < 2)
            throw new InvalidOperationException($"Sink has only {sink.Count} points, need at least 2 for segment check");

        double tStep = 0.001;
        for (double t = 0; t <= 1.0; t += tStep)
        {
            Point curvePt = q.Eval(t);
            double minDist = double.MaxValue;
            for (int j = 0; j < sink.Count - 1; j++)
            {
                double d = DistToSegment(curvePt, buf[j], buf[j + 1]);
                if (d < minDist) minDist = d;
            }
            if (minDist > tolerance * 1.01)
                throw new InvalidOperationException($"Curve point at t={t:F3} has distance {minDist:G} to polyline, exceeds tolerance {tolerance}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void CheckToleranceBoundCubic(CubicBez c, double tolerance)
    {
        Span<Point> buf = stackalloc Point[2048];
        var sink = new FlattenSink(buf);
        CurveFlattener.CubicBez(c, tolerance, ref sink);

        if (sink.Count < 2)
            throw new InvalidOperationException($"Sink has only {sink.Count} points, need at least 2 for segment check");

        double tStep = 0.001;
        for (double t = 0; t <= 1.0; t += tStep)
        {
            Point curvePt = c.Eval(t);
            double minDist = double.MaxValue;
            for (int j = 0; j < sink.Count - 1; j++)
            {
                double d = DistToSegment(curvePt, buf[j], buf[j + 1]);
                if (d < minDist) minDist = d;
            }
            if (minDist > tolerance * 1.01)
                throw new InvalidOperationException($"Curve point at t={t:F3} has distance {minDist:G} to polyline, exceeds tolerance {tolerance}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DistToSegment(Point pt, Point segStart, Point segEnd)
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