using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class LineTests
{
    [Test]
    public async Task Length()
    {
        var l = new Line(new Point(0, 0), new Point(3, 4));
        if (Math.Abs(l.Length - 5) > 1e-10)
            throw new InvalidOperationException($"(3,4,5) right-triangle length must be 5, got {l.Length}");
    }

    [Test]
    public async Task LengthSquared()
    {
        var l = new Line(new Point(0, 0), new Point(3, 4));
        if (Math.Abs(l.LengthSquared - 25) > 1e-10)
            throw new InvalidOperationException($"LengthSquared must be 25, got {l.LengthSquared}");
    }

    [Test]
    public async Task Aabb()
    {
        var l = new Line(new Point(1, 2), new Point(5, 8));
        var aabb = l.Aabb();
        if (Math.Abs(aabb.MinX - 1) > 1e-10 || Math.Abs(aabb.MinY - 2) > 1e-10
            || Math.Abs(aabb.MaxX - 5) > 1e-10 || Math.Abs(aabb.MaxY - 8) > 1e-10)
            throw new InvalidOperationException($"AABB mismatch: {aabb}");
    }

    [Test]
    public async Task ClosestPointOnSegment()
    {
        var l = new Line(new Point(0, 0), new Point(10, 0));
        var closest = l.ClosestPoint(new Point(5, 3));
        if (Math.Abs(closest.X - 5) > 1e-10 || Math.Abs(closest.Y) > 1e-10)
            throw new InvalidOperationException($"Closest point on segment wrong: {closest}");
    }

    [Test]
    public async Task ClosestPointClampedToEnd()
    {
        var l = new Line(new Point(0, 0), new Point(10, 0));
        var closest = l.ClosestPoint(new Point(20, 0));
        if (Math.Abs(closest.X - 10) > 1e-10)
            throw new InvalidOperationException("Closest point beyond end must clamp to end");
    }

    [Test]
    public async Task ClosestPointClampedToStart()
    {
        var l = new Line(new Point(0, 0), new Point(10, 0));
        var closest = l.ClosestPoint(new Point(-5, 0));
        if (Math.Abs(closest.X) > 1e-10)
            throw new InvalidOperationException("Closest point before start must clamp to start");
    }

    [Test]
    public async Task ClosestPointDegenerateSegmentReturnsStart()
    {
        var p = new Point(3, 4);
        var l = new Line(p, p);
        var closest = l.ClosestPoint(new Point(10, 10));
        if (!closest.Equals(p))
            throw new InvalidOperationException("Degenerate segment must return start point");
    }

    [Test]
    public async Task DistanceToPointOnLine()
    {
        var l = new Line(new Point(0, 0), new Point(10, 0));
        double d = l.DistanceTo(new Point(5, 0));
        if (Math.Abs(d) > 1e-10)
            throw new InvalidOperationException($"Distance to point on line must be 0, got {d}");
    }

    [Test]
    public async Task DistanceToPointOffLine()
    {
        var l = new Line(new Point(0, 0), new Point(10, 0));
        double d = l.DistanceTo(new Point(5, 12));
        if (Math.Abs(d - 12) > 1e-10)
            throw new InvalidOperationException($"Distance to point (5,12) from horizontal line must be 12, got {d}");
    }

    [Test]
    public async Task ClosestPointOnDiagonalSegment()
    {
        var l = new Line(new Point(0, 0), new Point(5, 5));
        var closest = l.ClosestPoint(new Point(5, 0));
        if (Math.Abs(closest.X - 2.5) > 0.001 || Math.Abs(closest.Y - 2.5) > 0.001)
            throw new InvalidOperationException($"Closest to diagonal wrong: {closest}");
    }
}
