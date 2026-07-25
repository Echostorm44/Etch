using System;
using System.Buffers;
using Etch.Geometry;
using Etch.Geometry.Flatten;

namespace Etch.Strokes;

public static class StrokeToFill
{
    public static BezPath Convert(BezPath input, Strokes.StrokeStyle style)
    {
        if (input.IsEmpty || style.Width <= 0f)
        {
            return input;
        }

        float halfWidth = style.Width * 0.5f;

        BezPath dashed = input;
        if (style.DashPattern != null && style.DashPattern.Length > 0)
        {
            var dashPattern = new DashPattern(style.DashPattern, style.DashOffset);
            var dashedBuilder = BezPathBuilder.Begin(input.VerbCount * 2);
            Dasher.Expand(input, dashPattern, ref dashedBuilder);
            dashed = dashedBuilder.Build();
        }

        Span<Point> scratch = stackalloc Point[2048];
        var flatSink = new FlattenSink(scratch);
        CurveFlattener.BezPath(dashed, 0.25, ref flatSink);

        ReadOnlySpan<Point> points = flatSink.Written;
        int count = points.Length;
        if (count < 2)
        {
            return dashed;
        }

        return BuildStrokedOutline(points, style, halfWidth);
    }

    private static BezPath BuildStrokedOutline(ReadOnlySpan<Point> points, Strokes.StrokeStyle style, float halfWidth)
    {
        int n = points.Length;
        var builder = BezPathBuilder.Begin(n * 4 + 10);

        Vec2 startTangent = ComputeTangentVec(points, 0, 1);
        Vec2 endTangent = ComputeTangentVec(points, n - 2, n - 1);

        Vec2 startNormal = new Vec2(-startTangent.Y, startTangent.X);
        Vec2 endNormal = new Vec2(-endTangent.Y, endTangent.X);

        builder.MoveTo(points[0] + startNormal * halfWidth);
        builder.LineTo(points[0] - startNormal * halfWidth);

        CapEmitter.EmitReverse(style.Cap, points[0], startTangent, halfWidth, ref builder, ref builder);

        for (int i = 1; i < n - 1; i++)
        {
            Vec2 endTan = ComputeTangentVec(points, i - 1, i);
            Vec2 startTan = ComputeTangentVec(points, i, i + 1);

            var joinOuter = BezPathBuilder.Begin(16);
            var joinInner = BezPathBuilder.Begin(16);
            joinOuter.MoveTo(points[i]);
            joinInner.MoveTo(points[i]);

            JoinEmitter.Emit(style.Join, endTan, startTan, halfWidth, style.MiterLimit, ref joinOuter, ref joinInner);

            foreach (var seg in joinOuter.Build().Iterate())
            {
                if (seg.Verb == PathVerb.LineTo) builder.LineTo(seg.End);
                else if (seg.Verb == PathVerb.CubicTo) builder.CubicTo(seg.Control0, seg.Control1, seg.End);
            }
        }

        builder.LineTo(points[n - 1] + endNormal * halfWidth);
        CapEmitter.Emit(style.Cap, points[n - 1], endTangent, halfWidth, ref builder, ref builder);
        builder.LineTo(points[n - 1] - endNormal * halfWidth);

        for (int i = n - 2; i >= 1; i--)
        {
            Vec2 normal = new Vec2(-ComputeTangentVec(points, i, i + 1).Y, ComputeTangentVec(points, i, i + 1).X);
            builder.LineTo(points[i] - normal * halfWidth);
        }

        builder.Close();
        return builder.Build();
    }

    private static Point ComputeTangent(ReadOnlySpan<Point> pts, int fromIdx, int toIdx)
    {
        Point from = pts[fromIdx];
        Point to = pts[toIdx];
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20) return new Point(1, 0);
        double invLen = 1.0 / Math.Sqrt(lenSq);
        return new Point(dx * invLen, dy * invLen);
    }

    private static Vec2 ComputeTangentVec(ReadOnlySpan<Point> pts, int fromIdx, int toIdx)
    {
        double dx = pts[toIdx].X - pts[fromIdx].X;
        double dy = pts[toIdx].Y - pts[fromIdx].Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-20) return new Vec2(1, 0);
        double invLen = 1.0 / Math.Sqrt(lenSq);
        return new Vec2(dx * invLen, dy * invLen);
    }
}