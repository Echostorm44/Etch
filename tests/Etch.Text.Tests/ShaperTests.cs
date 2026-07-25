using Etch.Text.Shape;
using TUnit;

namespace Etch.Text.Tests;

internal sealed class ShaperTests : IDisposable
{
    private FontFace? _robotoFont;
    private FontFace? _arabicFont;

    [Test]
    public async Task EmptyTextReturnsEmptyRun()
    {
        var request = new ShapeRequest("".AsSpan(), null!, BiDiLevel.LeftToRight, "Latn");
        var result = Shaper.Shape(request);
        int count = result.GlyphCount;
        float advance = result.Advance;

        await Assert.That(count).IsEqualTo(0);
        await Assert.That(advance).IsEqualTo(0f);
    }

    [Test]
    public async Task BiDiLevelHasExpectedValues()
    {
        await Assert.That((int)BiDiLevel.Neutral).IsEqualTo(0);
        await Assert.That((int)BiDiLevel.LeftToRight).IsEqualTo(1);
        await Assert.That((int)BiDiLevel.RightToLeft).IsEqualTo(2);
    }

    [Test]
    public async Task ShapeRequestConstructsWithAllFields()
    {
        var request = new ShapeRequest("test".AsSpan(), null!, BiDiLevel.RightToLeft, "Arab");
        int length = request.Text.Length;
        BiDiLevel bidi = request.BiDiLevel;
        string tag = request.ScriptTag;

        await Assert.That(length).IsEqualTo(4);
        await Assert.That(bidi).IsEqualTo(BiDiLevel.RightToLeft);
        await Assert.That(tag).IsEqualTo("Arab");
    }

    [Test]
    public async Task HelloWithRobotoProducesFiveGlyphs()
    {
        var font = GetRobotoFont();
        var request = new ShapeRequest("Hello".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var result = Shaper.Shape(request);

        await Assert.That(result.GlyphCount).IsEqualTo(5);
        for (int i = 0; i < 5; i++)
        {
            await Assert.That(result.Glyphs[i].Cluster).IsEqualTo(i);
        }
    }

    [Test]
    public async Task ArabicLamAlefLigatureProducesCorrectGlyphCount()
    {
        var font = GetArabicFont();
        var request = new ShapeRequest("\u0644\u0627".AsSpan(), font, BiDiLevel.RightToLeft, "Arab");
        var result = Shaper.Shape(request);

        await Assert.That(result.GlyphCount).IsGreaterThan(0);
    }

    [Test]
    public async Task MissingGlyphFallsBackToNotdef()
    {
        var font = GetRobotoFont();
        var request = new ShapeRequest("\uFFFF".AsSpan(), font, BiDiLevel.LeftToRight, "Latn");
        var result = Shaper.Shape(request);

        await Assert.That(result.GlyphCount).IsEqualTo(1);
        await Assert.That((int)result.Glyphs[0].GlyphId).IsEqualTo(0);
    }

    private FontFace GetRobotoFont()
    {
        _robotoFont ??= FontFace.Load(TestFonts.RobotoRegular, 2048, 12f);
        return _robotoFont;
    }

    private FontFace GetArabicFont()
    {
        _arabicFont ??= FontFace.Load(TestFonts.AmiriArabic, 2048, 12f);
        return _arabicFont;
    }

    public void Dispose()
    {
        _robotoFont?.Dispose();
        _arabicFont?.Dispose();
    }
}