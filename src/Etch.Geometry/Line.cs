using System;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct Line : IEquatable<Line>
{
    public readonly Point Start, End;

    public Line(Point start, Point end)
    {
        Start = start;
        End = end;
    }

    public double Length => (End - Start).Length;

    public double LengthSquared => (End - Start).LengthSquared;

    public Rect Aabb()
        => Rect.FromPoints(Start, End);

    public Point ClosestPoint(Point p)
    {
        Vec2 ab = End - Start;
        double abLenSq = ab.LengthSquared;
        if (abLenSq < GeometryConstants.Epsilon * GeometryConstants.Epsilon)
            return Start;

        Vec2 ap = p - Start;
        double t = Math.Max(0, Math.Min(1, ap.Dot(ab) / abLenSq));
        return Start + ab * t;
    }

    public double DistanceTo(Point p)
    {
        Point closest = ClosestPoint(p);
        return (closest - p).Length;
    }

    public bool Equals(Line other)
        => Start.Equals(other.Start) && End.Equals(other.End);

    public override bool Equals(object? obj) => obj is Line other && Equals(other);

    public static bool operator ==(Line left, Line right) => left.Equals(right);

    public static bool operator !=(Line left, Line right) => !left.Equals(right);

    public override int GetHashCode() => HashCode.Combine(Start, End);

    public override string ToString() => $"Line({Start}, {End})";
}
