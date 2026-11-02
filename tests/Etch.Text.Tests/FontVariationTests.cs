using Etch.Geometry;
using Etch.Text.Outline;
using Etch.Text.Rasterize;
using Etch.Text.Shape;
using TUnit;

namespace Etch.Text.Tests;

/// <summary>
/// Variable fonts (FontFace.Load with FontVariation settings): the weight reaches shaping
/// (HarfBuzz advances), rasterizing (FreeType design coordinates) and glyph outlines.
/// </summary>
internal sealed class FontVariationTests
{
    private const float PointSize = 36f; // 48 px

    [Test]
    public async Task Weight_ThickensTheRasterizedGlyph()
    {
        long thin = Coverage(InterAt(100), 'l');
        long regular = Coverage(InterAt(400), 'l');
        long black = Coverage(InterAt(900), 'l');

        await Assert.That(regular).IsGreaterThan(thin + thin / 2);
        await Assert.That(black).IsGreaterThan(regular + regular / 2);
    }

    [Test]
    public async Task Weight_WidensTheShapedAdvances()
    {
        float thin = Advance(InterAt(100), "Hamburgefonts");
        float black = Advance(InterAt(900), "Hamburgefonts");

        // Inter's black is markedly wider than its thin.
        await Assert.That(black).IsGreaterThan(thin * 1.1f);
    }

    [Test]
    public async Task Weight_OutsideTheAxis_IsClampedToItsEnd()
    {
        await Assert.That(Coverage(InterAt(5000), 'l')).IsEqualTo(Coverage(InterAt(900), 'l'));
        await Assert.That(Coverage(InterAt(float.NaN), 'l')).IsEqualTo(Coverage(InterAt(400), 'l'));
    }

    [Test]
    public async Task StaticFont_IgnoresVariations()
    {
        using var plain = FontFace.Load(TestFonts.RobotoRegular, 2048, PointSize);
        using var weighted = FontFace.Load(TestFonts.RobotoRegular, 2048, PointSize, [FontVariation.Weight(900)]);

        await Assert.That(weighted.Variations.Length).IsEqualTo(0);
        await Assert.That(Coverage(weighted, 'l')).IsEqualTo(Coverage(plain, 'l'));
    }

    [Test]
    public async Task VariableOutline_AtTheDefaultWeight_MatchesTheGlyfTable()
    {
        using var unvaried = FontFace.Load(TestFonts.InterVariable, 2048, PointSize);
        using var regular = InterAt(400);

        foreach (char character in "aegS&")
        {
            var glyf = OutlineBounds(unvaried, character);
            var freeType = OutlineBounds(regular, character);
            await Assert.That(Math.Abs(freeType.MinX - glyf.MinX)).IsLessThan(0.5).Because($"'{character}' left");
            await Assert.That(Math.Abs(freeType.MaxX - glyf.MaxX)).IsLessThan(0.5).Because($"'{character}' right");
            await Assert.That(Math.Abs(freeType.MinY - glyf.MinY)).IsLessThan(0.5).Because($"'{character}' bottom");
            await Assert.That(Math.Abs(freeType.MaxY - glyf.MaxY)).IsLessThan(0.5).Because($"'{character}' top");
        }
    }

    [Test]
    public async Task VariableOutline_FollowsTheWeight()
    {
        using var thin = InterAt(100);
        using var black = InterAt(900);

        var thinStem = OutlineBounds(thin, 'l');
        var blackStem = OutlineBounds(black, 'l');

        await Assert.That(blackStem.MaxX - blackStem.MinX).IsGreaterThan(2 * (thinStem.MaxX - thinStem.MinX));
    }

    private static FontFace InterAt(float weight)
        => FontFace.Load(TestFonts.InterVariable, 2048, PointSize, [FontVariation.Weight(weight)]);

    private static ushort GlyphId(FontFace font, char character)
    {
        if (!font.TryGetGlyph(character, out uint glyphId))
        {
            throw new InvalidOperationException($"No glyph for '{character}'");
        }
        return (ushort)glyphId;
    }

    private static long Coverage(FontFace font, char character)
    {
        byte[] bitmap = new byte[256 * 256];
        GlyphRasterizer.Rasterize(font, GlyphId(font, character), 0f, bitmap, out int w, out int h);
        long sum = 0;
        for (int i = 0; i < w * h; i++)
        {
            sum += bitmap[i];
        }
        return sum;
    }

    private static float Advance(FontFace font, string text)
        => Shaper.Shape(new ShapeRequest(text.AsSpan(), font, BiDiLevel.LeftToRight, "Latn")).Advance;

    private static Rect OutlineBounds(FontFace font, char character)
    {
        var path = GlyphOutlineBuilder.Build(font, GlyphId(font, character), BezPathBuilder.Begin(64))
            ?? throw new InvalidOperationException($"No outline for '{character}'");
        return path.Aabb();
    }
}
