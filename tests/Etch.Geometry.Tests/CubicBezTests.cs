using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class CubicBezTests
{
    [Test]
    public async Task EvalAtZeroIsStart()
    {
        var c = new CubicBez(new Point(1, 2), new Point(3, 4), new Point(5, 6), new Point(7, 8));
        var result = c.Eval(0);
        if (!result.Equals(c.P0))
            throw new InvalidOperationException($"Eval(0) must be P0");
    }

    [Test]
    public async Task EvalAtOneIsEnd()
    {
        var c = new CubicBez(new Point(1, 2), new Point(3, 4), new Point(5, 6), new Point(7, 8));
        var result = c.Eval(1);
        if (!result.Equals(c.P3))
            throw new InvalidOperationException($"Eval(1) must be P3");
    }

    [Test]
    public async Task SubdivideMidpointReconstruction()
    {
        var c = new CubicBez(new Point(0, 0), new Point(1, 3), new Point(2, 3), new Point(3, 0));
        var (left, right) = c.Subdivide();
        for (int i = 0; i <= 10; i++)
        {
            double t = i / 10.0;
            var fromOriginal = c.Eval(t);
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
    public async Task AabbContainsSampledPoints()
    {
        var c = new CubicBez(new Point(0, 0), new Point(1, 5), new Point(2, 5), new Point(3, 0));
        var aabb = c.Aabb();
        for (int i = 0; i <= 1000; i++)
        {
            double t = i / 1000.0;
            var pt = c.Eval(t);
            if (!aabb.Contains(pt))
                throw new InvalidOperationException($"AABB does not contain sampled point at t={t}: {pt}");
        }
    }

    [Test]
    public async Task TransformedByAffine()
    {
        var c = new CubicBez(new Point(1, 0), new Point(1, 1), new Point(0, 1), new Point(0, 0));
        var t = Affine.Rotate(Math.PI);
        var ct = c.TransformedBy(t);
        for (int i = 0; i <= 10; i++)
        {
            double tt = i / 10.0;
            var fromTransformed = ct.Eval(tt);
            var fromOriginal = t * c.Eval(tt);
            if (Math.Abs(fromTransformed.X - fromOriginal.X) > 1e-10 || Math.Abs(fromTransformed.Y - fromOriginal.Y) > 1e-10)
                throw new InvalidOperationException($"TransformedBy mismatch at t={tt}: transformed=({fromTransformed.X:G},{fromTransformed.Y:G}), manual=({fromOriginal.X:G},{fromOriginal.Y:G})");
        }
    }

    [Test]
    public async Task ReverseEval()
    {
        var c = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(3, 2), new Point(4, 0));
        var rev = c.Reverse();
        for (int i = 0; i <= 10; i++)
        {
            double t = i / 10.0;
            var fromReverse = rev.Eval(t);
            var fromOriginal = c.Eval(1 - t);
            if (Math.Abs(fromReverse.X - fromOriginal.X) > 1e-10 || Math.Abs(fromReverse.Y - fromOriginal.Y) > 1e-10)
                throw new InvalidOperationException($"Reverse mismatch at t={t}");
        }
    }
}
