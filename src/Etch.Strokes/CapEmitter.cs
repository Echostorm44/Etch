using System;
using Etch.Geometry;

namespace Etch.Strokes;

public static class CapEmitter
{
    public static void Emit(CapStyle style, Point endpoint, Vec2 tangent, float halfWidth, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        switch (style)
        {
            case CapStyle.Butt:
                EmitButt(endpoint, tangent, halfWidth, ref outer, ref inner);
                break;

            case CapStyle.Round:
                EmitRound(endpoint, tangent, halfWidth, ref outer, ref inner);
                break;

            case CapStyle.Square:
                EmitSquare(endpoint, tangent, halfWidth, ref outer, ref inner);
                break;
        }
    }

    private static void EmitButt(Point endpoint, Vec2 tangent, float halfWidth, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        Vec2 normal = new Vec2(-tangent.Y, tangent.X);
        Point outerPt = endpoint + normal * halfWidth;
        Point innerPt = endpoint - normal * halfWidth;
        outer.LineTo(outerPt);
        inner.LineTo(innerPt);
    }

    private static void EmitRound(Point endpoint, Vec2 tangent, float halfWidth, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        Vec2 normal = new Vec2(-tangent.Y, tangent.X);
        Point outerStart = endpoint + normal * halfWidth;
        Point innerStart = endpoint - normal * halfWidth;

        outer.LineTo(outerStart);
        inner.LineTo(innerStart);

        double startAngle = Math.Atan2(normal.Y, normal.X);
        int segments = 2;

        for (int i = 0; i < segments; i++)
        {
            double t0 = (double)i / segments;
            double t1 = (double)(i + 0.333) / segments;
            double t2 = (double)(i + 0.666) / segments;
            double t3 = (double)(i + 1) / segments;

            double a0 = startAngle + Math.PI * t0;
            double a1 = startAngle + Math.PI * t1;
            double a2 = startAngle + Math.PI * t2;
            double a3 = startAngle + Math.PI * t3;

            outer.CubicTo(
                new Point(endpoint.X + halfWidth * Math.Cos(a1), endpoint.Y + halfWidth * Math.Sin(a1)),
                new Point(endpoint.X + halfWidth * Math.Cos(a2), endpoint.Y + halfWidth * Math.Sin(a2)),
                new Point(endpoint.X + halfWidth * Math.Cos(a3), endpoint.Y + halfWidth * Math.Sin(a3)));

            inner.CubicTo(
                new Point(endpoint.X + halfWidth * Math.Cos(a1), endpoint.Y + halfWidth * Math.Sin(a1)),
                new Point(endpoint.X + halfWidth * Math.Cos(a2), endpoint.Y + halfWidth * Math.Sin(a2)),
                new Point(endpoint.X + halfWidth * Math.Cos(a3), endpoint.Y + halfWidth * Math.Sin(a3)));
        }
    }

    private static void EmitSquare(Point endpoint, Vec2 tangent, float halfWidth, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        Vec2 normal = new Vec2(-tangent.Y, tangent.X);
        Point outerStart = endpoint + normal * halfWidth + tangent * halfWidth;
        Point innerStart = endpoint - normal * halfWidth + tangent * halfWidth;
        Point outerEnd = endpoint + normal * halfWidth;
        Point innerEnd = endpoint - normal * halfWidth;

        outer.LineTo(outerStart);
        inner.LineTo(innerStart);

        outer.LineTo(outerEnd);
        inner.LineTo(innerEnd);
    }

    public static void EmitReverse(CapStyle style, Point start, Vec2 tangent, float halfWidth, ref BezPathBuilder outer, ref BezPathBuilder inner)
    {
        Vec2 reversed = new Vec2(-tangent.X, -tangent.Y);
        Emit(style, start, reversed, halfWidth, ref outer, ref inner);
    }
}