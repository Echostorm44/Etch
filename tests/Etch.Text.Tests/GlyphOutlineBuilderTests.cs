using Etch.Geometry;
using Etch.Text.Shape;
using Etch.Text.Outline;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class GlyphOutlineBuilderTests : IDisposable
{
    private FontFace? _robotoFont;

    [Test]
    public async Task NullFaceThrowsArgumentNull()
    {
        EtchException caught = Capture(static () => GlyphOutlineBuilder.Build(null!, 0, BezPathBuilder.Begin(64)));

        await Assert.That(caught.Code).IsEqualTo(PanicCodes.ArgumentNull);
        await Assert.That(caught.Message).Contains("face");
    }

    [Test]
    public async Task NotdefGlyphReturnsPath()
    {
        var font = GetRobotoFont();
        BezPath path;
        using (var builder = BezPathBuilder.Begin(64))
        {
            path = GlyphOutlineBuilder.Build(font, 0, builder) ?? throw new InvalidOperationException("Expected non-null path");
        }
        await Assert.That(path.VerbCount).IsGreaterThan(0);
        await Assert.That(path.IsEmpty).IsFalse();
    }

    [Test]
    public async Task LetterHProducesNonEmptyPath()
    {
        var font = GetRobotoFont();
        var request = new ShapeRequest("H".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var result = Shaper.Shape(request);

        await Assert.That(result.GlyphCount).IsEqualTo(1);

        BezPath path;
        using (var builder = BezPathBuilder.Begin(256))
        {
            path = GlyphOutlineBuilder.Build(font, result.Glyphs[0].GlyphId, builder) ?? throw new InvalidOperationException("Expected non-null path");
        }
        await Assert.That(path.VerbCount).IsGreaterThan(0);
    }

    [Test]
    public async Task LetterAProducesPathWithExpectedVerbs()
    {
        var font = GetRobotoFont();
        var request = new ShapeRequest("A".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var result = Shaper.Shape(request);

        await Assert.That(result.GlyphCount).IsEqualTo(1);

        BezPath path;
        using (var builder = BezPathBuilder.Begin(256))
        {
            path = GlyphOutlineBuilder.Build(font, result.Glyphs[0].GlyphId, builder) ?? throw new InvalidOperationException("Expected non-null path");
        }

        var verbCount = path.VerbCount;

        var hasMoveTo = false;
        var hasLineTo = false;
        var hasClose = false;
        foreach (var segment in path.Iterate())
        {
            switch (segment.Verb)
            {
                case PathVerb.MoveTo:
                    hasMoveTo = true;
                    break;
                case PathVerb.LineTo:
                    hasLineTo = true;
                    break;
                case PathVerb.Close:
                    hasClose = true;
                    break;
            }
        }

        await Assert.That(verbCount).IsGreaterThan(0);
        await Assert.That(hasMoveTo).IsTrue();
        await Assert.That(hasLineTo).IsTrue();
        await Assert.That(hasClose).IsTrue();
    }

    [Test]
    public async Task LetterCProducesCurvedPath()
    {
        var font = GetRobotoFont();
        var request = new ShapeRequest("C".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var result = Shaper.Shape(request);

        await Assert.That(result.GlyphCount).IsEqualTo(1);

        BezPath path;
        using (var builder = BezPathBuilder.Begin(256))
        {
            path = GlyphOutlineBuilder.Build(font, result.Glyphs[0].GlyphId, builder) ?? throw new InvalidOperationException("Expected non-null path");
        }

        var verbCount = path.VerbCount;

        var hasQuadOrCubic = false;
        foreach (var segment in path.Iterate())
        {
            if (segment.Verb == PathVerb.QuadTo || segment.Verb == PathVerb.CubicTo)
            {
                hasQuadOrCubic = true;
                break;
            }
        }

        await Assert.That(verbCount).IsGreaterThan(0);
        await Assert.That(hasQuadOrCubic).IsTrue();
    }

    private FontFace GetRobotoFont()
    {
        _robotoFont ??= FontFace.Load(TestFonts.RobotoRegular, 2048, 12f);
        return _robotoFont;
    }

    public void Dispose()
    {
        _robotoFont?.Dispose();
    }

    private static EtchException Capture(Action act)
    {
        try
        {
            act();
        }
        catch (EtchException ex)
        {
            return ex;
        }
        throw new InvalidOperationException("GlyphOutlineBuilder.Build did not throw.");
    }
}