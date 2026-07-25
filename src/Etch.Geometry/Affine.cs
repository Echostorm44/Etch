using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 48)]
public readonly struct Affine : IEquatable<Affine>
{
    public readonly double M00, M01;
    public readonly double M10, M11;
    public readonly double M02, M12;

    public Affine(double m00, double m01, double m10, double m11, double m02, double m12)
    {
        M00 = m00; M01 = m01;
        M10 = m10; M11 = m11;
        M02 = m02; M12 = m12;
    }

    public static Affine Identity => new(1.0, 0.0, 0.0, 1.0, 0.0, 0.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Affine Translate(Vec2 t) => new(1.0, 0.0, 0.0, 1.0, t.X, t.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Affine Translate(double x, double y) => new(1.0, 0.0, 0.0, 1.0, x, y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Affine Scale(double s) => new(s, 0.0, 0.0, s, 0.0, 0.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Affine Scale(double sx, double sy) => new(sx, 0.0, 0.0, sy, 0.0, 0.0);

    public static Affine Rotate(double radians)
    {
        double s = Math.Sin(radians);
        double c = Math.Cos(radians);
        return new(c, -s, s, c, 0.0, 0.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Affine Skew(double sx, double sy) => new(1.0, Math.Tan(sy), Math.Tan(sx), 1.0, 0.0, 0.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double Determinant() => M00 * M11 - M01 * M10;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Point Transform(Point p) => new(M00 * p.X + M01 * p.Y + M02, M10 * p.X + M11 * p.Y + M12);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Vec2 Transform(Vec2 v) => new(M00 * v.X + M01 * v.Y, M10 * v.X + M11 * v.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Point operator *(Affine a, Point p) => a.Transform(p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vec2 operator *(Affine a, Vec2 v) => a.Transform(v);

    public static Affine operator *(Affine a, Affine b) => new(
        a.M00 * b.M00 + a.M01 * b.M10,
        a.M00 * b.M01 + a.M01 * b.M11,
        a.M10 * b.M00 + a.M11 * b.M10,
        a.M10 * b.M01 + a.M11 * b.M11,
        a.M00 * b.M02 + a.M01 * b.M12 + a.M02,
        a.M10 * b.M02 + a.M11 * b.M12 + a.M12);

    public static bool operator ==(Affine left, Affine right) => left.Equals(right);

    public static bool operator !=(Affine left, Affine right) => !left.Equals(right);

    public static Affine Multiply(Affine a, Affine b) => a * b;

    public static Point Multiply(Affine a, Point p) => a * p;

    public static Vec2 Multiply(Affine a, Vec2 v) => a * v;

    public Affine Inverse()
    {
        double det = Determinant();
        if (Math.Abs(det) < GeometryConstants.Epsilon)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.NonInvertibleAffine,
                "Affine transform is singular (determinant near zero) and cannot be inverted.");
        }
        double inv = 1.0 / det;
        return new Affine(
            M11 * inv,
            -M01 * inv,
            -M10 * inv,
            M00 * inv,
            (M01 * M12 - M11 * M02) * inv,
            (M10 * M02 - M00 * M12) * inv);
    }

    public Affine PreTranslate(Vec2 t) => this * Translate(t);

    public Affine PreTranslate(double x, double y) => this * Translate(x, y);

    public Affine PreScale(double s) => this * Scale(s);

    public Affine PreScale(double sx, double sy) => this * Scale(sx, sy);

    public Affine PreRotate(double radians) => this * Rotate(radians);

    public Affine PreSkew(double sx, double sy) => this * Skew(sx, sy);

    public Affine PostTranslate(Vec2 t) => Translate(t) * this;

    public Affine PostTranslate(double x, double y) => Translate(x, y) * this;

    public Affine PostScale(double s) => Scale(s) * this;

    public Affine PostScale(double sx, double sy) => Scale(sx, sy) * this;

    public Affine PostRotate(double radians) => Rotate(radians) * this;

    public Affine PostSkew(double sx, double sy) => Skew(sx, sy) * this;

    public bool Equals(Affine other) =>
        M00 == other.M00 && M01 == other.M01 &&
        M10 == other.M10 && M11 == other.M11 &&
        M02 == other.M02 && M12 == other.M12;

    public override bool Equals(object? obj) => obj is Affine other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(M00, M01, M10, M11, M02, M12);

    public override string ToString() =>
        $"Affine({M00:G}, {M01:G}, {M10:G}, {M11:G}, {M02:G}, {M12:G})";
}
