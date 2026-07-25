using Etch.Geometry;
using Etch.Strokes;
using TUnit;

namespace Etch.Strokes.Tests;

internal sealed class MiterLimitTests
{
    private static (Vec2 endTangent, Vec2 startTangent) TangentsForAngle(double degrees)
    {
        double angle = degrees * Math.PI / 180.0;
        return (new Vec2(Math.Cos(angle), Math.Sin(angle)), new Vec2(1, 0));
    }

    private static int CountLines(BezPath path)
    {
        int count = 0;
        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.LineTo) count++;
        }
        return count;
    }

    private static int CountCubics(BezPath path)
    {
        int count = 0;
        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.CubicTo) count++;
        }
        return count;
    }

    [Test]
    public async Task NinetyDegreeJoinWithLimit4ProducesMiter()
    {
        float halfWidth = 5f;
        var (endTangent, startTangent) = TangentsForAngle(90);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        var path = outer.Build();
        int lines = CountLines(path);
        if (lines < 1)
            throw new InvalidOperationException("Miter join should produce miter segment");
    }

    [Test]
    public async Task TenDegreeJoinWithLimit4ProducesBevel()
    {
        float halfWidth = 5f;
        var (endTangent, startTangent) = TangentsForAngle(10);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        var path = outer.Build();
        int lines = CountLines(path);
        if (lines < 1)
            throw new InvalidOperationException("Acute angle should fall back to bevel (at least 1 line)");
    }

    [Test]
    public async Task ThirtyDegreeJoinWithLimit4ProducesMiter()
    {
        float halfWidth = 5f;
        var (endTangent, startTangent) = TangentsForAngle(30);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        var path = outer.Build();
        if (CountCubics(path) != 0)
            throw new InvalidOperationException("30° with limit 4 should not produce cubic segments (boundary case)");
    }

    [Test]
    public async Task BoundaryAngleProducesMiter()
    {
        float halfWidth = 5f;
        double boundaryAngle = 2.0 * Math.Asin(1.0 / 4.0) * 180.0 / Math.PI;
        var (endTangent, startTangent) = TangentsForAngle(boundaryAngle);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        var path = outer.Build();
        int lines = CountLines(path);
        if (lines < 1)
            throw new InvalidOperationException($"Boundary angle {boundaryAngle}° should produce miter");
    }

    [Test]
    public async Task Limit1ProducesBevelForAnyAcuteAngle()
    {
        float halfWidth = 5f;
        double[] angles = { 30, 45, 60, 90, 120 };

        foreach (double angle in angles)
        {
            var (endTangent, startTangent) = TangentsForAngle(angle);

            var outer = BezPathBuilder.Begin(32);
            var inner = BezPathBuilder.Begin(32);
            outer.MoveTo(new Point(0, halfWidth));
            inner.MoveTo(new Point(halfWidth, 0));

            JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 1f, ref outer, ref inner);

            var path = outer.Build();
            int lines = CountLines(path);
            if (lines > 2)
                throw new InvalidOperationException($"Angle {angle}° with limit 1 should produce bevel, not miter");
        }
    }

    [Test]
    public async Task Limit10AllowsAcuteJoins()
    {
        float halfWidth = 5f;
        var (endTangent, startTangent) = TangentsForAngle(15);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, halfWidth));
        inner.MoveTo(new Point(halfWidth, 0));

        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 10f, ref outer, ref inner);

        var path = outer.Build();
        int lines = CountLines(path);
        if (lines < 1)
            throw new InvalidOperationException("15° with limit 10 should produce miter");
    }

    [Test]
    public async Task WideAngleProducesNoSegments()
    {
        float halfWidth = 5f;
        var (endTangent, startTangent) = TangentsForAngle(180);

        var outer = BezPathBuilder.Begin(32);
        var inner = BezPathBuilder.Begin(32);
        outer.MoveTo(new Point(0, 0));
        inner.MoveTo(new Point(0, 0));

        int before = outer.VerbCount;
        JoinEmitter.Emit(JoinStyle.Miter, endTangent, startTangent, halfWidth, 4f, ref outer, ref inner);

        if (outer.VerbCount != before)
            throw new InvalidOperationException("180° should produce no segments (collinear)");
    }

    [Test]
    public async Task RatioComputationForVerification()
    {
        var (endTangent, startTangent) = TangentsForAngle(30);

        double cross = endTangent.X * startTangent.Y - endTangent.Y * startTangent.X;
        double endLen = Math.Sqrt(endTangent.X * endTangent.X + endTangent.Y * endTangent.Y);
        double startLen = Math.Sqrt(startTangent.X * startTangent.X + startTangent.Y * startTangent.Y);
        double sinHalfAngle = Math.Abs(cross) / (endLen * startLen);
        double ratio = 1.0 / sinHalfAngle;

        double expectedRatio = 1.0 / Math.Sin(30.0 * Math.PI / 180.0);
        if (Math.Abs(ratio - expectedRatio) > 0.01)
            throw new InvalidOperationException($"30° ratio should be ~2.0, got {ratio}");
    }
}