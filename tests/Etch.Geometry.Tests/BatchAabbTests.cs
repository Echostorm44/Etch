using System;
using Etch.Geometry;
using Etch.Primitives;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class BatchAabbTests
{
    [Test]
    public void OfPoints_Empty_ReturnsEmpty()
    {
        var result = BatchAabb.OfPoints(Array.Empty<Point>());
        if (!result.IsEmpty)
            throw new InvalidOperationException("Empty span must return Rect.Empty");
    }

    [Test]
    public void OfPoints_SinglePoint_ReturnsDegenerateRect()
    {
        var pt = new Point(3.0, 4.0);
        var result = BatchAabb.OfPoints(new[] { pt });

        if (result.MinX != 3.0 || result.MaxX != 3.0)
            throw new InvalidOperationException($"MinX/MaxX should be 3.0, got {result.MinX}/{result.MaxX}");
        if (result.MinY != 4.0 || result.MaxY != 4.0)
            throw new InvalidOperationException($"MinY/MaxY should be 4.0, got {result.MinY}/{result.MaxY}");
        if (result.Width != 0.0)
            throw new InvalidOperationException($"Width should be 0, got {result.Width}");
        if (result.Height != 0.0)
            throw new InvalidOperationException($"Height should be 0, got {result.Height}");
        if (result.IsEmpty)
            throw new InvalidOperationException("Single-point rect should not be empty");
    }

    [Test]
    public void OfPoints_TwoPoints_ContainsBoth()
    {
        var pts = new[] { new Point(1.0, 2.0), new Point(5.0, 7.0) };
        var result = BatchAabb.OfPoints(pts);

        if (result.MinX != 1.0 || result.MaxX != 5.0)
            throw new InvalidOperationException($"X bounds wrong: {result.MinX}/{result.MaxX}");
        if (result.MinY != 2.0 || result.MaxY != 7.0)
            throw new InvalidOperationException($"Y bounds wrong: {result.MinY}/{result.MaxY}");
    }

    [Test]
    public void OfPoints_FourCorners_Square()
    {
        var pts = new[]
        {
            new Point(0.0, 0.0),
            new Point(10.0, 0.0),
            new Point(10.0, 10.0),
            new Point(0.0, 10.0)
        };
        var result = BatchAabb.OfPoints(pts);

        if (result.MinX != 0.0 || result.MaxX != 10.0)
            throw new InvalidOperationException($"X wrong: {result.MinX}/{result.MaxX}");
        if (result.MinY != 0.0 || result.MaxY != 10.0)
            throw new InvalidOperationException($"Y wrong: {result.MinY}/{result.MaxY}");
        if (result.Width != 10.0 || result.Height != 10.0)
            throw new InvalidOperationException($"Size wrong: {result.Width}x{result.Height}");
    }

    [Test]
    public void OfPoints_HandComputed()
    {
        var pts = new[]
        {
            new Point(2.5, -1.0),
            new Point(8.0, 3.5),
            new Point(-3.0, 7.0),
            new Point(0.0, 0.0)
        };
        var result = BatchAabb.OfPoints(pts);

        if (result.MinX != -3.0 || result.MaxX != 8.0)
            throw new InvalidOperationException($"X: expected [-3, 8], got [{result.MinX}, {result.MaxX}]");
        if (result.MinY != -1.0 || result.MaxY != 7.0)
            throw new InvalidOperationException($"Y: expected [-1, 7], got [{result.MinY}, {result.MaxY}]");
    }

    [Test]
    public void OfPoints_NegativeCoords()
    {
        var pts = new[]
        {
            new Point(-100.0, -50.0),
            new Point(-10.0, -20.0),
            new Point(-50.0, -100.0)
        };
        var result = BatchAabb.OfPoints(pts);

        if (result.MinX != -100.0 || result.MaxX != -10.0)
            throw new InvalidOperationException($"X: expected [-100, -10], got [{result.MinX}, {result.MaxX}]");
        if (result.MinY != -100.0 || result.MaxY != -20.0)
            throw new InvalidOperationException($"Y: expected [-100, -20], got [{result.MinY}, {result.MaxY}]");
    }

    [Test]
    public void OfPoints_ZeroAlloc()
    {
        var pts = new Point[100];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Point(i, i * 2);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iter = 0; iter < 100; iter++)
        {
            BatchAabb.OfPoints(pts);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        if (delta > 0)
            throw new InvalidOperationException($"OfPoints allocated {delta} bytes (expected 0)");
    }

    [Test]
    public void OfPointsTransformed_Empty_ReturnsEmpty()
    {
        var result = BatchAabb.OfPointsTransformed(Affine.Identity, Array.Empty<Point>());
        if (!result.IsEmpty)
            throw new InvalidOperationException("Empty span must return Rect.Empty");
    }

    [Test]
    public void OfPointsTransformed_SinglePoint_Identity()
    {
        var a = Affine.Identity;
        var pts = new[] { new Point(3.0, 4.0) };
        var result = BatchAabb.OfPointsTransformed(a, pts);

        if (result.MinX != 3.0 || result.MaxX != 3.0)
            throw new InvalidOperationException($"X: expected 3.0, got {result.MinX}/{result.MaxX}");
        if (result.MinY != 4.0 || result.MaxY != 4.0)
            throw new InvalidOperationException($"Y: expected 4.0, got {result.MinY}/{result.MaxY}");
    }

    [Test]
    public void OfPointsTransformed_Translate()
    {
        var a = Affine.Translate(5.0, -3.0);
        var pts = new[] { new Point(0.0, 0.0), new Point(10.0, 10.0) };
        var result = BatchAabb.OfPointsTransformed(a, pts);

        if (result.MinX != 5.0 || result.MaxX != 15.0)
            throw new InvalidOperationException($"X: expected [5, 15], got [{result.MinX}, {result.MaxX}]");
        if (result.MinY != -3.0 || result.MaxY != 7.0)
            throw new InvalidOperationException($"Y: expected [-3, 7], got [{result.MinY}, {result.MaxY}]");
    }

    [Test]
    public void OfPointsTransformed_Scale()
    {
        var a = Affine.Scale(2.0, 3.0);
        var pts = new[] { new Point(1.0, 1.0), new Point(3.0, 2.0) };
        var result = BatchAabb.OfPointsTransformed(a, pts);

        if (result.MinX != 2.0 || result.MaxX != 6.0)
            throw new InvalidOperationException($"X: expected [2, 6], got [{result.MinX}, {result.MaxX}]");
        if (result.MinY != 3.0 || result.MaxY != 6.0)
            throw new InvalidOperationException($"Y: expected [3, 6], got [{result.MinY}, {result.MaxY}]");
    }

    [Test]
    public void OfPointsTransformed_ZeroAlloc()
    {
        var a = Affine.Translate(1.5, -0.75);
        var pts = new Point[100];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Point(i, i * 2);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iter = 0; iter < 100; iter++)
        {
            BatchAabb.OfPointsTransformed(a, pts);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        if (delta > 0)
            throw new InvalidOperationException($"OfPointsTransformed allocated {delta} bytes (expected 0)");
    }

    [Test]
    public void OfPointsTransformed_Equivalence()
    {
        var a = Affine.Rotate(Math.PI / 6) * Affine.Translate(5.0, -3.0) * Affine.Scale(2.0, 0.5);
        var pts = new Point[50];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Point(i * 0.1, i * 0.2 - 1.0);

        var transformed = new Point[pts.Length];
        BatchTransform.TransformPoints(a, pts, transformed);
        var fromSeparate = BatchAabb.OfPoints(transformed);
        var fromFused = BatchAabb.OfPointsTransformed(a, pts);

        if (fromSeparate.MinX != fromFused.MinX || fromSeparate.MaxX != fromFused.MaxX)
            throw new InvalidOperationException($"X mismatch: separate={fromSeparate.MinX}/{fromSeparate.MaxX}, fused={fromFused.MinX}/{fromFused.MaxX}");
        if (fromSeparate.MinY != fromFused.MinY || fromSeparate.MaxY != fromFused.MaxY)
            throw new InvalidOperationException($"Y mismatch: separate={fromSeparate.MinY}/{fromSeparate.MaxY}, fused={fromFused.MinY}/{fromFused.MaxY}");
    }

    [Test]
    public void OfCurves_EmptyInput_ReturnsEmptySpans()
    {
        var curves = Array.Empty<CubicBez>();
        var outAabbs = Array.Empty<Rect>();
        BatchAabb.OfCurves(curves, outAabbs);
    }

    [Test]
    public void OfCurves_SingleCurve_HandComputed()
    {
        var curve = new CubicBez(
            new Point(0.0, 0.0),
            new Point(1.0, 0.0),
            new Point(1.0, 1.0),
            new Point(1.0, 2.0)
        );

        var outAabbs = new Rect[1];
        BatchAabb.OfCurves(new[] { curve }, outAabbs);

        var aabb = outAabbs[0];
        if (Math.Abs(aabb.MinX) > 1e-10 || Math.Abs(aabb.MaxX - 1.0) > 1e-10)
            throw new InvalidOperationException($"X: expected [0, 1], got [{aabb.MinX}, {aabb.MaxX}]");
        if (Math.Abs(aabb.MinY) > 1e-10 || Math.Abs(aabb.MaxY - 2.0) > 1e-10)
            throw new InvalidOperationException($"Y: expected [0, 2], got [{aabb.MinY}, {aabb.MaxY}]");
    }

    [Test]
    public void OfCurves_MultipleCurves()
    {
        var curves = new[]
        {
            new CubicBez(new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(1, 2)),
            new CubicBez(new Point(0, 0), new Point(0, 1), new Point(1, 1), new Point(2, 1)),
        };

        var outAabbs = new Rect[2];
        BatchAabb.OfCurves(curves, outAabbs);

        if (outAabbs[0].MinX != 0.0 || outAabbs[0].MaxX != 1.0 || outAabbs[0].MinY != 0.0 || outAabbs[0].MaxY != 2.0)
            throw new InvalidOperationException($"Curve 0: expected [0,1]x[0,2], got [{outAabbs[0].MinX},{outAabbs[0].MaxX}]x[{outAabbs[0].MinY},{outAabbs[0].MaxY}]");

        if (outAabbs[1].MinX != 0.0 || outAabbs[1].MaxX != 2.0 || outAabbs[1].MinY != 0.0 || outAabbs[1].MaxY != 1.0)
            throw new InvalidOperationException($"Curve 1: expected [0,2]x[0,1], got [{outAabbs[1].MinX},{outAabbs[1].MaxX}]x[{outAabbs[1].MinY},{outAabbs[1].MaxY}]");
    }

    [Test]
    public void OfCurves_SpanLengthMismatch_Panics()
    {
        bool threw = false;
        try
        {
            var curves = new CubicBez[3];
            var outAabbs = new Rect[2];
            BatchAabb.OfCurves(curves, outAabbs);
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.SpanLengthMismatch)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Mismatched span lengths must panic");
    }

    [Test]
    public void OfPoints_LargeArray()
    {
        int count = 4096;
        var pts = new Point[count];
        for (int i = 0; i < count; i++)
            pts[i] = new Point(i * 0.1, i * 0.2);

        var result = BatchAabb.OfPoints(pts);

        if (Math.Abs(result.MinX - 0.0) > 1e-10)
            throw new InvalidOperationException($"MinX: expected 0.0, got {result.MinX}");
        if (Math.Abs(result.MinY - 0.0) > 1e-10)
            throw new InvalidOperationException($"MinY: expected 0.0, got {result.MinY}");
        if (Math.Abs(result.MaxX - (count - 1) * 0.1) > 1e-10)
            throw new InvalidOperationException($"MaxX: expected {(count - 1) * 0.1}, got {result.MaxX}");
        if (Math.Abs(result.MaxY - (count - 1) * 0.2) > 1e-10)
            throw new InvalidOperationException($"MaxY: expected {(count - 1) * 0.2}, got {result.MaxY}");
    }

    [Test]
    public void OfPoints_AllSamePoint()
    {
        var pts = new Point[10];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Point(5.0, 5.0);

        var result = BatchAabb.OfPoints(pts);

        if (result.Width != 0.0 || result.Height != 0.0)
            throw new InvalidOperationException($"Degenerate rect: {result.Width}x{result.Height}");
        if (result.MinX != 5.0 || result.MaxX != 5.0 || result.MinY != 5.0 || result.MaxY != 5.0)
            throw new InvalidOperationException($"Wrong bounds: {result}");
    }
}
