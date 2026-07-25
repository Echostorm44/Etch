using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using Etch.Primitives;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class BatchTransformTests
{
    [Test]
    public void ScalarBasicTranslate()
    {
        var a = Affine.Translate(5.0, -3.0);
        Point[] src = { new Point(0, 0), new Point(10, 0), new Point(10, 10) };
        Point[] dst = new Point[3];

        BatchTransform.TransformPoints(a, src, dst);

        if (dst[0].X != 5 || dst[0].Y != -3)
            throw new InvalidOperationException($"Expected (5,-3), got ({dst[0].X},{dst[0].Y})");
        if (dst[1].X != 15 || dst[1].Y != -3)
            throw new InvalidOperationException($"Expected (15,-3), got ({dst[1].X},{dst[1].Y})");
        if (dst[2].X != 15 || dst[2].Y != 7)
            throw new InvalidOperationException($"Expected (15,7), got ({dst[2].X},{dst[2].Y})");
    }

    [Test]
    public void ScalarBasicScale()
    {
        var a = Affine.Scale(2.0, 3.0);
        Point[] src = { new Point(1, 1), new Point(2, 3) };
        Point[] dst = new Point[2];

        BatchTransform.TransformPoints(a, src, dst);

        if (dst[0].X != 2 || dst[0].Y != 3)
            throw new InvalidOperationException($"Expected (2,3), got ({dst[0].X},{dst[0].Y})");
        if (dst[1].X != 4 || dst[1].Y != 9)
            throw new InvalidOperationException($"Expected (4,9), got ({dst[1].X},{dst[1].Y})");
    }

    [Test]
    public void Vec2Transform()
    {
        var a = Affine.Rotate(Math.PI / 4);
        Vec2[] src = { new Vec2(1, 0), new Vec2(0, 1) };
        Vec2[] dst = new Vec2[2];

        BatchTransform.TransformVec2(a, src, dst);

        double cos45 = Math.Cos(Math.PI / 4);
        double expected = Math.Sqrt(2) / 2;
        if (Math.Abs(dst[0].X - expected) > 1e-10 || Math.Abs(dst[0].Y - expected) > 1e-10)
            throw new InvalidOperationException($"Vec2 rotate (1,0) expected (~{expected},~{expected}), got ({dst[0].X},{dst[0].Y})");
        if (Math.Abs(dst[1].X + expected) > 1e-10 || Math.Abs(dst[1].Y - expected) > 1e-10)
            throw new InvalidOperationException($"Vec2 rotate (0,1) expected (~{-expected},~{expected}), got ({dst[1].X},{dst[1].Y})");
    }

    [Test]
    public void InPlaceAliasing()
    {
        var a = Affine.Translate(5.0, -3.0);
        Point[] pts = { new Point(0, 0), new Point(10, 0), new Point(10, 10) };

        BatchTransform.TransformInPlace(a, pts);

        if (pts[0].X != 5 || pts[0].Y != -3)
            throw new InvalidOperationException($"In-place translate failed at [0]");
        if (pts[1].X != 15 || pts[1].Y != -3)
            throw new InvalidOperationException($"In-place translate failed at [1]");
        if (pts[2].X != 15 || pts[2].Y != 7)
            throw new InvalidOperationException($"In-place translate failed at [2]");
    }

    [Test]
    public void SpanLengthMismatchPanics()
    {
        bool threw = false;
        try
        {
            Point[] src = { new Point(0, 0), new Point(1, 1) };
            Point[] dst = { new Point(0, 0) };
            BatchTransform.TransformPoints(Affine.Identity, src, dst);
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.SpanLengthMismatch)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Mismatched span lengths must panic SpanLengthMismatch");
    }

    [Test]
    public void Vec2SpanLengthMismatchPanics()
    {
        bool threw = false;
        try
        {
            Vec2[] src = { new Vec2(0, 0), new Vec2(1, 1) };
            Vec2[] dst = { new Vec2(0, 0) };
            BatchTransform.TransformVec2(Affine.Identity, src, dst);
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.SpanLengthMismatch)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Mismatched Vec2 span lengths must panic SpanLengthMismatch");
    }

    [Test]
    public void ZeroAllocTransform()
    {
        var a = Affine.Translate(5.0, -3.0);
        Point[] src = new Point[100];
        for (int i = 0; i < src.Length; i++) src[i] = new Point(i, i);
        Span<Point> dst = new Point[100];

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iter = 0; iter < 100; iter++)
        {
            BatchTransform.TransformPoints(a, src, dst);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        if (delta > 0)
            throw new InvalidOperationException($"TransformPoints allocated {delta} bytes (expected 0)");
    }

    [Test]
    public void ZeroAllocVec2Transform()
    {
        var a = Affine.Scale(2.0, 0.5);
        Vec2[] src = new Vec2[100];
        for (int i = 0; i < src.Length; i++) src[i] = new Vec2(i, i * 2);
        Span<Vec2> dst = new Vec2[100];

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iter = 0; iter < 100; iter++)
        {
            BatchTransform.TransformVec2(a, src, dst);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        if (delta > 0)
            throw new InvalidOperationException($"TransformVec2 allocated {delta} bytes (expected 0)");
    }

    [Test]
    public void CrossPathEquivalence()
    {
        var transforms = new[]
        {
            Affine.Identity,
            Affine.Translate(5.0, -3.0),
            Affine.Scale(2.0, 3.0),
            Affine.Rotate(Math.PI / 6),
            Affine.Rotate(Math.PI / 4) * Affine.Translate(10, 20),
            Affine.Scale(0.5, 2.0) * Affine.Rotate(Math.PI / 3),
        };

        var inputs = new[]
        {
            new Point(0, 0),
            new Point(1, 0),
            new Point(0, 1),
            new Point(1, 1),
            new Point(-1, -1),
            new Point(100, -50),
            new Point(0.5, 0.25),
            new Point(Math.PI, Math.E),
        };

        foreach (var a in transforms)
        {
            Point[] src = new Point[inputs.Length];
            inputs.CopyTo(src, 0);

            Point[] dstScalar = new Point[inputs.Length];
            Point[] dstInPlace = new Point[inputs.Length];
            inputs.CopyTo(dstInPlace, 0);

            BatchTransform.TransformPoints(a, src, dstScalar);
            BatchTransform.TransformInPlace(a, dstInPlace);

            for (int i = 0; i < inputs.Length; i++)
            {
                double expectedX = a.M00 * inputs[i].X + a.M01 * inputs[i].Y + a.M02;
                double expectedY = a.M10 * inputs[i].X + a.M11 * inputs[i].Y + a.M12;

                if (dstScalar[i].X != expectedX || dstScalar[i].Y != expectedY)
                    throw new InvalidOperationException($"TransformPoints[{i}] produced ({dstScalar[i].X},{dstScalar[i].Y}), expected ({expectedX},{expectedY})");
                if (dstInPlace[i].X != expectedX || dstInPlace[i].Y != expectedY)
                    throw new InvalidOperationException($"TransformInPlace[{i}] produced ({dstInPlace[i].X},{dstInPlace[i].Y}), expected ({expectedX},{expectedY})");
            }
        }
    }

    [Test]
    public void Vec2CrossPathEquivalence()
    {
        var transforms = new[]
        {
            Affine.Identity,
            Affine.Scale(2.0, 3.0),
            Affine.Rotate(Math.PI / 4),
            Affine.Scale(0.5, 2.0) * Affine.Rotate(Math.PI / 3),
        };

        var inputs = new[]
        {
            new Vec2(0, 0),
            new Vec2(1, 0),
            new Vec2(0, 1),
            new Vec2(1, 1),
            new Vec2(-1, -1),
            new Vec2(100, -50),
            new Vec2(0.5, 0.25),
        };

        foreach (var a in transforms)
        {
            Vec2[] src = new Vec2[inputs.Length];
            inputs.CopyTo(src, 0);

            Vec2[] dst = new Vec2[inputs.Length];

            BatchTransform.TransformVec2(a, src, dst);

            for (int i = 0; i < inputs.Length; i++)
            {
                double expectedX = a.M00 * inputs[i].X + a.M01 * inputs[i].Y;
                double expectedY = a.M10 * inputs[i].X + a.M11 * inputs[i].Y;

                if (dst[i].X != expectedX || dst[i].Y != expectedY)
                    throw new InvalidOperationException($"TransformVec2[{i}] produced ({dst[i].X},{dst[i].Y}), expected ({expectedX},{expectedY})");
            }
        }
    }

    [Test]
    public void LargeArrayTransform()
    {
        var a = Affine.Translate(1.5, -0.75);
        int count = 4096;
        Point[] src = new Point[count];
        for (int i = 0; i < count; i++) src[i] = new Point(i * 0.1, i * 0.2);
        Point[] dst = new Point[count];

        BatchTransform.TransformPoints(a, src, dst);

        for (int i = 0; i < count; i++)
        {
            double expectedX = i * 0.1 + 1.5;
            double expectedY = i * 0.2 - 0.75;
            if (Math.Abs(dst[i].X - expectedX) > 1e-10 || Math.Abs(dst[i].Y - expectedY) > 1e-10)
                throw new InvalidOperationException($"LargeArray transform failed at index {i}");
        }
    }

    [Test]
    public void EmptySpan()
    {
        Point[] src = Array.Empty<Point>();
        Point[] dst = Array.Empty<Point>();
        BatchTransform.TransformPoints(Affine.Identity, src, dst);
    }
}
