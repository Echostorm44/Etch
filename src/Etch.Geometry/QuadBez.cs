using System;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 48)]
public readonly struct QuadBez : IEquatable<QuadBez>
{
    public readonly Point P0, P1, P2;

    public QuadBez(Point p0, Point p1, Point p2)
    {
        P0 = p0; P1 = p1; P2 = p2;
    }

    public Point Eval(double t)
    {
        Point p0p1 = Point.Lerp(P0, P1, t);
        Point p1p2 = Point.Lerp(P1, P2, t);
        return Point.Lerp(p0p1, p1p2, t);
    }

    public Vec2 Derivative(double t)
    {
        double dx = 2 * (P1.X - P0.X) + 2 * t * (P2.X - 2 * P1.X + P0.X);
        double dy = 2 * (P1.Y - P0.Y) + 2 * t * (P2.Y - 2 * P1.Y + P0.Y);
        return new Vec2(dx, dy);
    }

    public (QuadBez Left, QuadBez Right) Subdivide(double t)
    {
        Point p0p1 = Point.Lerp(P0, P1, t);
        Point p1p2 = Point.Lerp(P1, P2, t);
        Point mid = Point.Lerp(p0p1, p1p2, t);
        return (new QuadBez(P0, p0p1, mid), new QuadBez(mid, p1p2, P2));
    }

    public (QuadBez Left, QuadBez Right) Subdivide()
        => Subdivide(0.5);

    public Rect Aabb()
        => CurveAabb.ComputeQuadBezAabb(P0, P1, P2);

    public CubicBez Elevate()
    {
        Point p1e = Point.Lerp(P0, P1, 2.0 / 3.0);
        Point p2e = Point.Lerp(P2, P1, 2.0 / 3.0);
        return new CubicBez(P0, p1e, p2e, P2);
    }

    public QuadBez TransformedBy(Affine a)
        => new QuadBez(a * P0, a * P1, a * P2);

    public bool Equals(QuadBez other)
        => P0.Equals(other.P0) && P1.Equals(other.P1) && P2.Equals(other.P2);

    public override bool Equals(object? obj) => obj is QuadBez other && Equals(other);

    public static bool operator ==(QuadBez left, QuadBez right) => left.Equals(right);

    public static bool operator !=(QuadBez left, QuadBez right) => !left.Equals(right);

    public override int GetHashCode() => HashCode.Combine(P0, P1, P2);

    public override string ToString() => $"QuadBez({P0}, {P1}, {P2})";
}
