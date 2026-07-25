using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class AffineTests
{
    [Test]
    public async Task IdentityTimesPointIsUnchanged()
    {
        var p = new Point(3.0, 4.0);
        var result = Affine.Identity * p;
        if (Math.Abs(result.X - p.X) > 1e-10 || Math.Abs(result.Y - p.Y) > 1e-10)
            throw new InvalidOperationException("Identity * Point must be unchanged");
    }

    [Test]
    public async Task IdentityTimesVec2IsUnchanged()
    {
        var v = new Vec2(3.0, 4.0);
        var result = Affine.Identity * v;
        if (Math.Abs(result.X - v.X) > 1e-10 || Math.Abs(result.Y - v.Y) > 1e-10)
            throw new InvalidOperationException("Identity * Vec2 must be unchanged");
    }

    [Test]
    public async Task LeftIdentityHolds()
    {
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            var a = RandomAffine(rng);
            var result = Affine.Identity * a;
            if (!result.Equals(a))
                throw new InvalidOperationException($"Left identity failed on iteration {i}");
        }
    }

    [Test]
    public async Task RightIdentityHolds()
    {
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            var a = RandomAffine(rng);
            var result = a * Affine.Identity;
            if (!result.Equals(a))
                throw new InvalidOperationException($"Right identity failed on iteration {i}");
        }
    }

    [Test]
    public async Task TranslateOriginIsOffset()
    {
        var offset = new Vec2(5.0, -3.0);
        var t = Affine.Translate(offset);
        var p = Point.Origin;
        var result = t * p;
        if (Math.Abs(result.X - offset.X) > 1e-10 || Math.Abs(result.Y - offset.Y) > 1e-10)
            throw new InvalidOperationException("Translate(v) applied to Origin must equal Origin + v");
    }

    [Test]
    public async Task RotatePiTwiceIsIdentity()
    {
        var r = Affine.Rotate(Math.PI);
        var composed = r * r;
        double maxDiff = MaxComponentDiff(composed, Affine.Identity);
        if (maxDiff > 1e-10)
            throw new InvalidOperationException($"Rotate(π) * Rotate(π) differs from Identity by {maxDiff:G}");
    }

    [Test]
    public async Task DeterminantOfIdentityIsOne()
    {
        double det = Affine.Identity.Determinant();
        if (Math.Abs(det - 1.0) > 1e-10)
            throw new InvalidOperationException("Identity determinant must be 1");
    }

    [Test]
    public async Task DeterminantOfScaleIsSxTimesSy()
    {
        double sx = 2.5, sy = 3.0;
        double det = Affine.Scale(sx, sy).Determinant();
        if (Math.Abs(det - sx * sy) > 1e-10)
            throw new InvalidOperationException($"Scale determinant must be sx*sy, got {det}");
    }

    [Test]
    public async Task InverseOfIdentityIsIdentity()
    {
        var inv = Affine.Identity.Inverse();
        if (!inv.Equals(Affine.Identity))
            throw new InvalidOperationException("Inverse of Identity must be Identity");
    }

    [Test]
    public async Task InverseRoundTrip()
    {
        var a = new Affine(2.0, 1.0, 0.5, 1.5, 3.0, 4.0);
        var inv = a.Inverse();
        var roundTrip = inv * a;
        double maxDiff = MaxComponentDiff(roundTrip, Affine.Identity);
        if (maxDiff > 1e-9)
            throw new InvalidOperationException($"A.Inverse() * A differs from Identity by {maxDiff:G}");
    }

    [Test]
    public async Task InverseCompositionLaw()
    {
        var a = new Affine(2.0, 1.0, 0.5, 1.5, 3.0, 4.0);
        var b = new Affine(1.5, -0.5, 0.8, 2.0, -1.0, 2.5);
        var abInv = (a * b).Inverse();
        var bInvAInv = b.Inverse() * a.Inverse();
        double maxDiff = MaxComponentDiff(abInv, bInvAInv);
        if (maxDiff > 1e-9)
            throw new InvalidOperationException($"(a*b).Inverse() differs from b.Inverse() * a.Inverse() by {maxDiff:G}");
    }

    [Test]
    public async Task SingularAffineInversePanics()
    {
        var singular = Affine.Scale(0.0, 0.0);
        bool threw = false;
        try
        {
            _ = singular.Inverse();
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.NonInvertibleAffine)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Inverse on singular (zero-scale) matrix must panic NonInvertibleAffine");
    }

    [Test]
    public async Task NearSingularAffineInversePanics()
    {
        var nearSingular = Affine.Scale(1e-14, 1.0);
        bool threw = false;
        try
        {
            _ = nearSingular.Inverse();
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.NonInvertibleAffine)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Inverse on near-singular matrix must panic NonInvertibleAffine");
    }

    [Test]
    public async Task TransformPointConsistentWithComposition()
    {
        var r = Affine.Rotate(Math.PI / 4);
        var t = Affine.Translate(3.0, 4.0);
        var a = r * t;
        var p = new Point(1.0, 0.0);
        Point step1 = t * p;
        Point step2 = r * step1;
        var actual = a * p;
        if (Math.Abs(actual.X - step2.X) > 1e-10 || Math.Abs(actual.Y - step2.Y) > 1e-10)
            throw new InvalidOperationException($"Transform inconsistent: expected ({step2.X:G}, {step2.Y:G}), got ({actual.X:G}, {actual.Y:G})");
    }

    [Test]
    public async Task Vec2TransformIgnoresTranslation()
    {
        var t = Affine.Translate(10.0, 20.0);
        var v = new Vec2(3.0, 4.0);
        var result = t * v;
        if (Math.Abs(result.X - v.X) > 1e-10 || Math.Abs(result.Y - v.Y) > 1e-10)
            throw new InvalidOperationException("Transforming a Vec2 must ignore translation");
    }

    [Test]
    public async Task PreAndPostTranslateDifference()
    {
        var a = Affine.Rotate(Math.PI / 6);
        var t = new Vec2(1.0, 2.0);
        var preResult = a.PreTranslate(t);
        var postResult = a.PostTranslate(t);
        if (preResult.Equals(postResult))
            throw new InvalidOperationException("PreTranslate and PostTranslate must produce different results");
    }

    [Test]
    public async Task ToStringContainsAllComponents()
    {
        var a = new Affine(1, 2, 3, 4, 5, 6);
        string s = a.ToString();
        if (!s.Contains("1") || !s.Contains("2") || !s.Contains("3") ||
            !s.Contains("4") || !s.Contains("5") || !s.Contains("6"))
            throw new InvalidOperationException($"ToString must contain all 6 components, got: {s}");
    }

    private static Affine RandomAffine(Random rng)
    {
        return new Affine(
            rng.NextDouble() * 4 - 2,
            rng.NextDouble() * 4 - 2,
            rng.NextDouble() * 4 - 2,
            rng.NextDouble() * 4 - 2,
            rng.NextDouble() * 10 - 5,
            rng.NextDouble() * 10 - 5);
    }

    private static double MaxComponentDiff(Affine a, Affine b)
    {
        double d00 = Math.Abs(a.M00 - b.M00);
        double d01 = Math.Abs(a.M01 - b.M01);
        double d10 = Math.Abs(a.M10 - b.M10);
        double d11 = Math.Abs(a.M11 - b.M11);
        double d02 = Math.Abs(a.M02 - b.M02);
        double d12 = Math.Abs(a.M12 - b.M12);
        return Math.Max(d00, Math.Max(d01, Math.Max(d10,
            Math.Max(d11, Math.Max(d02, d12)))));
    }
}
