using System;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 24)]
public readonly struct Circle : IEquatable<Circle>
{
    public readonly Point Center;
    public readonly double Radius;

    public Circle(Point center, double radius)
    {
        if (radius < 0)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.InvalidCircle,
                $"Circle: radius must be non-negative (got {radius})");
        }
        Center = center;
        Radius = radius;
    }

    public Circle(double centerX, double centerY, double radius)
        : this(new Point(centerX, centerY), radius) { }

    public bool Contains(Point p)
    {
        double dx = p.X - Center.X;
        double dy = p.Y - Center.Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }

    public bool Intersects(Rect rect)
    {
        double closestX = Math.Max(rect.MinX, Math.Min(Center.X, rect.MaxX));
        double closestY = Math.Max(rect.MinY, Math.Min(Center.Y, rect.MaxY));
        double dx = Center.X - closestX;
        double dy = Center.Y - closestY;
        return dx * dx + dy * dy <= Radius * Radius;
    }

    public bool Intersects(Circle other)
    {
        double dx = Center.X - other.Center.X;
        double dy = Center.Y - other.Center.Y;
        double distSq = dx * dx + dy * dy;
        double radiiSum = Radius + other.Radius;
        return distSq <= radiiSum * radiiSum;
    }

    public Rect Aabb()
    {
        return new Rect(
            Center.X - Radius, Center.Y - Radius,
            Center.X + Radius, Center.Y + Radius);
    }

    public bool Equals(Circle other)
        => Center.Equals(other.Center) && Radius == other.Radius;

    public override bool Equals(object? obj) => obj is Circle other && Equals(other);

    public static bool operator ==(Circle left, Circle right) => left.Equals(right);

    public static bool operator !=(Circle left, Circle right) => !left.Equals(right);

    public override int GetHashCode() => HashCode.Combine(Center, Radius);

    public override string ToString() => $"Circle({Center.X:G}, {Center.Y:G}, r={Radius:G})";
}
