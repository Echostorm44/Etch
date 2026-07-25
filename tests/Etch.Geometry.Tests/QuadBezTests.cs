using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class QuadBezTests
{
    [Test]
    public async Task EvalAtZeroIsStart()
    {
        var q = new QuadBez(new Point(3, 4), new Point(5, 6), new Point(7, 8));
        var result = q.Eval(0);
        if (!result.Equals(q.P0))
            throw new InvalidOperationException($"Eval(0) must be P0: got ({result.X}, {result.Y})");
    }

    [Test]
    public async Task EvalAtOneIsEnd()
    {
        var q = new QuadBez(new Point(3, 4), new Point(5, 6), new Point(7, 8));
        var result = q.Eval(1);
        if (!result.Equals(q.P2))
            throw new InvalidOperationException($"Eval(1) must be P2: got ({result.X}, {result.Y})");
    }

    [Test]
    public async Task EvalMidpoint()
    {
        var q = new QuadBez(new Point(0, 0), new Point(1, 2), new Point(3, 0));
        var result = q.Eval(0.5);
        if (Math.Abs(result.X - 1.25) > 1e-10 || Math.Abs(result.Y - 1) > 1e-10)
            throw new InvalidOperationException($"Midpoint eval wrong: got ({result.X}, {result.Y}), expected (1.25, 1)");
    }

    [Test]
    public async Task SubdivideMidpointReconstruction()
    {
        var q = new QuadBez(new Point(0, 0), new Point(1, 2), new Point(3, 0));
        var (left, right) = q.Subdivide();
        for (int i = 0; i <= 10; i++)
        {
            double t = i / 10.0;
            var fromOriginal = q.Eval(t);
            Point fromSubdivided;
            if (t <= 0.5)
                fromSubdivided = left.Eval(t * 2);
            else
                fromSubdivided = right.Eval((t - 0.5) * 2);

            if (Math.Abs(fromOriginal.X - fromSubdivided.X) > 0.001 || Math.Abs(fromOriginal.Y - fromSubdivided.Y) > 0.001)
                throw new InvalidOperationException($"Subdivide mismatch at t={t}: original=({fromOriginal.X:G},{fromOriginal.Y:G}), sub=({fromSubdivided.X:G},{fromSubdivided.Y:G})");
        }
    }

    [Test]
    public async Task ElevateMatchesQuadEval()
    {
        var q = new QuadBez(new Point(0, 0), new Point(1, 4), new Point(3, 0));
        var cubic = q.Elevate();
        for (int i = 0; i <= 10; i++)
        {
            double t = i / 10.0;
            var fromQuad = q.Eval(t);
            var fromCubic = cubic.Eval(t);
            if (Math.Abs(fromQuad.X - fromCubic.X) > 1e-14 || Math.Abs(fromQuad.Y - fromCubic.Y) > 1e-14)
                throw new InvalidOperationException($"Elevate mismatch at t={t}: quad=({fromQuad.X:G},{fromQuad.Y:G}), cubic=({fromCubic.X:G},{fromCubic.Y:G})");
        }
    }

    [Test]
    public async Task AabbContainsSampledPoints()
    {
        var q = new QuadBez(new Point(0, 0), new Point(5, 10), new Point(10, 0));
        var aabb = q.Aabb();
        for (int i = 0; i <= 1000; i++)
        {
            double t = i / 1000.0;
            var pt = q.Eval(t);
            if (!aabb.Contains(pt))
                throw new InvalidOperationException($"AABB does not contain sampled point at t={t}: {pt}");
        }
    }

    [Test]
    public async Task TransformedByAffine()
    {
        var q = new QuadBez(new Point(1, 0), new Point(1, 1), new Point(0, 1));
        var t = Affine.Rotate(Math.PI / 2);
        var qt = q.TransformedBy(t);
        for (int i = 0; i <= 10; i++)
        {
            double tt = i / 10.0;
            var fromTransformed = qt.Eval(tt);
            var fromOriginal = t * q.Eval(tt);
            if (Math.Abs(fromTransformed.X - fromOriginal.X) > 1e-10 || Math.Abs(fromTransformed.Y - fromOriginal.Y) > 1e-10)
                throw new InvalidOperationException($"TransformedBy mismatch at t={tt}");
        }
    }
}
