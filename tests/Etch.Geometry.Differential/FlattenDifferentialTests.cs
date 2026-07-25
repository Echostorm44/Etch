using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Geometry.Oracle;
using TUnit;

namespace Etch.Geometry.Differential;

internal sealed class FlattenDifferentialTests
{
    private const double Tolerance = 0.25;

    [Test]
    public async Task Flatten_MatchesKurbo_Cubic()
    {
        var runner = new DifferentialRunner(nameof(Flatten_MatchesKurbo_Cubic));
        bool passed = runner.Run(
            iterations: 1_000,
            generate: RandomWellConditionedCubic,
            property: cubic =>
            {
                Span<Point> ours = stackalloc Point[8192];
                var sink = new FlattenSink(ours);
                CurveFlattener.CubicBez(cubic, Tolerance, ref sink);
                var oursSlice = ours[..sink.Count];

                var oracle = KurboOracle.CubicFlatten(cubic, Tolerance);

                double hausdorff = Hausdorff.Distance(oursSlice, oracle);
                bool hausdorffOk = hausdorff <= 2 * Tolerance;
                bool countOk = SegmentCountWithin(oursSlice.Length, oracle.Count, 0.5, 2.0);

                if (!countOk)
                    Console.WriteLine($"Flatten segment count diverged: ours={oursSlice.Length}, oracle={oracle.Count}");

                if (!hausdorffOk)
                    Console.WriteLine($"Flatten Hausdorff exceeded: {hausdorff} > {2 * Tolerance}");

                return hausdorffOk && countOk;
            });

        if (!passed)
            throw new InvalidOperationException(runner.FailMessage);
    }

    [Test]
    public async Task Flatten_MatchesKurbo_Quad()
    {
        var runner = new DifferentialRunner(nameof(Flatten_MatchesKurbo_Quad));
        bool passed = runner.Run(
            iterations: 1_000,
            generate: RandomWellConditionedQuad,
            property: quad =>
            {
                Span<Point> ours = stackalloc Point[8192];
                var sink = new FlattenSink(ours);
                CurveFlattener.QuadBez(quad, Tolerance, ref sink);
                var oursSlice = ours[..sink.Count];

                var oracle = KurboOracle.QuadFlatten(quad, Tolerance);

                double hausdorff = Hausdorff.Distance(oursSlice, oracle);
                bool hausdorffOk = hausdorff <= 2 * Tolerance;
                bool countOk = SegmentCountWithin(oursSlice.Length, oracle.Count, 0.5, 2.0);

                if (!countOk)
                    Console.WriteLine($"Flatten segment count diverged: ours={oursSlice.Length}, oracle={oracle.Count}");

                if (!hausdorffOk)
                    Console.WriteLine($"Flatten Hausdorff exceeded: {hausdorff} > {2 * Tolerance}");

                return hausdorffOk && countOk;
            });

        if (!passed)
            throw new InvalidOperationException(runner.FailMessage);
    }

    [Test]
    public async Task Flatten_MatchesKurbo_BezPath()
    {
        var runner = new DifferentialRunner(nameof(Flatten_MatchesKurbo_BezPath));
        bool passed = runner.Run(
            iterations: 500,
            generate: RandomBezPath,
            property: path =>
            {
                Span<Point> ours = stackalloc Point[8192];
                var sink = new FlattenSink(ours);
                CurveFlattener.BezPath(path, Tolerance, ref sink);
                var oursSlice = ours[..sink.Count];

                var oracle = FlattenBezPath(path, Tolerance);

                double hausdorff = Hausdorff.Distance(oursSlice, oracle);
                bool hausdorffOk = hausdorff <= 2 * Tolerance;
                bool countOk = SegmentCountWithin(oursSlice.Length, oracle.Count, 0.5, 2.0);

                if (!countOk)
                    Console.WriteLine($"Flatten segment count diverged: ours={oursSlice.Length}, oracle={oracle.Count}");

                if (!hausdorffOk)
                    Console.WriteLine($"Flatten Hausdorff exceeded: {hausdorff} > {2 * Tolerance}");

                return hausdorffOk && countOk;
            });

        if (!passed)
            throw new InvalidOperationException(runner.FailMessage);
    }

    [Test]
    public async Task Flatten_BrokenFlattener_Cubic_FailsWithHausdorff()
    {
        const double brokenTolerance = Tolerance * 10.0;
        int hausdorffFailures = 0;
        int countFailures = 0;

        var runner = new DifferentialRunner(nameof(Flatten_BrokenFlattener_Cubic_FailsWithHausdorff));
        runner.Run(
            iterations: 1_000,
            generate: RandomWellConditionedCubic,
            property: cubic =>
            {
                Span<Point> ours = stackalloc Point[8192];
                var sink = new FlattenSink(ours);
                CurveFlattener.CubicBez(cubic, brokenTolerance, ref sink);
                var oursSlice = ours[..sink.Count];

                var oracle = KurboOracle.CubicFlatten(cubic, Tolerance);

                double hausdorff = Hausdorff.Distance(oursSlice, oracle);
                bool hausdorffOk = hausdorff <= 2 * Tolerance;
                bool countOk = SegmentCountWithin(oursSlice.Length, oracle.Count, 0.5, 2.0);

                if (!hausdorffOk) hausdorffFailures++;
                if (!countOk) countFailures++;

                return true;
            });

        if (hausdorffFailures < 10)
            throw new InvalidOperationException($"Expected at least 10 Hausdorff failures, got {hausdorffFailures}. Count failures: {countFailures}");
    }

