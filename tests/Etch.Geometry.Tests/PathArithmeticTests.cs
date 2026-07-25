using System;
using Etch.Geometry;
using TUnit;

namespace Etch.Geometry.Tests;

internal sealed class PathArithmeticTests
{
    [Test]
    public void ApproximateLength_StraightLine_ReturnsExactLength()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        var path = builder.Build();

        double length = PathArithmetic.ApproximateLength(path);

        if (Math.Abs(length - 10.0) > 1e-6)
            throw new InvalidOperationException($"Expected 10.0, got {length}");
    }

    [Test]
    public void ApproximateLength_CubicStraightLine_ReturnsExactLength()
    {
        var c = new CubicBez(new Point(0, 0), new Point(3.333, 0), new Point(6.666, 0), new Point(10, 0));

        double length = PathArithmetic.ApproximateLength(c);

        if (Math.Abs(length - 10.0) > 1e-6)
            throw new InvalidOperationException($"Expected 10.0, got {length}");
    }

    [Test]
    public void ApproximateLength_QuadStraightLine_ReturnsExactLength()
    {
        var q = new QuadBez(new Point(0, 0), new Point(5, 0), new Point(10, 0));

        double length = PathArithmetic.ApproximateLength(q);

        if (Math.Abs(length - 10.0) > 1e-6)
            throw new InvalidOperationException($"Expected 10.0, got {length}");
    }

    [Test]
    public void ApproximateLength_UnitCircle_Approximate()
    {
        double fourPi = 2.0 * Math.PI;
        var path = UnitCirclePath();
        double length = PathArithmetic.ApproximateLength(path, 0.1);
        double error = Math.Abs(length - fourPi) / fourPi;
        if (error > 0.05)
            throw new InvalidOperationException($"Circle length error: {error * 100:F1}%");
    }

    [Test]
    public void SampleAtLength_StartPoint_ReturnsPathStart()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        var path = builder.Build();

        var sample = PathArithmetic.SampleAtLength(path, 0);

        if (sample.X != 0 || sample.Y != 0)
            throw new InvalidOperationException($"Expected (0,0), got ({sample.X}, {sample.Y})");
    }

    [Test]
    public void SampleAtLength_EndPoint_ReturnsPathEnd()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        var path = builder.Build();
        double length = PathArithmetic.ApproximateLength(path);

        var sample = PathArithmetic.SampleAtLength(path, length);

        if (sample.X != 10 || sample.Y != 10)
            throw new InvalidOperationException($"Expected (10,10), got ({sample.X}, {sample.Y})");
    }

    [Test]
    public void SampleAtLength_HalfLength_ReturnsMidpoint()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        var path = builder.Build();
        double length = PathArithmetic.ApproximateLength(path);

        var sample = PathArithmetic.SampleAtLength(path, length / 2);

        if (Math.Abs(sample.X - 5) > 1e-6 || Math.Abs(sample.Y - 0) > 1e-6)
            throw new InvalidOperationException($"Expected (5,0), got ({sample.X}, {sample.Y})");
    }

    [Test]
    public void SampleAtLengthsSorted_Monotonic()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        var path = builder.Build();
        double length = PathArithmetic.ApproximateLength(path);

        Span<double> lengths = stackalloc double[] { length * 0.25, length * 0.5, length * 0.75 };
        Span<Point> output = stackalloc Point[3];
        PathArithmetic.SampleAtLengthsSorted(path, lengths, output);

        double d1 = output[0].DistanceTo(output[1]);
        double d2 = output[1].DistanceTo(output[2]);

        if (d1 > d2 * 1.01)
            throw new InvalidOperationException($"Expected second segment to be at least as long as first, got d1={d1}, d2={d2}");
    }

    [Test]
    public void SampleAtLengthsSorted_UnsortedLengths_Throws()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        var path = builder.Build();

        Span<double> lengths = stackalloc double[] { 5, 3, 7 };
        Span<Point> output = stackalloc Point[3];

        bool threw = false;
        try
        {
            PathArithmetic.SampleAtLengthsSorted(path, lengths, output);
        }
        catch (EtchException ex) when (ex.Code == PanicCodes.UnsortedLengths)
        {
            threw = true;
        }

        if (!threw)
            throw new InvalidOperationException("Expected UnsortedLengths panic");
    }

    [Test]
    public void SampleAtLengthsSorted_EmptyInput_HandlesGracefully()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        var path = builder.Build();

        Span<double> lengths = stackalloc double[] { 0, 0, 0 };
        Span<Point> output = stackalloc Point[3];

        PathArithmetic.SampleAtLengthsSorted(path, lengths, output);
    }

    [Test]
    public void ApproximateLength_ZeroPointPath_ReturnsZero()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        var path = builder.Build();

        double length = PathArithmetic.ApproximateLength(path);

        if (length != 0)
            throw new InvalidOperationException($"Expected 0, got {length}");
    }

    [Test]
    public void SampleAtLength_BeyondEnd_ReturnsEndPoint()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        var path = builder.Build();
        double length = PathArithmetic.ApproximateLength(path);

        var sample = PathArithmetic.SampleAtLength(path, length * 2);

        if (sample.X != 10 || sample.Y != 0)
            throw new InvalidOperationException($"Expected (10,0), got ({sample.X}, {sample.Y})");
    }

    [Test]
    public void SampleAtLength_NegativeLength_ReturnsStartPoint()
    {
        var builder = BezPathBuilder.Begin(64);
        builder.MoveTo(new Point(5, 5));
        builder.LineTo(new Point(10, 0));
        var path = builder.Build();

        var sample = PathArithmetic.SampleAtLength(path, -5);

        if (sample.X != 5 || sample.Y != 5)
            throw new InvalidOperationException($"Expected (5,5), got ({sample.X}, {sample.Y})");
    }

    private static BezPath UnitCirclePath()
    {
        double k = 0.5522847498307936;
        var builder = BezPathBuilder.Begin(32);
        builder.MoveTo(new Point(1, 0));
        builder.CubicTo(
            new Point(1, k),
            new Point(k, 1),
            new Point(0, 1));
        builder.CubicTo(
            new Point(-k, 1),
            new Point(-1, k),
            new Point(-1, 0));
        builder.CubicTo(
            new Point(-1, -k),
            new Point(-k, -1),
            new Point(0, -1));
        builder.CubicTo(
            new Point(k, -1),
            new Point(1, -k),
            new Point(1, 0));
        builder.Close();
        return builder.Build();
    }
}
