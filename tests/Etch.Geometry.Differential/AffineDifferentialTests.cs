using System;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using Etch.Geometry.Differential;
using Etch.Geometry.Oracle;
using TUnit;

namespace Etch.Geometry.Differential;

internal sealed class AffineDifferentialTests
{
    [Test]
    public async Task Compose_MatchesKurbo()
    {
        var runner = new DifferentialRunner(nameof(Compose_MatchesKurbo));
        bool passed = runner.Run(
            iterations: 10_000,
            generate: RandomAffinePair,
            property: pair =>
            {
                Affine ours = pair.A * pair.B;
                Affine oracle = KurboOracle.Compose(pair.A, pair.B);
                return AffineClose(ours, oracle, Tolerances.ComposeEpsilon);
            });

        if (!passed)
            throw new InvalidOperationException(runner.FailMessage);
    }

    [Test]
    public async Task Inverse_MatchesKurbo()
    {
        var runner = new DifferentialRunner(nameof(Inverse_MatchesKurbo), rejectReason: "singular");
        bool passed = runner.Run(
            iterations: 10_000,
            generate: RandomAffine,
            property: a =>
            {
                Affine ours = a.Inverse();
                Affine oracle = KurboOracle.Inverse(a);
                return AffineClose(ours, oracle, Tolerances.InverseEpsilon);
            });

        if (!passed)
            throw new InvalidOperationException(runner.FailMessage);
    }

    [Test]
    public async Task PointTransform_MatchesKurbo()
    {
        var runner = new DifferentialRunner(nameof(PointTransform_MatchesKurbo));
        bool passed = runner.Run(
            iterations: 1_000,
            generate: RandomAffinePointPair,
            property: pair =>
            {
                Point ours = pair.Affine.Transform(pair.Point);
                Span<Point> src = stackalloc Point[] { pair.Point };
                Span<Point> dst = stackalloc Point[1];
                KurboOracle.TransformPoints(pair.Affine, src, dst);
                Point oracle = dst[0];
                return PointClose(ours, oracle, Tolerances.PointTransformEpsilon);
            });

        if (!passed)
            throw new InvalidOperationException(runner.FailMessage);
    }

    [Test]
    public async Task DegenerateInputsPanicConsistently()
    {
        var singular = Affine.Scale(0.0, 0.0);

        bool oursThrew = false;
        try
        {
            _ = singular.Inverse();
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.NonInvertibleAffine)
        {
            oursThrew = true;
        }

        Affine oracleResult = KurboOracle.Inverse(singular);

        if (!oursThrew)
            throw new InvalidOperationException("Our implementation must panic ET-P-0302 for singular affine");
    }

    private static Affine RandomAffine(Random rng)
    {
        while (true)
        {
            var a = new Affine(
                rng.NextDouble() * 4 - 2,
                rng.NextDouble() * 4 - 2,
                rng.NextDouble() * 4 - 2,
                rng.NextDouble() * 4 - 2,
                rng.NextDouble() * 1000 - 500,
                rng.NextDouble() * 1000 - 500);
            if (Math.Abs(a.Determinant()) > 1e-3)
                return a;
        }
    }

    private static AffinePair RandomAffinePair(Random rng) => new(RandomAffine(rng), RandomAffine(rng));

    private static AffinePointPair RandomAffinePointPair(Random rng) =>
        new(RandomAffine(rng), new Point(rng.NextDouble() * 1000 - 500, rng.NextDouble() * 1000 - 500));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool AffineClose(Affine ours, Affine oracle, double epsilon)
    {
        return RelativeComparer.NearlyEqual(ours.M00, oracle.M00, epsilon) &&
               RelativeComparer.NearlyEqual(ours.M01, oracle.M01, epsilon) &&
               RelativeComparer.NearlyEqual(ours.M10, oracle.M10, epsilon) &&
               RelativeComparer.NearlyEqual(ours.M11, oracle.M11, epsilon) &&
               RelativeComparer.NearlyEqual(ours.M02, oracle.M02, epsilon) &&
               RelativeComparer.NearlyEqual(ours.M12, oracle.M12, epsilon);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PointClose(Point ours, Point oracle, double epsilon)
    {
        return RelativeComparer.NearlyEqual(ours.X, oracle.X, epsilon) &&
               RelativeComparer.NearlyEqual(ours.Y, oracle.Y, epsilon);
    }

    private readonly struct AffinePair
    {
        public readonly Affine A;
        public readonly Affine B;
        public AffinePair(Affine a, Affine b) { A = a; B = b; }
    }

    private readonly struct AffinePointPair
    {
        public readonly Affine Affine;
        public readonly Point Point;
        public AffinePointPair(Affine affine, Point point) { Affine = affine; Point = point; }
    }
}