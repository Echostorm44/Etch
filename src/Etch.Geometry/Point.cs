using System;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct Point : IEquatable<Point>
{
    public readonly double X;
    public readonly double Y;

    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static Point Origin => new(0.0, 0.0);

    public static Point NaN => new(double.NaN, double.NaN);

    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);

    public static Point operator +(Point p, Vec2 v) => new(p.X + v.X, p.Y + v.Y);

    public static Vec2 operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);

    public static Point operator -(Point p, Vec2 v) => new(p.X - v.X, p.Y - v.Y);

    public static Point operator *(Point p, double scalar) => new(p.X * scalar, p.Y * scalar);

    public static Point operator *(double scalar, Point p) => new(p.X * scalar, p.Y * scalar);

    public static bool operator ==(Point left, Point right) => left.Equals(right);

    public static bool operator !=(Point left, Point right) => !left.Equals(right);

    public static Point Add(Point p, Vec2 v) => p + v;

    public static Point Subtract(Point p, Vec2 v) => p - v;

    public static Point Multiply(Point p, double scalar) => p * scalar;

    public static Point Multiply(double scalar, Point p) => scalar * p;

    public double DistanceSquaredTo(Point other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    public double DistanceTo(Point other) => (this - other).Length;

    public static Point Lerp(Point a, Point b, double t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t);

    public bool Equals(Point other)
    {
        if (double.IsNaN(X) || double.IsNaN(other.X) || double.IsNaN(Y) || double.IsNaN(other.Y))
        {
            return false;
        }
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object? obj) => obj is Point other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public override string ToString() => $"({X}, {Y})";
}
