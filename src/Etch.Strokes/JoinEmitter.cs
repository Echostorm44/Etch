using System;
using Etch.Geometry;

namespace Etch.Strokes;

public static class JoinEmitter
{
    public static void Emit(JoinStyle style, Vec2 endTangent, Vec2 startTangent, float halfWidth, float miterLimit, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        double cross = endTangent.X * startTangent.Y - endTangent.Y * startTangent.X;
        double dot = endTangent.X * startTangent.X + endTangent.Y * startTangent.Y;

        if (Math.Abs(cross) < 1e-10)
        {
            return;
        }

        switch (style)
        {
            case JoinStyle.Round:
                EmitRound(endTangent, startTangent, halfWidth, cross, ref outer, ref inner);
                break;

            case JoinStyle.Bevel:
                EmitBevel(halfWidth, ref outer, ref inner);
                break;

            case JoinStyle.Miter:
            default:
                EmitMiter(endTangent, startTangent, halfWidth, miterLimit, cross, dot, ref outer, ref inner);
                break;
        }
    }

    private static void EmitRound(Vec2 endTangent, Vec2 startTangent, float halfWidth, double cross, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        double sign = cross >= 0 ? 1.0 : -1.0;

        Vec2 endNorm = new Vec2(-endTangent.Y, endTangent.X) * halfWidth;
        Vec2 startNorm = new Vec2(-startTangent.Y, startTangent.X) * halfWidth;

        Point outerCurr = outer.Current;
        Point innerCurr = inner.Current;

        double cosAngle = Math.Clamp(endTangent.X * startTangent.X + endTangent.Y * startTangent.Y, -1.0, 1.0);
        double angle = Math.Acos(cosAngle);
        int segments = ArcSegments(angle);

        double sweepAngle = Math.PI - angle;
        if (sign < 0) sweepAngle = -(Math.PI - angle);

        double startAngle = Math.Atan2(endNorm.Y, endNorm.X);

        outer.LineTo(outerCurr + endNorm);
        inner.LineTo(innerCurr + startNorm);

        for (int i = 0; i < segments; i++)
        {
            double t0 = (double)i / segments;
            double t1 = (double)(i + 0.333) / segments;
            double t2 = (double)(i + 0.666) / segments;
            double t3 = (double)(i + 1) / segments;

            double a0 = startAngle + sweepAngle * t0;
            double a1 = startAngle + sweepAngle * t1;
            double a2 = startAngle + sweepAngle * t2;
            double a3 = startAngle + sweepAngle * t3;

            Point p0 = new Point(outerCurr.X + halfWidth * Math.Cos(a0), outerCurr.Y + halfWidth * Math.Sin(a0));
            Point p1 = new Point(outerCurr.X + halfWidth * Math.Cos(a1), outerCurr.Y + halfWidth * Math.Sin(a1));
            Point p2 = new Point(outerCurr.X + halfWidth * Math.Cos(a2), outerCurr.Y + halfWidth * Math.Sin(a2));
            Point p3 = new Point(outerCurr.X + halfWidth * Math.Cos(a3), outerCurr.Y + halfWidth * Math.Sin(a3));

            outer.CubicTo(p1, p2, p3);

            Point q0 = new Point(innerCurr.X + halfWidth * Math.Cos(a0), innerCurr.Y + halfWidth * Math.Sin(a0));
            Point q1 = new Point(innerCurr.X + halfWidth * Math.Cos(a1), innerCurr.Y + halfWidth * Math.Sin(a1));
            Point q2 = new Point(innerCurr.X + halfWidth * Math.Cos(a2), innerCurr.Y + halfWidth * Math.Sin(a2));
            Point q3 = new Point(innerCurr.X + halfWidth * Math.Cos(a3), innerCurr.Y + halfWidth * Math.Sin(a3));

            inner.CubicTo(q1, q2, q3);
        }
    }

    private static int ArcSegments(double angle)
    {
        if (angle <= Math.PI / 6) return 1;
        if (angle <= Math.PI / 3) return 2;
        if (angle <= Math.PI / 2) return 3;
        return 4;
    }

    private static void EmitBevel(float halfWidth, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        Point outerCurr = outer.Current;
        Point innerCurr = inner.Current;
        double dx = outerCurr.X - innerCurr.X;
        double dy = outerCurr.Y - innerCurr.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-10) return;

        double nx = -dy / len * halfWidth;
        double ny = dx / len * halfWidth;

        outer.LineTo(new Point(outerCurr.X + nx, outerCurr.Y + ny));
        inner.LineTo(new Point(innerCurr.X + nx, innerCurr.Y + ny));
    }

    private static void EmitMiter(Vec2 endTangent, Vec2 startTangent, float halfWidth, float miterLimit, double cross, double dot, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        double len1Sq = endTangent.X * endTangent.X + endTangent.Y * endTangent.Y;
        double len2Sq = startTangent.X * startTangent.X + startTangent.Y * startTangent.Y;
        if (len1Sq < 1e-20 || len2Sq < 1e-20) return;

        double len1 = Math.Sqrt(len1Sq);
        double len2 = Math.Sqrt(len2Sq);

        double sinHalfAngle = Math.Abs(cross) / (len1 * len2);
        if (sinHalfAngle < 1e-10) return;

        double miterRatio = 1.0 / sinHalfAngle;
        if (miterRatio > miterLimit)
        {
            EmitBevel(halfWidth, ref outer, ref inner);
            return;
        }

        Point outerCurr = outer.Current;
        Point innerCurr = inner.Current;

        double dx = innerCurr.X - outerCurr.X;
        double dy = innerCurr.Y - outerCurr.Y;

        double t1 = (dx * startTangent.Y - dy * startTangent.X) / cross;
        double t2 = (dx * endTangent.Y - dy * endTangent.X) / cross;

        double cornerX = outerCurr.X + endTangent.X * t1;
        double cornerY = outerCurr.Y + endTangent.Y * t1;

        double bisectorX = endTangent.X / len1 + startTangent.X / len2;
        double bisectorY = endTangent.Y / len1 + startTangent.Y / len2;
        double bisectorLenSq = bisectorX * bisectorX + bisectorY * bisectorY;
        if (bisectorLenSq < 1e-20)
        {
            EmitBevel(halfWidth, ref outer, ref inner);
            return;
        }
        double bisectorLen = Math.Sqrt(bisectorLenSq);
        bisectorX /= bisectorLen;
        bisectorY /= bisectorLen;

        double miterLen = halfWidth / sinHalfAngle;

        double mx = cornerX + bisectorX * miterLen;
        double my = cornerY + bisectorY * miterLen;

        Point miterPt = new Point(mx, my);
        outer.LineTo(miterPt);
        inner.LineTo(miterPt);
    }
}