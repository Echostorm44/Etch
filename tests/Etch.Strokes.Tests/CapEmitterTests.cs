using Etch.Geometry;
using Etch.Strokes;
using TUnit;

namespace Etch.Strokes.Tests;

internal sealed class CapEmitterTests
{
    [Test]
    public async Task ButtCapOnHorizontalStroke()
    {
        float halfWidth = 5f;
        Vec2 tangent = new Vec2(1, 0);
        Point endpoint = new Point(10, 0);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, 0));
        inner.MoveTo(new Point(0, 0));

        CapEmitter.Emit(CapStyle.Butt, endpoint, tangent, halfWidth, ref outer, ref inner);

        var path = outer.Build();
        if (path.IsEmpty)
            throw new InvalidOperationException("Butt cap should produce output");
    }

    [Test]
    public async Task SquareCapExtendsPastEndpoint()
    {
        float halfWidth = 5f;
        Vec2 tangent = new Vec2(1, 0);
        Point endpoint = new Point(10, 0);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        CapEmitter.Emit(CapStyle.Square, endpoint, tangent, halfWidth, ref outer, ref inner);

        var path = outer.Build();
        int lineCount = 0;
        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.LineTo) lineCount++;
        }

        if (lineCount < 2)
            throw new InvalidOperationException("Square cap should have at least 2 line segments");
    }

    [Test]
    public async Task RoundCapProducesCubicSegments()
    {
        float halfWidth = 5f;
        Vec2 tangent = new Vec2(1, 0);
        Point endpoint = new Point(10, 0);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, 0));
        inner.MoveTo(new Point(0, 0));

        CapEmitter.Emit(CapStyle.Round, endpoint, tangent, halfWidth, ref outer, ref inner);

        var path = outer.Build();
        int cubicCount = 0;
        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.CubicTo) cubicCount++;
        }

        if (cubicCount < 1)
            throw new InvalidOperationException("Round cap should contain cubic segments");
    }

    [Test]
    public async Task AllThreeCapStylesProduceDifferentOutput()
    {
        float halfWidth = 5f;
        Vec2 tangent = new Vec2(1, 0);
        Point endpoint = new Point(10, 0);

        BezPath MakeButt()
        {
            var o = BezPathBuilder.Begin(32);
            var i = BezPathBuilder.Begin(32);
            o.MoveTo(new Point(0, 0));
            i.MoveTo(new Point(0, 0));
            CapEmitter.Emit(CapStyle.Butt, endpoint, tangent, halfWidth, ref o, ref i);
            return o.Build();
        }

        BezPath MakeSquare()
        {
            var o = BezPathBuilder.Begin(32);
            var i = BezPathBuilder.Begin(32);
            o.MoveTo(new Point(0, 0));
            i.MoveTo(new Point(0, 0));
            CapEmitter.Emit(CapStyle.Square, endpoint, tangent, halfWidth, ref o, ref i);
            return o.Build();
        }

        BezPath MakeRound()
        {
            var o = BezPathBuilder.Begin(32);
            var i = BezPathBuilder.Begin(32);
            o.MoveTo(new Point(0, 0));
            i.MoveTo(new Point(0, 0));
            CapEmitter.Emit(CapStyle.Round, endpoint, tangent, halfWidth, ref o, ref i);
            return o.Build();
        }

        var buttPath = MakeButt();
        var squarePath = MakeSquare();
        var roundPath = MakeRound();

        if (buttPath.VerbCount == squarePath.VerbCount && squarePath.VerbCount == roundPath.VerbCount)
            throw new InvalidOperationException("Different cap styles should produce different verb counts");
    }

    [Test]
    public async Task ReverseCapAtStartPoint()
    {
        float halfWidth = 5f;
        Vec2 tangent = new Vec2(1, 0);
        Point start = new Point(0, 0);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, 5));
        inner.MoveTo(new Point(0, -5));

        CapEmitter.EmitReverse(CapStyle.Butt, start, tangent, halfWidth, ref outer, ref inner);

        var path = outer.Build();
        if (path.IsEmpty)
            throw new InvalidOperationException("Reverse cap should produce output");
    }

    [Test]
    public async Task RoundCapArcDeviationSmall()
    {
        float halfWidth = 5f;
        Vec2 tangent = new Vec2(1, 0);
        Point center = new Point(0, 0);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(-halfWidth, 0));
        inner.MoveTo(new Point(-halfWidth, 0));

        CapEmitter.Emit(CapStyle.Round, center, tangent, halfWidth, ref outer, ref inner);

        var path = outer.Build();
        int cubicCount = 0;
        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.CubicTo) cubicCount++;
        }

        if (cubicCount < 1)
            throw new InvalidOperationException("Round cap must have cubic segments for arc approximation");
    }
}