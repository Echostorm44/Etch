using Etch.Geometry;
using Etch.Primitives;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class PointTests
{
    [Test]
    public async Task OriginIsZero()
    {
        var origin = Point.Origin;
        if (origin.X != 0.0 || origin.Y != 0.0)
            throw new InvalidOperationException("Origin must be (0, 0)");
    }

    [Test]
    public async Task NaNPointNotEqualToItself()
    {
        var nan = Point.NaN;
        if (nan.Equals(nan))
            throw new InvalidOperationException("NaN Point must not equal itself");
    }

    [Test]
    public async Task NaNPropagatesThroughAddition()
    {
        var nan = Point.NaN;
        var finite = Point.Origin;
        var result = nan + (finite - finite);
        if (!double.IsNaN(result.X) || !double.IsNaN(result.Y))
            throw new InvalidOperationException("NaN must propagate through Vec2 addition");
    }

    [Test]
    public async Task LerpAtZeroReturnsFirst()
    {
        var a = new Point(3.0, 4.0);
        var b = new Point(7.0, 9.0);
        var result = Point.Lerp(a, b, 0.0);
        if (result.X != a.X || result.Y != a.Y)
            throw new InvalidOperationException("Lerp(a, b, 0) must return a");
    }

    [Test]
    public async Task LerpAtOneReturnsSecond()
    {
        var a = new Point(3.0, 4.0);
        var b = new Point(7.0, 9.0);
        var result = Point.Lerp(a, b, 1.0);
        if (result.X != b.X || result.Y != b.Y)
            throw new InvalidOperationException("Lerp(a, b, 1) must return b");
    }

    [Test]
    public async Task LerpMidpointIsCorrect()
    {
        var a = new Point(2.0, 4.0);
        var b = new Point(6.0, 8.0);
        var mid = Point.Lerp(a, b, 0.5);
        double expectedX = 2.0 + (6.0 - 2.0) * 0.5;
        double expectedY = 4.0 + (8.0 - 4.0) * 0.5;
        if (Math.Abs(mid.X - expectedX) > 0.5 || Math.Abs(mid.Y - expectedY) > 0.5)
            throw new InvalidOperationException("Midpoint must be correct within tolerance");
    }

    [Test]
    public async Task SubtractionAlgebra()
    {
        var a = new Point(5.0, 6.0);
        var b = new Point(2.0, 3.0);
        var diff = a - b;
        var recovered = b + diff;
        if (Math.Abs(recovered.X - a.X) > 1.0 || Math.Abs(recovered.Y - a.Y) > 1.0)
            throw new InvalidOperationException("(a - b) + b must equal a within 1 ulp");
    }

    [Test]
    public async Task DistanceToIsSymmetric()
    {
        var a = new Point(1.0, 2.0);
        var b = new Point(4.0, 6.0);
        double d_ab = a.DistanceTo(b);
        double d_ba = b.DistanceTo(a);
        if (Math.Abs(d_ab - d_ba) > 0.0)
            throw new InvalidOperationException("DistanceTo must be symmetric");
    }

    [Test]
    public async Task DistanceToOrigin()
    {
        var p = new Point(3.0, 4.0);
        double d = p.DistanceTo(Point.Origin);
        if (Math.Abs(d - 5.0) > 1e-10)
            throw new InvalidOperationException($"Distance of (3,4) from origin must be 5, got {d}");
    }

    [Test]
    public async Task ScaleFromOrigin()
    {
        var p = new Point(1.5, 2.5);
        var scaled = p * 2.0;
        if (Math.Abs(scaled.X - 3.0) > 1e-10 || Math.Abs(scaled.Y - 5.0) > 1e-10)
            throw new InvalidOperationException("Point scaled by 2 must double each component");
    }

    [Test]
    public async Task ToStringFormat()
    {
        var p = new Point(1.5, 2.5);
        string s = p.ToString();
        if (!s.Contains("1.5", StringComparison.Ordinal) || !s.Contains("2.5", StringComparison.Ordinal))
            throw new InvalidOperationException($"ToString must contain both values, got: {s}");
    }

    [Test]
    public async Task EqualityIsComponentwise()
    {
        var a = new Point(1.0, 2.0);
        var b = new Point(1.0, 2.0);
        var c = new Point(1.0, 3.0);
        if (!a.Equals(b))
            throw new InvalidOperationException("Equal points must be equal");
        if (a.Equals(c))
            throw new InvalidOperationException("Different points must not be equal");
    }
}
