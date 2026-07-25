using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class RectTests
{
    [Test]
    public async Task FromLTRB()
    {
        var r = Rect.FromLTRB(1, 2, 3, 4);
        if (r.MinX != 1 || r.MinY != 2 || r.MaxX != 3 || r.MaxY != 4)
            throw new InvalidOperationException("FromLTRB values mismatch");
    }

    [Test]
    public async Task FromMinSize()
    {
        var r = Rect.FromMinSize(1, 2, 10, 20);
        if (r.MinX != 1 || r.MinY != 2 || r.MaxX != 11 || r.MaxY != 22)
            throw new InvalidOperationException("FromMinSize mismatch");
    }

    [Test]
    public async Task FromPointsNormalizes()
    {
        var a = new Point(5, 10);
        var b = new Point(1, 2);
        var r = Rect.FromPoints(a, b);
        if (r.MinX != 1 || r.MinY != 2 || r.MaxX != 5 || r.MaxY != 10)
            throw new InvalidOperationException("FromPoints must normalise ordering");
    }

    [Test]
    public async Task WidthAndHeight()
    {
        var r = Rect.FromLTRB(1, 2, 4, 6);
        if (Math.Abs(r.Width - 3) > 1e-10 || Math.Abs(r.Height - 4) > 1e-10)
            throw new InvalidOperationException($"Width={r.Width} or Height={r.Height} incorrect");
    }

    [Test]
    public async Task Area()
    {
        var r = Rect.FromLTRB(0, 0, 4, 3);
        if (Math.Abs(r.Area - 12) > 1e-10)
            throw new InvalidOperationException($"Area={r.Area}, expected 12");
    }

    [Test]
    public async Task IsEmptyFalseWhenZeroWidth()
    {
        var r = Rect.FromLTRB(0, 0, 0, 5);
        if (r.IsEmpty)
            throw new InvalidOperationException("Zero-width rect must not be empty");
    }

    [Test]
    public async Task IsEmptyFalseWhenZeroHeight()
    {
        var r = Rect.FromLTRB(0, 0, 5, 0);
        if (r.IsEmpty)
            throw new InvalidOperationException("Zero-height rect must not be empty");
    }

    [Test]
    public async Task IsEmptyFalseWhenPositiveArea()
    {
        var r = Rect.FromLTRB(0, 0, 5, 5);
        if (r.IsEmpty)
            throw new InvalidOperationException("Positive-area rect must not be empty");
    }

    [Test]
    public async Task Center()
    {
        var r = Rect.FromLTRB(0, 0, 4, 6);
        var c = r.Center;
        if (Math.Abs(c.X - 2) > 1e-10 || Math.Abs(c.Y - 3) > 1e-10)
            throw new InvalidOperationException($"Center=({c.X}, {c.Y}), expected (2,3)");
    }

    [Test]
    public async Task ContainsPointInside()
    {
        var r = Rect.FromLTRB(0, 0, 10, 10);
        if (!r.Contains(new Point(5, 5)))
            throw new InvalidOperationException("Center must be contained");
    }

    [Test]
    public async Task ContainsPointOnEdge()
    {
        var r = Rect.FromLTRB(0, 0, 10, 10);
        if (!r.Contains(new Point(0, 5)) || !r.Contains(new Point(10, 5)))
            throw new InvalidOperationException("Edge points must be contained");
    }

    [Test]
    public async Task ContainsPointOutside()
    {
        var r = Rect.FromLTRB(0, 0, 10, 10);
        if (r.Contains(new Point(15, 5)))
            throw new InvalidOperationException("Outside point must not be contained");
    }

    [Test]
    public async Task ContainsRectReflexive()
    {
        var r = Rect.FromLTRB(1, 2, 3, 4);
        if (!r.Contains(r))
            throw new InvalidOperationException("a.Contains(a) must be true (reflexive)");
    }

    [Test]
    public async Task ContainsRectImpliesIntersects()
    {
        var a = Rect.FromLTRB(0, 0, 10, 10);
        var b = Rect.FromLTRB(2, 2, 8, 8);
        if (!a.Contains(b) || !a.Intersects(b))
            throw new InvalidOperationException("Contains implies Intersects");
    }

    [Test]
    public async Task IntersectsReturnsTrue()
    {
        var a = Rect.FromLTRB(0, 0, 5, 5);
        var b = Rect.FromLTRB(3, 3, 8, 8);
        if (!a.Intersects(b))
            throw new InvalidOperationException("Overlapping rects must intersect");
    }

    [Test]
    public async Task IntersectsReturnsFalseOnDisjoint()
    {
        var a = Rect.FromLTRB(0, 0, 5, 5);
        var b = Rect.FromLTRB(10, 10, 15, 15);
        if (a.Intersects(b))
            throw new InvalidOperationException("Disjoint rects must not intersect");
    }

    [Test]
    public async Task IntersectDisjointReturnsEmpty()
    {
        var a = Rect.FromLTRB(0, 0, 5, 5);
        var b = Rect.FromLTRB(10, 10, 15, 15);
        var result = a.Intersect(b);
        if (!result.IsEmpty)
            throw new InvalidOperationException("Disjoint intersect must be empty");
    }

    [Test]
    public async Task IntersectDisjointNotInverted()
    {
        var a = Rect.FromLTRB(0, 0, 5, 5);
        var b = Rect.FromLTRB(10, 10, 15, 15);
        var result = a.Intersect(b);
        if (result.MinX > result.MaxX || result.MinY > result.MaxY)
            throw new InvalidOperationException("Disjoint intersect must not produce inverted coords");
    }

    [Test]
    public async Task IntersectOverlappingReturnsOverlap()
    {
        var a = Rect.FromLTRB(0, 0, 5, 5);
        var b = Rect.FromLTRB(3, 3, 8, 8);
        var result = a.Intersect(b);
        if (Math.Abs(result.MinX - 3) > 1e-10 || Math.Abs(result.MaxX - 5) > 1e-10
            || Math.Abs(result.MinY - 3) > 1e-10 || Math.Abs(result.MaxY - 5) > 1e-10)
            throw new InvalidOperationException("Overlap rect mismatch");
    }

    [Test]
    public async Task UnionIsCommutative()
    {
        var a = Rect.FromLTRB(0, 0, 5, 5);
        var b = Rect.FromLTRB(3, 3, 8, 8);
        var ab = a.Union(b);
        var ba = b.Union(a);
        if (!ab.Equals(ba))
            throw new InvalidOperationException("Union must be commutative");
    }

    [Test]
    public async Task UnionIsAssociative()
    {
        var a = Rect.FromLTRB(0, 0, 2, 2);
        var b = Rect.FromLTRB(1, 1, 3, 3);
        var c = Rect.FromLTRB(2, 2, 4, 4);
        var ab_c = a.Union(b).Union(c);
        var a_bc = a.Union(b.Union(c));
        if (!ab_c.Equals(a_bc))
            throw new InvalidOperationException("Union must be associative");
    }

    [Test]
    public async Task Inflate()
    {
        var r = Rect.FromLTRB(2, 3, 6, 7);
        var inflated = r.Inflate(1);
        if (Math.Abs(inflated.MinX - 1) > 1e-10 || Math.Abs(inflated.MaxX - 7) > 1e-10)
            throw new InvalidOperationException($"Inflate mismatch: {inflated}");
    }

    [Test]
    public async Task Translated()
    {
        var r = Rect.FromLTRB(1, 2, 4, 5);
        var t = r.Translated(new Vec2(10, 20));
        if (Math.Abs(t.MinX - 11) > 1e-10 || Math.Abs(t.MinY - 22) > 1e-10)
            throw new InvalidOperationException($"Translated mismatch: {t}");
    }

    [Test]
    public async Task InvertedRectPanics()
    {
        bool threw = false;
        try
        {
            _ = new Rect(5, 4, 3, 2);
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.InvertedRect)
        {
            threw = true;
        }
        if (!threw)
            throw new InvalidOperationException("new Rect(5,4,3,2) must panic InvertedRect");
    }
}
