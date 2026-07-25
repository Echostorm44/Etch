using System;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 64)]
public readonly struct CubicBez : IEquatable<CubicBez>
{
    public readonly Point P0, P1, P2, P3;

    public CubicBez(Point p0, Point p1, Point p2, Point p3)
    {
        P0 = p0; P1 = p1; P2 = p2; P3 = p3;
    }

    public Point Eval(double t)
    {
        Point p0p1 = Point.Lerp(P0, P1, t);
        Point p1p2 = Point.Lerp(P1, P2, t);
        Point p2p3 = Point.Lerp(P2, P3, t);
        Point p0p1p2 = Point.Lerp(p0p1, p1p2, t);
        Point p1p2p3 = Point.Lerp(p1p2, p2p3, t);
        return Point.Lerp(p0p1p2, p1p2p3, t);
    }

    public Vec2 Derivative(double t)
    {
        double t2 = t * t;
        double dx = 3 * (P1.X - P0.X) + 2 * (2 * P2.X - P1.X - P0.X) * t + (P3.X - 3 * P2.X + 3 * P1.X - P0.X) * t2;
        double dy = 3 * (P1.Y - P0.Y) + 2 * (2 * P2.Y - P1.Y - P0.Y) * t + (P3.Y - 3 * P2.Y + 3 * P1.Y - P0.Y) * t2;
        return new Vec2(dx, dy);
    }

    public Vec2 SecondDerivative(double t)
    {
        double dx = 2 * (2 * P2.X - P1.X - P0.X) + 2 * (P3.X - 3 * P2.X + 3 * P1.X - P0.X) * t;
        double dy = 2 * (2 * P2.Y - P1.Y - P0.Y) + 2 * (P3.Y - 3 * P2.Y + 3 * P1.Y - P0.Y) * t;
        return new Vec2(dx, dy);
    }

    public (CubicBez Left, CubicBez Right) Subdivide(double t)
    {
        Point p0p1 = Point.Lerp(P0, P1, t);
        Point p1p2 = Point.Lerp(P1, P2, t);
        Point p2p3 = Point.Lerp(P2, P3, t);
        Point p0p1p2 = Point.Lerp(p0p1, p1p2, t);
        Point p1p2p3 = Point.Lerp(p1p2, p2p3, t);
        Point mid = Point.Lerp(p0p1p2, p1p2p3, t);
        return (new CubicBez(P0, p0p1, p0p1p2, mid), new CubicBez(mid, p1p2p3, p2p3, P3));
    }

    public (CubicBez Left, CubicBez Right) Subdivide()
        => Subdivide(0.5);

    public Rect Aabb()
        => CurveAabb.ComputeCubicBezAabb(P0, P1, P2, P3);

    public CubicBez TransformedBy(Affine a)
        => new CubicBez(a * P0, a * P1, a * P2, a * P3);

    public CubicBez Reverse()
        => new CubicBez(P3, P2, P1, P0);

    public bool Equals(CubicBez other)
        => P0.Equals(other.P0) && P1.Equals(other.P1) && P2.Equals(other.P2) && P3.Equals(other.P3);

    public override bool Equals(object? obj) => obj is CubicBez other && Equals(other);

    public static bool operator ==(CubicBez left, CubicBez right) => left.Equals(right);

    public static bool operator !=(CubicBez left, CubicBez right) => !left.Equals(right);

    public override int GetHashCode() => HashCode.Combine(P0, P1, P2, P3);

    public override string ToString() => $"CubicBez({P0}, {P1}, {P2}, {P3})";
}
