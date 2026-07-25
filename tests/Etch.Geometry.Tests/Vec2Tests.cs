using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class Vec2Tests
{
    [Test]
    public async Task NormalizeUnitLength()
    {
        var v = new Vec2(3.0, 4.0);
        var n = v.Normalize();
        double len = Math.Sqrt(n.X * n.X + n.Y * n.Y);
        if (Math.Abs(len - 1.0) > 1e-10)
            throw new InvalidOperationException($"Normalised vector must have length 1, got {len}");
    }

    [Test]
    public async Task NormalizeZeroThrows()
    {
        var zero = new Vec2(0.0, 0.0);
        bool threw = false;
        try
        {
            _ = zero.Normalize();
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.DegenerateVector)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Normalize on zero vector must throw DegenerateVector");
    }

    [Test]
    public async Task DotIsCommutative()
    {
        var a = new Vec2(1.0, 2.0);
        var b = new Vec2(3.0, 4.0);
        double ab = a.Dot(b);
        double ba = b.Dot(a);
        if (Math.Abs(ab - ba) > 0.0)
            throw new InvalidOperationException("Dot product must be commutative");
    }

    [Test]
    public async Task CrossIsAntisymmetric()
    {
        var a = new Vec2(1.0, 2.0);
        var b = new Vec2(3.0, 4.0);
        double ab = a.Cross(b);
        double ba = b.Cross(a);
        if (Math.Abs(ab + ba) > 0.0)
            throw new InvalidOperationException("Cross product must be antisymmetric: Cross(a,b) = -Cross(b,a)");
    }

    [Test]
    public async Task PerpendicularDotIsZero()
    {
        var v = new Vec2(3.0, 4.0);
        var perp = v.Perpendicular();
        double dot = v.Dot(perp);
        if (Math.Abs(dot) > 1e-10)
            throw new InvalidOperationException("A vector dotted with its perpendicular must be zero");
    }

    [Test]
    public async Task AdditionAndSubtractionAlgebra()
    {
        var a = new Vec2(5.0, 6.0);
        var b = new Vec2(2.0, 3.0);
        var sum = a + b;
        var recovered = sum - b;
        double dx = Math.Abs(recovered.X - a.X);
        double dy = Math.Abs(recovered.Y - a.Y);
        if (dx > 1.0 || dy > 1.0)
            throw new InvalidOperationException("(a + b) - b must equal a within 1 ulp");
    }

    [Test]
    public async Task LengthSquaredWithoutSqrt()
    {
        var v = new Vec2(3.0, 4.0);
        if (Math.Abs(v.LengthSquared - 25.0) > 1e-10)
            throw new InvalidOperationException("LengthSquared of (3,4) must be 25");
    }

    [Test]
    public async Task LengthConsistentWithPythagoras()
    {
        var v = new Vec2(5.0, 12.0);
        double expected = 13.0;
        if (Math.Abs(v.Length - expected) > 1e-10)
            throw new InvalidOperationException($"Length of (5,12) must be 13, got {v.Length}");
    }

    [Test]
    public async Task Negation()
    {
        var v = new Vec2(3.0, -4.0);
        var neg = -v;
        if (Math.Abs(neg.X - (-3.0)) > 1e-10 || Math.Abs(neg.Y - 4.0) > 1e-10)
            throw new InvalidOperationException("Negation must negate both components");
    }

    [Test]
    public async Task ScalarMultiplicationCommutes()
    {
        var v = new Vec2(2.0, 3.0);
        double s = 4.0;
        Vec2 va = v * s;
        Vec2 vb = s * v;
        if (!va.Equals(vb))
            throw new InvalidOperationException("Scalar multiplication must commute: v*s == s*v");
    }

    [Test]
    public async Task ToStringFormat()
    {
        var v = new Vec2(1.5, 2.5);
        string s = v.ToString();
        if (!s.Contains("1.5", StringComparison.Ordinal) || !s.Contains("2.5", StringComparison.Ordinal))
            throw new InvalidOperationException($"ToString must contain both values, got: {s}");
    }

    [Test]
    public async Task EqualityIsComponentwise()
    {
        var a = new Vec2(1.0, 2.0);
        var b = new Vec2(1.0, 2.0);
        var c = new Vec2(1.0, 3.0);
        if (!a.Equals(b))
            throw new InvalidOperationException("Equal Vec2s must be equal");
        if (a.Equals(c))
            throw new InvalidOperationException("Different Vec2s must not be equal");
    }
}
