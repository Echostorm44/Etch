using System;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

internal static class CurveAabb
{
    public static Rect ComputeQuadBezAabb(Point p0, Point p1, Point p2)
    {
        double minX = Math.Min(p0.X, Math.Min(p1.X, p2.X));
        double maxX = Math.Max(p0.X, Math.Max(p1.X, p2.X));
        double minY = Math.Min(p0.Y, Math.Min(p1.Y, p2.Y));
        double maxY = Math.Max(p0.Y, Math.Max(p1.Y, p2.Y));

        double ax = p1.X - p0.X;
        double bx = p2.X - p1.X - ax;
        double ay = p1.Y - p0.Y;
        double by = p2.Y - p1.Y - ay;

        if (Math.Abs(bx) > GeometryConstants.Epsilon)
        {
            double tx = -ax / bx;
            if (tx > GeometryConstants.Epsilon && tx < 1 - GeometryConstants.Epsilon)
            {
                double ex = p0.X + (2 * ax + bx * tx) * tx;
                minX = Math.Min(minX, ex);
                maxX = Math.Max(maxX, ex);
            }
        }

        if (Math.Abs(by) > GeometryConstants.Epsilon)
        {
            double ty = -ay / by;
            if (ty > GeometryConstants.Epsilon && ty < 1 - GeometryConstants.Epsilon)
            {
                double ey = p0.Y + (2 * ay + by * ty) * ty;
                minY = Math.Min(minY, ey);
                maxY = Math.Max(maxY, ey);
            }
        }

        return new Rect(minX, minY, maxX, maxY);
    }

    public static Rect ComputeCubicBezAabb(Point p0, Point p1, Point p2, Point p3)
    {
        double minX = Math.Min(p0.X, Math.Min(p1.X, Math.Min(p2.X, p3.X)));
        double maxX = Math.Max(p0.X, Math.Max(p1.X, Math.Max(p2.X, p3.X)));
        double minY = Math.Min(p0.Y, Math.Min(p1.Y, Math.Min(p2.Y, p3.Y)));
        double maxY = Math.Max(p0.Y, Math.Max(p1.Y, Math.Max(p2.Y, p3.Y)));

        SolveCubicExtrema(p0.X, p1.X, p2.X, p3.X, ref minX, ref maxX);
        SolveCubicExtrema(p0.Y, p1.Y, p2.Y, p3.Y, ref minY, ref maxY);

        return new Rect(minX, minY, maxX, maxY);
    }

    private static void SolveCubicExtrema(double p0, double p1, double p2, double p3, ref double min, ref double max)
    {
        double a = -p0 + 3 * p1 - 3 * p2 + p3;
        double b = 2 * (p0 - 2 * p1 + p2);
        double c = -p0 + p1;

        if (Math.Abs(a) < GeometryConstants.Epsilon)
        {
            if (Math.Abs(b) > GeometryConstants.Epsilon)
            {
                double t = -c / b;
                if (t > GeometryConstants.Epsilon && t < 1 - GeometryConstants.Epsilon)
                {
                    double x = p0 + (b + a * t) * t;
                    min = Math.Min(min, x);
                    max = Math.Max(max, x);
                }
            }
            return;
        }

        double disc = b * b - 4 * a * c;
        if (disc < 0) return;

        double sqrtDisc = Math.Sqrt(disc);
        double twoA = 2 * a;
        double[] ts = new[]
        {
            (-b + sqrtDisc) / twoA,
            (-b - sqrtDisc) / twoA
        };

        foreach (double t in ts)
        {
            if (t > GeometryConstants.Epsilon && t < 1 - GeometryConstants.Epsilon)
            {
                double x = p0 + (b + a * t) * t;
                min = Math.Min(min, x);
                max = Math.Max(max, x);
            }
        }
    }
}
