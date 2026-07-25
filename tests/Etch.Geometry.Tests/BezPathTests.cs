using System;
using System.Collections.Generic;
using Etch.Geometry;
using Etch.Primitives;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class BezPathTests
{
    [Test]
    public async Task BuildSimplePath()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        builder.Close();
        var path = builder.Build();

        if (path.IsEmpty)
            throw new InvalidOperationException("Path must not be empty");
        if (path.VerbCount != 4)
            throw new InvalidOperationException($"VerbCount must be 4, got {path.VerbCount}");
    }

    [Test]
    public async Task IterateAllVerbs()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.QuadTo(new Point(15, 5), new Point(10, 10));
        builder.CubicTo(new Point(5, 15), new Point(0, 10), new Point(0, 0));
        builder.Close();
        var path = builder.Build();

        List<PathVerb> verbs = new();
        foreach (var seg in path.Iterate())
        {
            verbs.Add(seg.Verb);
        }

        if (verbs.Count != 5)
            throw new InvalidOperationException($"Expected 5 verbs, got {verbs.Count}");
        if (verbs[0] != PathVerb.MoveTo || verbs[1] != PathVerb.LineTo
            || verbs[2] != PathVerb.QuadTo || verbs[3] != PathVerb.CubicTo || verbs[4] != PathVerb.Close)
            throw new InvalidOperationException($"Verb sequence mismatch: {string.Join(", ", verbs)}");
    }

    [Test]
    public async Task PathVerbWithoutMoveToPanics()
    {
        bool threw = false;
        try
        {
            using var builder = BezPathBuilder.Begin(8);
            builder.LineTo(new Point(10, 0));
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.PathVerbWithoutMoveTo)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("LineTo before MoveTo must panic PathVerbWithoutMoveTo");
    }

    [Test]
    public async Task BuildTwicePanics()
    {
        bool threw = false;
        try
        {
            using var builder = BezPathBuilder.Begin(8);
            builder.MoveTo(new Point(0, 0));
            builder.Build();
            builder.Build();
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.BuilderConsumed)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("Build() twice must panic BuilderConsumed");
    }

    [Test]
    public async Task TransformedByIdentity()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(1, 2));
        builder.LineTo(new Point(10, 0));
        builder.QuadTo(new Point(15, 5), new Point(10, 10));
        var path = builder.Build();

        var transformed = path.TransformedBy(Affine.Identity);

        List<PathVerb> origVerbs = new();
        foreach (var seg in path.Iterate()) origVerbs.Add(seg.Verb);
        List<PathVerb> transVerbs = new();
        foreach (var seg in transformed.Iterate()) transVerbs.Add(seg.Verb);

        if (origVerbs.Count != transVerbs.Count)
            throw new InvalidOperationException("Identity transform must preserve verb count");
    }

    [Test]
    public async Task TransformedByTranslate()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.QuadTo(new Point(15, 5), new Point(10, 10));
        var path = builder.Build();

        var transformed = path.TransformedBy(Affine.Translate(5, -3));

        List<double> xCoords = new();
        foreach (var seg in transformed.Iterate())
        {
            if (seg.Verb == PathVerb.MoveTo || seg.Verb == PathVerb.LineTo)
                xCoords.Add(seg.End.X);
            else if (seg.Verb == PathVerb.QuadTo)
                xCoords.Add(seg.Control0.X);
        }

        foreach (double x in xCoords)
        {
            if (x < 5)
                throw new InvalidOperationException($"Translated path x-coords must be shifted by 5, got {x}");
        }
    }

    [Test]
    public async Task AabbContainsSampledPoints()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.QuadTo(new Point(10, 20), new Point(20, 0));
        builder.LineTo(new Point(20, 20));
        var path = builder.Build();

        Rect aabb = path.Aabb();

        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.QuadTo)
            {
                var quad = new QuadBez(seg.Start, seg.Control0, seg.End);
                for (int i = 0; i <= 1000; i++)
                {
                    double t = i / 1000.0;
                    var pt = quad.Eval(t);
                    if (!aabb.Contains(pt))
                        throw new InvalidOperationException($"AABB does not contain sampled quad point at t={t}: {pt}");
                }
            }
        }
    }

    [Test]
    public async Task CloseAfterMoveTo()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.Close();
        var path = builder.Build();

        if (path.VerbCount != 3)
            throw new InvalidOperationException($"MoveTo+LineTo+Close = 3 verbs, got {path.VerbCount}");
    }

    [Test]
    public async Task EnumerationZeroAlloc()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        builder.Close();
        var path = builder.Build();

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int iter = 0; iter < 100; iter++)
        {
            foreach (var seg in path.Iterate())
            {
                _ = seg.Verb;
            }
        }
        long after = GC.GetAllocatedBytesForCurrentThread();
        long delta = after - before;
        if (delta > 0)
            throw new InvalidOperationException($"Enumeration allocated {delta} bytes (expected 0)");
    }

    [Test]
    public async Task AabbOfEmptyPath()
    {
        using var builder = BezPathBuilder.Begin(8);
        var path = builder.Build();
        var aabb = path.Aabb();
        if (aabb.MinX != 0 || aabb.MaxX != 0 || aabb.MinY != 0 || aabb.MaxY != 0)
            throw new InvalidOperationException("Empty path AABB must be (0,0,0,0)");
    }
}
