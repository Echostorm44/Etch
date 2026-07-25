using System;
using Etch.Geometry;
using Etch.Primitives;
using TUnit;

namespace Etch.Geometry.Oracle;

internal sealed class OracleSmokeTests
{
    [Test]
    public void AffineIdentityComposeRoundTrip()
    {
        var id = Affine.Identity;
        var composed = KurboOracle.Compose(id, id);
        if (composed.M00 != 1.0 || composed.M01 != 0.0 || composed.M10 != 0.0
            || composed.M11 != 1.0 || composed.M02 != 0.0 || composed.M12 != 0.0)
            throw new InvalidOperationException($"Identity compose failed: {composed}");
    }

    [Test]
    public void AffineComposeTranslate()
    {
        var tx = Affine.Translate(5.0, -3.0);
        var ty = Affine.Translate(10.0, 2.0);
        var composed = KurboOracle.Compose(tx, ty);
        if (composed.M02 != 15.0 || composed.M12 != -1.0)
            throw new InvalidOperationException($"Translate compose failed: expected M02=15, M12=-1, got M02={composed.M02}, M12={composed.M12}");
    }

    [Test]
    public void AffineInverse()
    {
        var a = Affine.Rotate(Math.PI / 4) * Affine.Translate(5, -3);
        var inv = KurboOracle.Inverse(a);
        var composed = KurboOracle.Compose(a, inv);
        if (Math.Abs(composed.M00 - 1.0) > 1e-10 || Math.Abs(composed.M11 - 1.0) > 1e-10
            || Math.Abs(composed.M02) > 1e-10 || Math.Abs(composed.M12) > 1e-10)
            throw new InvalidOperationException($"Inverse compose should be identity, got: {composed}");
    }

    [Test]
    public void CubicEvalAtT0()
    {
        var cubic = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));
        var pt = KurboOracle.CubicEval(cubic, 0.0);
        if (Math.Abs(pt.X) > 1e-10 || Math.Abs(pt.Y) > 1e-10)
            throw new InvalidOperationException($"CubicEval at t=0 should be start point, got ({pt.X},{pt.Y})");
    }

    [Test]
    public void CubicEvalAtT1()
    {
        var cubic = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));
        var pt = KurboOracle.CubicEval(cubic, 1.0);
        if (Math.Abs(pt.X - 3.0) > 1e-10 || Math.Abs(pt.Y) > 1e-10)
            throw new InvalidOperationException($"CubicEval at t=1 should be end point, got ({pt.X},{pt.Y})");
    }

    [Test]
    public void CubicEvalMidpoint()
    {
        var cubic = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));
        var pt = KurboOracle.CubicEval(cubic, 0.5);
        double expectedX = 1.5;
        double expectedY = 1.125;
        if (Math.Abs(pt.X - expectedX) > 1e-6 || Math.Abs(pt.Y - expectedY) > 1e-6)
            throw new InvalidOperationException($"CubicEval at t=0.5 expected ({expectedX},{expectedY}), got ({pt.X},{pt.Y})");
    }

    [Test]
    public void CubicSubdivide()
    {
        var cubic = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));
        var (left, right) = KurboOracle.CubicSubdivide(cubic, 0.5);
        if (Math.Abs(left.P0.X) > 1e-10 || Math.Abs(left.P0.Y) > 1e-10)
            throw new InvalidOperationException($"Left should start at origin, got {left.P0}");
        if (Math.Abs(right.P3.X - 3.0) > 1e-10 || Math.Abs(right.P3.Y) > 1e-10)
            throw new InvalidOperationException($"Right should end at (3,0), got {right.P3}");
    }

    [Test]
    public void CubicAabb()
    {
        var cubic = new CubicBez(new Point(0, 0), new Point(1, 2), new Point(2, 1), new Point(3, 0));
        var aabb = KurboOracle.CubicAabb(cubic);
        if (aabb.MinX < 0 || aabb.MaxX < 3 || aabb.MinY < 0 || aabb.MaxY < 0)
            throw new InvalidOperationException($"CubicAabb bounds seem wrong: {aabb}");
    }

    [Test]
    public void PointTransformTranslate()
    {
        var a = Affine.Translate(5.0, -3.0);
        var src = new Point[] { new Point(0, 0), new Point(1, 1), new Point(-1, 2) };
        var dst = new Point[3];
        KurboOracle.TransformPoints(a, src, dst);
        if (dst[0].X != 5 || dst[0].Y != -3)
            throw new InvalidOperationException($"Transform failed at [0]: expected (5,-3), got ({dst[0].X},{dst[0].Y})");
        if (dst[1].X != 6 || dst[1].Y != -2)
            throw new InvalidOperationException($"Transform failed at [1]: expected (6,-2), got ({dst[1].X},{dst[1].Y})");
        if (dst[2].X != 4 || dst[2].Y != -1)
            throw new InvalidOperationException($"Transform failed at [2]: expected (4,-1), got ({dst[2].X},{dst[2].Y})");
    }
}
