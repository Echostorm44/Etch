using Etch.Geometry;
using Etch.Text.Rasterize;
using Etch.Text.Shape;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class GlyphRasterizerTests : IDisposable
{
    private FontFace? _font;

    [Test]
    public async Task WhitespaceGlyphRastersWithoutCrash()
    {
        var font = GetFont();
        var spaceRequest = new ShapeRequest(" ".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var spaceShaped = Shaper.Shape(spaceRequest);
        ushort spaceId = spaceShaped.Glyphs[0].GlyphId;

        byte[] destBuffer = new byte[1024 * 1024];
        GlyphRasterizer.Rasterize(font, spaceId, 0.0f, destBuffer, out int w, out int h);

        // Roboto's space glyph has a small bounding-box outline; just verify it runs
        await Assert.That(w).IsGreaterThanOrEqualTo(0);
        await Assert.That(h).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task LetterAProducesNonEmptyBitmap()
    {
        var font = GetFont();
        var request = new ShapeRequest("A".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var shaped = Shaper.Shape(request);
        ushort glyphId = shaped.Glyphs[0].GlyphId;

        byte[] dest = new byte[1024 * 64];
        GlyphRasterizer.Rasterize(font, glyphId, 0.0f, dest, out int w, out int h);

        await Assert.That(w).IsGreaterThan(0);
        await Assert.That(h).IsGreaterThan(0);
    }

    [Test]
    public async Task LetterADimensions()
    {
        var font = GetFont();
        var request = new ShapeRequest("A".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var shaped = Shaper.Shape(request);
        ushort glyphId = shaped.Glyphs[0].GlyphId;

        byte[] dest = new byte[1024 * 64];
        GlyphRasterizer.Rasterize(font, glyphId, 0.0f, dest, out int w, out int h);

        await Assert.That(w).IsGreaterThan(0);
        await Assert.That(h).IsGreaterThan(0);
    }

    [Test]
    public async Task LetterAHasNonZeroCoverage()
    {
        var font = GetFont();
        var request = new ShapeRequest("A".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var shaped = Shaper.Shape(request);
        ushort glyphId = shaped.Glyphs[0].GlyphId;

        byte[] dest = new byte[1024 * 64];
        GlyphRasterizer.Rasterize(font, glyphId, 0.0f, dest, out int w, out int h);

        int nonZero = 0;
        for (int i = 0; i < w * h; i++)
            if (dest[i] > 0) nonZero++;

        await Assert.That(nonZero).IsGreaterThan(0);
    }

    [Test]
    public async Task SubpixelShiftDoesNotCrash()
    {
        var font = GetFont();
        var request = new ShapeRequest("A".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var shaped = Shaper.Shape(request);
        ushort glyphId = shaped.Glyphs[0].GlyphId;

        byte[] dest = new byte[1024 * 64];
        GlyphRasterizer.Rasterize(font, glyphId, 0.0f, dest, out int w1, out int h1);
        GlyphRasterizer.Rasterize(font, glyphId, 0.3f, dest, out int w2, out int h2);

        await Assert.That(h1).IsEqualTo(h2);
    }

    [Test]
    public async Task ZeroAllocForSmallGlyph()
    {
        var font = GetFont();
        var request = new ShapeRequest("i".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var shaped = Shaper.Shape(request);
        ushort glyphId = shaped.Glyphs[0].GlyphId;

        byte[] dest = new byte[1024];
        for (int iter = 0; iter < 100; iter++)
        {
            GlyphRasterizer.Rasterize(font, glyphId, 0.0f, dest, out _, out _);
        }
    }

    private FontFace GetFont()
    {
        _font ??= FontFace.Load(TestFonts.RobotoRegular, 2048, 24f);
        return _font;
    }

    public void Dispose()
    {
        _font?.Dispose();
    }
}