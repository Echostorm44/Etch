using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Differential;

internal sealed class HausdorffTests
{
    [Test]
    public void IdenticalPolylines_ReturnsZero()
    {
        Point[] poly = [
            new Point(0, 0),
            new Point(1, 0),
            new Point(2, 0),
            new Point(3, 0),
        ];

        double d = Hausdorff.Distance(poly, poly);
        if (d != 0)
            throw new InvalidOperationException($"Expected 0, got {d}");
    }

    [Test]
    public void ParallelSegments_OffsetByD_ReturnsD()
    {
        Point[] a = [
            new Point(0, 0),
            new Point(1, 0),
        ];
        Point[] b = [
            new Point(0, 1),
            new Point(1, 1),
        ];

        double d = Hausdorff.Distance(a, b);
        if (d != 1.0)
            throw new InvalidOperationException($"Expected 1.0, got {d}");
    }

    [Test]
    public void TriangleVsOffsetTriangle_ReturnsOffsetDistance()
    {
        Point[] a = [
            new Point(0, 0),
            new Point(3, 0),
            new Point(1.5, 4),
        ];
        Point[] b = [
            new Point(0, 0.5),
            new Point(3, 0.5),
            new Point(1.5, 4.5),
        ];

        double d = Hausdorff.Distance(a, b);
        if (Math.Abs(d - 0.5) > 1e-10)
            throw new InvalidOperationException($"Expected 0.5, got {d}");
    }

    [Test]
    public void EmptyPoly_ReturnsMaxValue()
    {
        Point[] empty = [];
        Point[] poly = [new Point(0, 0), new Point(1, 1)];

        double d = Hausdorff.Distance(empty, poly);
        if (d != double.MaxValue)
            throw new InvalidOperationException($"Expected MaxValue for empty input, got {d}");
    }

    [Test]
    public void SinglePointPoly_VsPointOnSegment()
    {
        Point[] single = [new Point(0.5, 0)];
        Point[] seg = [new Point(0, 0), new Point(1, 0)];

        double d = Hausdorff.Distance(single, seg);
        if (Math.Abs(d - 0.5) > 1e-10)
            throw new InvalidOperationException($"Expected 0.5, got {d}");
    }
}