    private static CubicBez RandomWellConditionedCubic(Random rng)
    {
        while (true)
        {
            double x0 = rng.NextDouble() * 1000 - 500;
            double y0 = rng.NextDouble() * 1000 - 500;
            double x1 = rng.NextDouble() * 1000 - 500;
            double y1 = rng.NextDouble() * 1000 - 500;
            double x2 = rng.NextDouble() * 1000 - 500;
            double y2 = rng.NextDouble() * 1000 - 500;
            double x3 = rng.NextDouble() * 1000 - 500;
            double y3 = rng.NextDouble() * 1000 - 500;

            var p0 = new Point(x0, y0);
            var p1 = new Point(x1, y1);
            var p2 = new Point(x2, y2);
            var p3 = new Point(x3, y3);

            double d01 = (p1 - p0).Length;
            double d12 = (p2 - p1).Length;
            double d23 = (p3 - p2).Length;
            double d03 = (p3 - p0).Length;

            if (d01 < 1e-6 || d12 < 1e-6 || d23 < 1e-6 || d03 < 1e-6)
                continue;
            if (d01 > 1e6 || d12 > 1e6 || d23 > 1e6 || d03 > 1e6)
                continue;

            return new CubicBez(p0, p1, p2, p3);
        }
    }

    private static QuadBez RandomWellConditionedQuad(Random rng)
    {
        while (true)
        {
            double x0 = rng.NextDouble() * 1000 - 500;
            double y0 = rng.NextDouble() * 1000 - 500;
            double x1 = rng.NextDouble() * 1000 - 500;
            double y1 = rng.NextDouble() * 1000 - 500;
            double x2 = rng.NextDouble() * 1000 - 500;
            double y2 = rng.NextDouble() * 1000 - 500;

            var p0 = new Point(x0, y0);
            var p1 = new Point(x1, y1);
            var p2 = new Point(x2, y2);

            double d01 = (p1 - p0).Length;
            double d12 = (p2 - p1).Length;
            double d02 = (p2 - p0).Length;

            if (d01 < 1e-6 || d12 < 1e-6 || d02 < 1e-6)
                continue;
            if (d01 > 1e6 || d12 > 1e6 || d02 > 1e6)
                continue;

            return new QuadBez(p0, p1, p2);
        }
    }

    private static BezPath RandomBezPath(Random rng)
    {
        int verbCount = rng.Next(4, 17);
        var builder = BezPathBuilder.Begin(verbCount * 3);

        double x = rng.NextDouble() * 100 - 50;
        double y = rng.NextDouble() * 100 - 50;
        builder.MoveTo(new Point(x, y));

        for (int i = 0; i < verbCount; i++)
        {
            int verb = rng.Next(0, 4);
            switch (verb)
            {
                case 0:
                    x = rng.NextDouble() * 100 - 50;
                    y = rng.NextDouble() * 100 - 50;
                    builder.MoveTo(new Point(x, y));
                    break;
                case 1:
                    x = rng.NextDouble() * 100 - 50;
                    y = rng.NextDouble() * 100 - 50;
                    builder.LineTo(new Point(x, y));
                    break;
                case 2:
                    {
                        double cx = rng.NextDouble() * 100 - 50;
                        double cy = rng.NextDouble() * 100 - 50;
                        double ex = rng.NextDouble() * 100 - 50;
                        double ey = rng.NextDouble() * 100 - 50;
                        builder.QuadTo(new Point(cx, cy), new Point(ex, ey));
                        x = ex;
                        y = ey;
                    }
                    break;
                case 3:
                    {
                        double c1x = rng.NextDouble() * 100 - 50;
                        double c1y = rng.NextDouble() * 100 - 50;
                        double c2x = rng.NextDouble() * 100 - 50;
                        double c2y = rng.NextDouble() * 100 - 50;
                        double ex = rng.NextDouble() * 100 - 50;
                        double ey = rng.NextDouble() * 100 - 50;
                        builder.CubicTo(new Point(c1x, c1y), new Point(c2x, c2y), new Point(ex, ey));
                        x = ex;
                        y = ey;
                    }
                    break;
            }
        }

        builder.Close();
        return builder.Build();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool SegmentCountWithin(int ours, int oracle, double minRatio, double maxRatio)
    {
        if (oracle == 0) return ours == 0;
        double ratio = (double)ours / oracle;
        return ratio >= minRatio && ratio <= maxRatio;
    }

    private static List<Point> FlattenBezPath(BezPath path, double tolerance)
    {
        const int maxPoints = 8192;
        var result = new List<Point>(maxPoints);
        Point current = new Point(0, 0);

        foreach (PathSegment seg in path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    result.Add(seg.End);
                    current = seg.End;
                    break;
                case PathVerb.LineTo:
                    result.Add(seg.End);
                    current = seg.End;
                    break;
                case PathVerb.QuadTo:
                    {
                        var quad = new QuadBez(seg.Start, seg.Control0, seg.End);
                        result.Add(seg.Start);
                        var oracle = KurboOracle.QuadFlatten(quad, tolerance);
                        for (int i = 1; i < oracle.Count; i++)
                            result.Add(oracle[i]);
                        current = seg.End;
                    }
                    break;
                case PathVerb.CubicTo:
                    {
                        var cubic = new CubicBez(seg.Start, seg.Control0, seg.Control1, seg.End);
                        result.Add(seg.Start);
                        var oracle = KurboOracle.CubicFlatten(cubic, tolerance);
                        for (int i = 1; i < oracle.Count; i++)
                            result.Add(oracle[i]);
                        current = seg.End;
                    }
                    break;
                case PathVerb.Close:
                    current = new Point(0, 0);
                    break;
            }
        }

        return result;
    }
}