using System;
using Etch.Geometry;
using Etch.Geometry.Flatten;

namespace Etch.Strokes;

public static class Dasher
{
    public static void Expand(BezPath input, DashPattern pattern, ref BezPathBuilder output)
    {
        if (input.IsEmpty) return;

        double totalLength = PathArithmetic.ApproximateLength(input, 0.25);
        if (totalLength < 1e-10) return;

        Span<Point> scratch = stackalloc Point[2048];
        var flatSink = new FlattenSink(scratch);
        CurveFlattener.BezPath(input, 0.25, ref flatSink);
        ReadOnlySpan<Point> points = flatSink.Written;

        if (points.Length < 2) return;

        float patternLength = pattern.TotalLength();
        double position = pattern.Phase;

        var dashBuilder = BezPathBuilder.Begin(16);
        bool inDash = pattern.IsOnSegment(pattern.PhasePosition((float)position));

        for (int i = 1; i < points.Length; i++)
        {
            Point prev = points[i - 1];
            Point curr = points[i];
            double segLen = (curr - prev).Length;

            position += segLen;
            bool segEndsInDash = pattern.IsOnSegment(pattern.PhasePosition((float)position));

            if (inDash && segEndsInDash)
            {
                dashBuilder.LineTo(curr);
            }
            else if (inDash && !segEndsInDash)
            {
                dashBuilder.LineTo(curr);
                FlushDash(ref dashBuilder, ref output);
                inDash = false;
            }
            else if (!inDash && segEndsInDash)
            {
                FlushDash(ref dashBuilder, ref output);
                dashBuilder.MoveTo(prev);
                dashBuilder.LineTo(curr);
                inDash = true;
            }
            else
            {
            }
        }

        if (dashBuilder.VerbCount > 1)
        {
            FlushDash(ref dashBuilder, ref output);
        }
    }

    private static void FlushDash(ref BezPathBuilder dash, ref BezPathBuilder output)
    {
        var path = dash.Build();
        foreach (var seg in path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    output.MoveTo(seg.End);
                    break;
                case PathVerb.LineTo:
                    output.LineTo(seg.End);
                    break;
                case PathVerb.QuadTo:
                    output.QuadTo(seg.Control0, seg.End);
                    break;
                case PathVerb.CubicTo:
                    output.CubicTo(seg.Control0, seg.Control1, seg.End);
                    break;
                case PathVerb.Close:
                    output.Close();
                    break;
            }
        }
        dash = BezPathBuilder.Begin(16);
    }
}