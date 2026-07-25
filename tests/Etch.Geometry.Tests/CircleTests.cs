using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class CircleTests
{
    [Test]
    public async Task ConstructorPositiveRadius()
    {
        var c = new Circle(0, 0, 5);
        if (Math.Abs(c.Radius - 5) > 1e-10)
            throw new InvalidOperationException($"Radius mismatch: {c.Radius}");
    }

    [Test]
    public async Task ContainsCenter()
    {
        var c = new Circle(5, 5, 3);
        if (!c.Contains(new Point(5, 5)))
            throw new InvalidOperationException("Center must be contained");
    }

    [Test]
    public async Task ContainsInteriorPoint()
    {
        var c = new Circle(5, 5, 5);
        if (!c.Contains(new Point(7, 6)))
            throw new InvalidOperationException("Interior point must be contained");
    }

    [Test]
    public async Task DoesNotContainExteriorPoint()
    {
        var c = new Circle(0, 0, 1);
        if (c.Contains(new Point(10, 10)))
            throw new InvalidOperationException("Distant point must not be contained");
    }

    [Test]
    public async Task AabbContainsCircle()
    {
        var c = new Circle(5, 5, 3);
        var aabb = c.Aabb();
        double expectedMinX = 5 - 3;
        double expectedMaxX = 5 + 3;
        if (Math.Abs(aabb.MinX - expectedMinX) > 1e-10 || Math.Abs(aabb.MaxX - expectedMaxX) > 1e-10)
            throw new InvalidOperationException($"AABB x mismatch: {aabb}");
        double expectedMinY = 5 - 3;
        double expectedMaxY = 5 + 3;
        if (Math.Abs(aabb.MinY - expectedMinY) > 1e-10 || Math.Abs(aabb.MaxY - expectedMaxY) > 1e-10)
            throw new InvalidOperationException($"AABB y mismatch: {aabb}");
    }

    [Test]
    public async Task AabbCenterCorrect()
    {
        var c = new Circle(5, 5, 2);
        var aabb = c.Aabb();
        if (!aabb.Center.Equals(c.Center))
            throw new InvalidOperationException("AABB center must match circle center");
    }

    [Test]
    public async Task IntersectsRectTrue()
    {
        var c = new Circle(5, 5, 3);
        var r = Rect.FromLTRB(3, 3, 8, 8);
        if (!c.Intersects(r))
            throw new InvalidOperationException("Overlapping circle and rect must intersect");
    }

    [Test]
    public async Task IntersectsRectFalse()
    {
        var c = new Circle(0, 0, 1);
        var r = Rect.FromLTRB(10, 10, 15, 15);
        if (c.Intersects(r))
            throw new InvalidOperationException("Distant circle and rect must not intersect");
    }

    [Test]
    public async Task IntersectsCircleTrue()
    {
        var a = new Circle(0, 0, 5);
        var b = new Circle(3, 4, 5);
        if (!a.Intersects(b))
            throw new InvalidOperationException("Overlapping circles must intersect");
    }

    [Test]
    public async Task IntersectsCircleFalse()
    {
        var a = new Circle(0, 0, 1);
        var b = new Circle(100, 100, 1);
        if (a.Intersects(b))
            throw new InvalidOperationException("Distant circles must not intersect");
    }

    [Test]
    public async Task NegativeRadiusPanics()
    {
        bool threw = false;
        try
        {
            _ = new Circle(0, 0, -1);
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.InvalidCircle)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Negative radius must panic InvalidCircle");
    }

    [Test]
    public async Task ZeroRadiusCircleIsPoint()
    {
        var c = new Circle(3, 4, 0);
        if (!c.Contains(new Point(3, 4)))
            throw new InvalidOperationException("Zero-radius circle must contain its center");
    }
}
