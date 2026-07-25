using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Geometry;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct Rect : IEquatable<Rect>
{
    public readonly double MinX, MinY, MaxX, MaxY;

    public Rect(double minX, double minY, double maxX, double maxY)
    {
        if (maxX < minX || maxY < minY)
        {
            Etch.Panic.Invariant(
                Etch.PanicCodes.InvertedRect,
                $"Rect: max must be ≥ min (got MinX={minX}, MaxX={maxX}, MinY={minY}, MaxY={maxY})");
        }
        MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY;
    }

    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;

    public double Area => Width * Height;

    public static Rect Empty => new(double.NaN, double.NaN, double.NaN, double.NaN);

    public bool IsEmpty
    {
        get
        {
            if (double.IsNaN(MinX))
                return true;
            return Width < 0 || Height < 0;
        }
    }

    public Point Center => new((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

    public static Rect FromLTRB(double left, double top, double right, double bottom)
        => new Rect(left, top, right, bottom);

    public static Rect FromMinSize(Point min, Vec2 size)
        => new Rect(min.X, min.Y, min.X + size.X, min.Y + size.Y);

    public static Rect FromMinSize(double minX, double minY, double width, double height)
        => new Rect(minX, minY, minX + width, minY + height);

    public static Rect FromPoints(Point a, Point b)
        => new Rect(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    public bool Contains(Point p)
        => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public bool Contains(Rect other)
        => other.MinX >= MinX && other.MaxX <= MaxX
        && other.MinY >= MinY && other.MaxY <= MaxY;

    public bool Intersects(Rect other)
        => MinX < other.MaxX && other.MinX < MaxX
        && MinY < other.MaxY && other.MinY < MaxY;

    public Rect Intersect(Rect other)
    {
        double left = Math.Max(MinX, other.MinX);
        double top = Math.Max(MinY, other.MinY);
        double right = Math.Min(MaxX, other.MaxX);
        double bottom = Math.Min(MaxY, other.MaxY);
        if (left >= right || top >= bottom)
            return Rect.Empty;
        return new Rect(left, top, right, bottom);
    }

    public Rect Union(Rect other)
        => new Rect(
            Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public Rect Inflate(double amount)
        => new Rect(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);

    public Rect Inflate(Vec2 amount)
        => new Rect(MinX - amount.X, MinY - amount.Y, MaxX + amount.X, MaxY + amount.Y);

    public Rect Translated(Vec2 offset)
        => new Rect(MinX + offset.X, MinY + offset.Y, MaxX + offset.X, MaxY + offset.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rect Transform(Affine a)
    {
        double x0 = a.M00 * MinX + a.M01 * MinY + a.M02;
        double y0 = a.M10 * MinX + a.M11 * MinY + a.M12;
        double x1 = a.M00 * MaxX + a.M01 * MinY + a.M02;
        double y1 = a.M10 * MaxX + a.M11 * MinY + a.M12;
        double x2 = a.M00 * MaxX + a.M01 * MaxY + a.M02;
        double y2 = a.M10 * MaxX + a.M11 * MaxY + a.M12;
        double x3 = a.M00 * MinX + a.M01 * MaxY + a.M02;
        double y3 = a.M10 * MinX + a.M11 * MaxY + a.M12;

        double minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        double maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        double minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        double maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));

        return new Rect(minX, minY, maxX, maxY);
    }

    public bool Equals(Rect other)
        => MinX == other.MinX && MinY == other.MinY
        && MaxX == other.MaxX && MaxY == other.MaxY;

    public override bool Equals(object? obj) => obj is Rect other && Equals(other);

    public static bool operator ==(Rect left, Rect right) => left.Equals(right);

    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    public override int GetHashCode() => HashCode.Combine(MinX, MinY, MaxX, MaxY);

    public override string ToString() => $"Rect({MinX:G}, {MinY:G}, {MaxX:G}, {MaxY:G})";
}
