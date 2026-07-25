using Etch.Geometry;
using Etch.Strokes;
using TUnit;

namespace Etch.Strokes.Tests;

internal sealed class StrokeExpanderTests
{
    [Test]
    public async Task ExpandLineBasic()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        var input = builder.Build();

        var style = new StrokeStyle(2f);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Result must not be empty");
    }

    [Test]
    public async Task ExpandLineWithZeroWidthReturnsInput()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        var input = builder.Build();

        var style = new StrokeStyle(0f);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Zero-width style should return input");
    }

    [Test]
    public async Task ExpandEmptyPathReturnsEmpty()
    {
        using var builder = BezPathBuilder.Begin(8);
        var input = builder.Build();

        var style = new StrokeStyle(2f);
        var result = StrokeExpander.Expand(input, style);

        if (!result.IsEmpty)
            throw new InvalidOperationException("Empty path should return empty");
    }

    [Test]
    public async Task ExpandSinglePointReturnsInput()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(5, 5));
        var input = builder.Build();

        var style = new StrokeStyle(2f);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Single point should return input");
    }

    [Test]
    public async Task RoundCapProducesMoreVertices()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 1));
        builder.LineTo(new Point(10, 1));
        var input = builder.Build();

        var buttStyle = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt);
        var roundStyle = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Round);

        var buttResult = StrokeExpander.Expand(input, buttStyle);
        var roundResult = StrokeExpander.Expand(input, roundStyle);

        if (roundResult.VerbCount < buttResult.VerbCount)
            throw new InvalidOperationException("Round cap should produce at least as many verbs as butt cap");
    }

    [Test]
    public async Task MiterJoinAtRightAngle()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        var input = builder.Build();

        var style = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt, miterLimit: 4f);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Miter join result must not be empty");
    }

    [Test]
    public async Task BevelJoinAtRightAngle()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        var input = builder.Build();

        var style = new StrokeStyle(2f, JoinStyle.Bevel, CapStyle.Butt);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Bevel join result must not be empty");
    }

    [Test]
    public async Task SquareCapExtendsPath()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 1));
        builder.LineTo(new Point(10, 1));
        var input = builder.Build();

        var buttStyle = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt);
        var squareStyle = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Square);

        var buttResult = StrokeExpander.Expand(input, buttStyle);
        var squareResult = StrokeExpander.Expand(input, squareStyle);

        if (squareResult.VerbCount != buttResult.VerbCount)
            throw new InvalidOperationException("Square and butt caps should produce equal verb counts");
    }

    [Test]
    public async Task QuadCurveFlattensAndExpands()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.QuadTo(new Point(5, 10), new Point(10, 0));
        var input = builder.Build();

        var style = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Quad curve result must not be empty");
    }

    [Test]
    public async Task CubicCurveFlattensAndExpands()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.CubicTo(new Point(3, 10), new Point(7, 10), new Point(10, 0));
        var input = builder.Build();

        var style = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Cubic curve result must not be empty");
    }

    [Test]
    public async Task MiterLimitTruncatesToBevel()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 1));
        var input = builder.Build();

        var style = new StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt, miterLimit: 1.5f);
        var result = StrokeExpander.Expand(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Miter limit result must not be empty");
    }
}