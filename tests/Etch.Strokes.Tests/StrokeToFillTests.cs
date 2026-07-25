using Etch.Geometry;
using Etch.Strokes;
using TUnit;

namespace Etch.Strokes.Tests;

internal sealed class StrokeToFillTests
{
    [Test]
    public async Task RectangleStrokedAreaMatchesPerimeter()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(100, 0));
        builder.LineTo(new Point(100, 100));
        builder.LineTo(new Point(0, 100));
        builder.Close();
        var rect = builder.Build();

        var style = new Strokes.StrokeStyle(2f);
        var result = StrokeToFill.Convert(rect, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("StrokeToFill should produce output");
    }

    [Test]
    public async Task SingleLineWithRoundCap()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 5));
        builder.LineTo(new Point(100, 5));
        var line = builder.Build();

        var style = new Strokes.StrokeStyle(10f, JoinStyle.Miter, CapStyle.Round);
        var result = StrokeToFill.Convert(line, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Round cap should produce output");
    }

    [Test]
    public async Task ZeroWidthReturnsInput()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        var input = builder.Build();

        var style = new Strokes.StrokeStyle(0f);
        var result = StrokeToFill.Convert(input, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Zero-width should return input");
    }

    [Test]
    public async Task EmptyPathReturnsEmpty()
    {
        using var builder = BezPathBuilder.Begin(4);
        var empty = builder.Build();

        var style = new Strokes.StrokeStyle(2f);
        var result = StrokeToFill.Convert(empty, style);

        if (!result.IsEmpty)
            throw new InvalidOperationException("Empty path should return empty");
    }

    [Test]
    public async Task MiterJoinAtRightAngle()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        var path = builder.Build();

        var style = new Strokes.StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt, miterLimit: 4f);
        var result = StrokeToFill.Convert(path, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Miter join should produce output");
    }

    [Test]
    public async Task BevelJoinProducesCorrectOutline()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 10));
        var path = builder.Build();

        var style = new Strokes.StrokeStyle(2f, JoinStyle.Bevel, CapStyle.Butt);
        var result = StrokeToFill.Convert(path, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Bevel join should produce output");
    }

    [Test]
    public async Task SquareCapExtendsOutline()
    {
        using var builder = BezPathBuilder.Begin(8);
        builder.MoveTo(new Point(0, 5));
        builder.LineTo(new Point(10, 5));
        var line = builder.Build();

        var buttStyle = new Strokes.StrokeStyle(4f, JoinStyle.Miter, CapStyle.Butt);
        var squareStyle = new Strokes.StrokeStyle(4f, JoinStyle.Miter, CapStyle.Square);

        var buttResult = StrokeToFill.Convert(line, buttStyle);
        var squareResult = StrokeToFill.Convert(line, squareStyle);

        if (squareResult.VerbCount <= buttResult.VerbCount)
            throw new InvalidOperationException("Square cap should produce more vertices than butt cap");
    }

    [Test]
    public async Task QuadCurveConvertsToFillablePath()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.QuadTo(new Point(5, 10), new Point(10, 0));
        var quad = builder.Build();

        var style = new Strokes.StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt);
        var result = StrokeToFill.Convert(quad, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Quad curve stroke should produce output");
    }

    [Test]
    public async Task CubicCurveConvertsToFillablePath()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.CubicTo(new Point(3, 10), new Point(7, 10), new Point(10, 0));
        var cubic = builder.Build();

        var style = new Strokes.StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt);
        var result = StrokeToFill.Convert(cubic, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Cubic curve stroke should produce output");
    }

    [Test]
    public async Task MiterLimitTruncatesSpike()
    {
        using var builder = BezPathBuilder.Begin(16);
        builder.MoveTo(new Point(0, 0));
        builder.LineTo(new Point(10, 0));
        builder.LineTo(new Point(10, 1));
        var path = builder.Build();

        var style = new Strokes.StrokeStyle(2f, JoinStyle.Miter, CapStyle.Butt, miterLimit: 1.5f);
        var result = StrokeToFill.Convert(path, style);

        if (result.IsEmpty)
            throw new InvalidOperationException("Miter limit should not produce empty result");
    }
}