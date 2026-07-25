using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly double X;
    public readonly double Y;

    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared => X * X + Y * Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec2 Normalize()
    {
        double lenSq = X * X + Y * Y;
        if (lenSq < GeometryConstants.Epsilon * GeometryConstants.Epsilon)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.DegenerateVector,
                "Cannot normalise a zero-length or near-zero-length vector.");
        }
        double invLen = 1.0 / Math.Sqrt(lenSq);
        return new Vec2(X * invLen, Y * invLen);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Dot(Vec2 other) => X * other.X + Y * other.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Cross(Vec2 other) => X * other.Y - Y * other.X;

    public Vec2 Perpendicular() => new(-Y, X);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator -(Vec2 v) => new(-v.X, -v.Y);

    public static Vec2 operator *(Vec2 v, double scalar) => new(v.X * scalar, v.Y * scalar);

    public static Vec2 operator *(double scalar, Vec2 v) => new(v.X * scalar, v.Y * scalar);

    public static bool operator ==(Vec2 left, Vec2 right) => left.Equals(right);

    public static bool operator !=(Vec2 left, Vec2 right) => !left.Equals(right);

    public static Vec2 Add(Vec2 a, Vec2 b) => a + b;

    public static Vec2 Subtract(Vec2 a, Vec2 b) => a - b;

    public static Vec2 Negate(Vec2 v) => -v;

    public static Vec2 Multiply(Vec2 v, double scalar) => v * scalar;

    public static Vec2 Multiply(double scalar, Vec2 v) => scalar * v;

    public bool Equals(Vec2 other)
    {
        if (double.IsNaN(X) || double.IsNaN(other.X) || double.IsNaN(Y) || double.IsNaN(other.Y))
        {
            return false;
        }
        return X == other.X && Y == other.Y;
    }

    public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y);

    public override string ToString() => $"({X}, {Y})";
}
