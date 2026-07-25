using Etch.Geometry;
using Etch.Strokes;
using TUnit;

namespace Etch.Strokes.Tests;

internal sealed class JoinEmitterTests
{
    [Test]
    public async Task RoundJoinAtRightAngleArcDeviation()
    {
        float halfWidth = 5f;
        Vec2 endTangent = new Vec2(1, 0);
        Vec2 startTangent = new Vec2(0, 1);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Round, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        if (outer.VerbCount <= 2 || inner.VerbCount <= 2)
            throw new InvalidOperationException("Round join should produce curve segments");
    }

    [Test]
    public async Task BevelJoinProducesSingleLineSegment()
    {
        float halfWidth = 5f;
        Vec2 endTangent = new Vec2(1, 0);
        Vec2 startTangent = new Vec2(0, 1);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        int outerBefore = outer.VerbCount;
        int innerBefore = inner.VerbCount;

        JoinEmitter.Emit(JoinStyle.Bevel, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        if (outer.VerbCount != outerBefore + 1)
            throw new InvalidOperationException("Bevel should add exactly one line to outer");
        if (inner.VerbCount != innerBefore + 1)
            throw new InvalidOperationException("Bevel should add exactly one line to inner");
    }

    [Test]
    public async Task MiterJoinAtAcuteAngle()
    {
        float halfWidth = 5f;
        double angle30Deg = 30.0 * Math.PI / 180.0;
        Vec2 endTangent = new Vec2(Math.Cos(angle30Deg), Math.Sin(angle30Deg));
        Vec2 startTangent = new Vec2(Math.Cos(-angle30Deg), Math.Sin(-angle30Deg));

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        if (outer.VerbCount < 2)
            throw new InvalidOperationException("Miter should produce a join segment");
    }

    [Test]
    public async Task DegenerateCollinearTangentsNoOp()
    {
        float halfWidth = 5f;
        Vec2 collinear = new Vec2(1, 0);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        int outerBefore = outer.VerbCount;
        int innerBefore = inner.VerbCount;

        JoinEmitter.Emit(JoinStyle.Miter, collinear, collinear, halfWidth, 4f, ref outer, ref inner);

        if (outer.VerbCount != outerBefore)
            throw new InvalidOperationException("Collinear tangents should produce no join segment");
    }

    [Test]
    public async Task MiterLimitTruncatesToBevel()
    {
        float halfWidth = 5f;
        Vec2 endTangent = new Vec2(1, 0);
        Vec2 startTangent = new Vec2(0.1f, 0.995f);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        int outerBefore = outer.VerbCount;
        int innerBefore = inner.VerbCount;

        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 1.5f, ref outer, ref inner);

        if (outer.VerbCount == outerBefore)
            throw new InvalidOperationException("Miter exceeding limit should truncate to bevel (add segments)");
    }

    [Test]
    public async Task RoundJoinHasCubicSegments()
    {
        float halfWidth = 5f;
        Vec2 endTangent = new Vec2(1, 0);
        Vec2 startTangent = new Vec2(0, 1);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Round, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        var path = outer.Build();
        int cubicCount = 0;
        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.CubicTo) cubicCount++;
        }

        if (cubicCount < 1)
            throw new InvalidOperationException("Round join should contain cubic segments");
    }

    [Test]
    public async Task AllThreeJoinStylesProduceDifferentOutput()
    {
        float halfWidth = 5f;
        Vec2 endTangent = new Vec2(1, 0);
        Vec2 startTangent = new Vec2(0, 1);

        var miterOuter = BezPathBuilder.Begin(32);
        var miterInner = BezPathBuilder.Begin(32);
        miterOuter.MoveTo(new Point(0, 0));
        miterInner.MoveTo(new Point(0, 0));
        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 4f, ref miterOuter, ref miterInner);
        var miterPath = miterOuter.Build();

        var bevelOuter = BezPathBuilder.Begin(32);
        var bevelInner = BezPathBuilder.Begin(32);
        bevelOuter.MoveTo(new Point(0, 0));
        bevelInner.MoveTo(new Point(0, 0));
        JoinEmitter.Emit(JoinStyle.Bevel, endTangent, startTangent, halfWidth, 4f, ref bevelOuter, ref bevelInner);
        var bevelPath = bevelOuter.Build();

        var roundOuter = BezPathBuilder.Begin(32);
        var roundInner = BezPathBuilder.Begin(32);
        roundOuter.MoveTo(new Point(0, 0));
        roundInner.MoveTo(new Point(0, 0));
        JoinEmitter.Emit(JoinStyle.Round, endTangent, startTangent, halfWidth, 4f, ref roundOuter, ref roundInner);
        var roundPath = roundOuter.Build();

        if (miterPath.VerbCount == bevelPath.VerbCount && bevelPath.VerbCount == roundPath.VerbCount)
            throw new InvalidOperationException("Different join styles should produce different verb counts");
    }
}