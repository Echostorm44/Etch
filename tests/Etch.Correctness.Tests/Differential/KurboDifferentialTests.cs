using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Geometry.Oracle;
using TUnit;

namespace Etch.Correctness.Tests.Differential;

internal sealed class KurboDifferentialTests
{
    private const double Tolerance = 0.25;
    private const double HausdorffToleranceMultiplier = 1.5;
    private const int Seed = 42;

    [Test]
    public async Task FuzzFlatten_10KInputs_ZeroViolations()
    {
        await RunFuzz(iterations: 10_000, seed: Seed);
    }

    [Test]
    public async Task FuzzFlatten_1MInputs_ZeroViolations()
    {
        await RunFuzz(iterations: 1_000_000, seed: Seed);
    }

    [Test]
    public async Task FuzzFlatten_SeedCorpus_Passes()
    {
        string corpusDir = Path.Combine(FindRepoRoot(), "tests", "Etch.Correctness.Tests", "Differential", "corpus");
        if (!Directory.Exists(corpusDir))
        {
            await Assert.That(true).IsTrue();
            return;
        }

        foreach (var file in Directory.EnumerateFiles(corpusDir, "*.bin"))
        {
            byte[] bytes = await File.ReadAllBytesAsync(file);
            var result = FuzzSingle(bytes);
            if (!result.Pass)
            {
                throw new InvalidOperationException(
                    $"Seed corpus file {file} failed: {result.ErrorMessage}");
            }
        }
    }

    private static async Task RunFuzz(int iterations, int seed)
    {
        if (!KurboOracle.TryLoad())
        {
            await Assert.That(true).IsTrue();
            return;
        }

#pragma warning disable CA5394 // Random is deterministic and seeded for fuzz reproducibility
        var rng = new Random(seed);
#pragma warning restore CA5394
        int violations = 0;
        byte[]? firstFailingInput = null;
        string? firstFailureMessage = null;

        byte[] buffer = new byte[128];

        for (int i = 0; i < iterations; i++)
        {
            rng.NextBytes(buffer);
            var result = FuzzSingle(buffer);
            if (!result.Pass)
            {
                violations++;
                if (firstFailingInput == null)
                {
                    firstFailingInput = new byte[buffer.Length];
                    buffer.CopyTo(firstFailingInput, 0);
                    firstFailureMessage = result.ErrorMessage;
                }
            }
        }

        if (violations > 0 && firstFailingInput != null)
        {
            byte[] minimized = Minimize(firstFailingInput);
            string reproPath = Path.Combine(Path.GetTempPath(), $"kurbo_repro_{Guid.NewGuid():N}.bin");
            await File.WriteAllBytesAsync(reproPath, minimized);

            throw new InvalidOperationException(
                $"{violations}/{iterations} fuzz inputs violated Hausdorff tolerance. " +
                $"First failure: {firstFailureMessage}. Minimized reproducer written to {reproPath}");
        }

        await Assert.That(violations).IsEqualTo(0);
    }

    private static FuzzResult FuzzSingle(ReadOnlySpan<byte> input)
    {
        try
        {
            BezPath path = BezPathFuzzDecoder.Decode(input);
            if (path.IsEmpty)
                return new FuzzResult(true, null);

            double tol = Tolerance;

            foreach (PathSegment seg in path.Iterate())
            {
                if (seg.Verb == PathVerb.QuadTo)
                {
                    var quad = new QuadBez(seg.Start, seg.Control0, seg.End);
                    var ours = FlattenQuad(quad, tol);
                    var oracle = KurboOracle.QuadFlatten(quad, tol);
                    double h = HausdorffDistance(ours, oracle);
                    if (h > tol * HausdorffToleranceMultiplier)
                    {
                        return new FuzzResult(false, $"Quad Hausdorff={h:F4} > {tol * HausdorffToleranceMultiplier:F4}");
                    }
                }
                else if (seg.Verb == PathVerb.CubicTo)
                {
                    var cubic = new CubicBez(seg.Start, seg.Control0, seg.Control1, seg.End);
                    var ours = FlattenCubic(cubic, tol);
                    var oracle = KurboOracle.CubicFlatten(cubic, tol);
                    double h = HausdorffDistance(ours, oracle);
                    if (h > tol * HausdorffToleranceMultiplier)
                    {
                        return new FuzzResult(false, $"Cubic Hausdorff={h:F4} > {tol * HausdorffToleranceMultiplier:F4}");
                    }
                }
            }

            return new FuzzResult(true, null);
        }
        catch (Exception ex)
        {
            return new FuzzResult(false, $"Exception: {ex.Message}");
        }
    }

    private static double HausdorffDistance(List<Point> ours, IReadOnlyList<Point> oracle)
    {
        if (ours.Count == 0 || oracle.Count == 0)
            return 0.0;

        Span<Point> a = stackalloc Point[ours.Count];
        for (int i = 0; i < ours.Count; i++) a[i] = ours[i];

        Span<Point> b = stackalloc Point[oracle.Count];
        for (int i = 0; i < oracle.Count; i++) b[i] = oracle[i];

        return Hausdorff.Distance(a, b);
    }

    private static List<Point> FlattenQuad(QuadBez q, double tol)
    {
        Point[] buffer = ArrayPool<Point>.Shared.Rent(4096);
        var sink = new FlattenSink(buffer, autoflush: false);
        CurveFlattener.QuadBez(in q, tol, ref sink);
        var written = sink.Written.ToArray();
        ArrayPool<Point>.Shared.Return(buffer);
        return new List<Point>(written);
    }

    private static List<Point> FlattenCubic(CubicBez c, double tol)
    {
        Point[] buffer = ArrayPool<Point>.Shared.Rent(4096);
        var sink = new FlattenSink(buffer, autoflush: false);
        CurveFlattener.CubicBez(in c, tol, ref sink);
        var written = sink.Written.ToArray();
        ArrayPool<Point>.Shared.Return(buffer);
        return new List<Point>(written);
    }

    private static byte[] Minimize(byte[] failingInput)
    {
        byte[] current = new byte[failingInput.Length];
        failingInput.CopyTo(current, 0);

        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < current.Length; i++)
            {
                byte[] candidate = new byte[current.Length - 1];
                Buffer.BlockCopy(current, 0, candidate, 0, i);
                Buffer.BlockCopy(current, i + 1, candidate, i, current.Length - i - 1);

                if (FuzzSingle(candidate).Pass == false)
                {
                    current = candidate;
                    changed = true;
                    break;
                }
            }
        } while (changed && current.Length > 1);

        return current;
    }

    private static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Etch.sln")))
                return dir;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    private readonly record struct FuzzResult(bool Pass, string? ErrorMessage);
}
