using Etch.Text.Atlas;
using Etch.Text.Rasterize;
using Etch.Text.Shape;
using TUnit;

namespace Etch.Text.Tests;

/// <summary>
/// Outlined glyphs (GlyphRasterizer with an outline width): the glyph grown outward by the width,
/// through FreeType's stroker, or by dilating the glyph's own coverage for a face without FreeType.
/// </summary>
internal sealed class GlyphOutlineTests : IDisposable
{
    private const float PixelSize = 48f;
    private const float OutlineWidth = 3f;

    private FontFace? freeTypeFont;
    private FontFace? ownRasterizerFont;

    [Test]
    public async Task FreeTypeOutline_GrowsTheGlyphByTheWidthOnEverySide()
    {
        var font = FreeTypeFont();
        await Assert.That(font.FreeTypeFace).IsNotEqualTo(0);
        var glyph = Rasterized(font, 'O', 0f);
        var outline = Rasterized(font, 'O', OutlineWidth);

        // FreeType rounds the stroked outline to whole pixels: allow one pixel either way.
        await Assert.That(outline.Width - glyph.Width).IsBetween(2 * 3 - 1, 2 * 3 + 1);
        await Assert.That(outline.Height - glyph.Height).IsBetween(2 * 3 - 1, 2 * 3 + 1);
        await Assert.That(glyph.MinX - outline.MinX).IsBetween(2, 4);
        await Assert.That(glyph.MinY - outline.MinY).IsBetween(2, 4);
    }

    [Test]
    public async Task FreeTypeOutline_CoversTheGlyphAndKeepsTheCounterOpen()
    {
        var font = FreeTypeFont();
        var glyph = Rasterized(font, 'O', 0f);
        var outline = Rasterized(font, 'O', OutlineWidth);

        await Assert.That(UncoveredGlyphPixels(glyph, outline)).IsEqualTo(0);
        // The centre of the O stays empty: the outside border shrinks the counter, it does not fill it.
        await Assert.That(outline.CoverageAt(outline.MinX + outline.Width / 2, outline.MinY + outline.Height / 2)).IsEqualTo((byte)0);
        // And the ring around the glyph is solid: the pixel just left of the glyph's left edge, mid-height.
        await Assert.That(outline.CoverageAt(glyph.MinX - 1, glyph.MinY + glyph.Height / 2)).IsGreaterThan((byte)200);
    }

    [Test]
    public async Task FreeTypeOutline_MeasureMatchesRasterize()
    {
        var font = FreeTypeFont();
        ushort glyphId = GlyphId(font, 'g');
        GlyphRasterizer.Measure(font, glyphId, out int w, out int h, 0.5f, OutlineWidth);
        var outline = Rasterized(font, 'g', OutlineWidth, 0.5f);

        await Assert.That(outline.Width).IsEqualTo(w);
        await Assert.That(outline.Height).IsEqualTo(h);
    }

    [Test]
    public async Task OwnRasterizerOutline_GrowsTheGlyphByTheWidth()
    {
        var font = OwnRasterizerFont();
        await Assert.That(font.FreeTypeFace).IsEqualTo(0);
        var glyph = Rasterized(font, 'O', 0f);
        var outline = Rasterized(font, 'O', OutlineWidth);
        GlyphRasterizer.Measure(font, GlyphId(font, 'O'), out int measuredW, out int measuredH, 0f, OutlineWidth);

        await Assert.That(outline.Width).IsEqualTo(glyph.Width + 6);
        await Assert.That(outline.Height).IsEqualTo(glyph.Height + 6);
        await Assert.That(measuredW).IsEqualTo(outline.Width);
        await Assert.That(measuredH).IsEqualTo(outline.Height);
        await Assert.That(UncoveredGlyphPixels(glyph, outline)).IsEqualTo(0);
        await Assert.That(outline.CoverageAt(outline.MinX + outline.Width / 2, outline.MinY + outline.Height / 2)).IsEqualTo((byte)0);
    }

    [Test]
    public async Task GrowCoverage_DilatesAPixelIntoAnAntialiasedDisc()
    {
        byte[] source = [255];
        byte[] dest = new byte[5 * 5];
        GlyphRasterizer.GrowCoverage(source, 1, 1, 2f, dest, out int w, out int h);

        await Assert.That(w).IsEqualTo(5);
        await Assert.That(h).IsEqualTo(5);
        await Assert.That(dest[2 * 5 + 2]).IsEqualTo((byte)255); // centre
        // 2 px left: the source pixel's edge (0.5) grown by 2 reaches 2.5, this pixel's far edge.
        await Assert.That(dest[2 * 5 + 0]).IsEqualTo((byte)255);
        // (-2, -1), 2.24 px away: mostly covered (3 - 2.24 = 0.76).
        await Assert.That(dest[1 * 5 + 0]).IsBetween((byte)185, (byte)205);
        // The corner, 2.83 px away: the rounded corner only clips it (3 - 2.83 = 0.17).
        await Assert.That(dest[0]).IsBetween((byte)35, (byte)55);
    }

    [Test]
    public async Task OutlineWidth_KeysItsOwnAtlasEntry()
    {
        var plain = GlyphCacheKey.FromSizeAndSubpixel(PixelSize, 1, 50, 0);
        var outlined = GlyphCacheKey.FromSizeAndSubpixel(PixelSize, 1, 50, 0, 2f);
        var wider = GlyphCacheKey.FromSizeAndSubpixel(PixelSize, 1, 50, 0, 2.5f);

        await Assert.That(plain.Equals(outlined)).IsFalse();
        await Assert.That(outlined.Equals(wider)).IsFalse();
        await Assert.That(outlined.OutlineWidth).IsEqualTo(2f);
        await Assert.That(GlyphCacheKey.SizeOf).IsEqualTo(System.Runtime.InteropServices.Marshal.SizeOf<GlyphCacheKey>());
    }

    [Test]
    public async Task QuantizeOutlineWidth_NeverTurnsAnOutlineIntoTheGlyph()
    {
        await Assert.That(GlyphCacheKey.QuantizeOutlineWidth(0f)).IsEqualTo((byte)0);
        await Assert.That(GlyphCacheKey.QuantizeOutlineWidth(-1f)).IsEqualTo((byte)0);
        await Assert.That(GlyphCacheKey.QuantizeOutlineWidth(float.NaN)).IsEqualTo((byte)0);
        await Assert.That(GlyphCacheKey.QuantizeOutlineWidth(0.01f)).IsEqualTo((byte)1);
        await Assert.That(GlyphCacheKey.QuantizeOutlineWidth(1.5f)).IsEqualTo((byte)6);
        await Assert.That(GlyphCacheKey.QuantizeOutlineWidth(1000f)).IsEqualTo(byte.MaxValue);
    }

    /// <summary>Glyph pixels at full coverage that the outline does not fully cover.</summary>
    private static int UncoveredGlyphPixels(RasterizedGlyph glyph, RasterizedGlyph outline)
    {
        int uncovered = 0;
        for (int y = glyph.MinY; y < glyph.MinY + glyph.Height; y++)
        {
            for (int x = glyph.MinX; x < glyph.MinX + glyph.Width; x++)
            {
                if (glyph.CoverageAt(x, y) == 255 && outline.CoverageAt(x, y) < 255)
                {
                    uncovered++;
                }
            }
        }
        return uncovered;
    }

    private static ushort GlyphId(FontFace font, char character)
    {
        if (!font.TryGetGlyph(character, out uint glyphId))
        {
            throw new InvalidOperationException($"Roboto has no glyph for '{character}'");
        }
        return (ushort)glyphId;
    }

    private static RasterizedGlyph Rasterized(FontFace font, char character, float outlineWidth, float subpixelX = 0f)
    {
        byte[] coverage = new byte[256 * 256];
        GlyphRasterizer.Rasterize(font, GlyphId(font, character), subpixelX, coverage, out int w, out int h, out int minX, out int minY, outlineWidth);
        return new RasterizedGlyph(coverage, w, h, minX, minY);
    }

    private FontFace FreeTypeFont() => freeTypeFont ??= FontFace.Load(TestFonts.RobotoRegular, 2048, PixelSize * 72f / 96f);

    private FontFace OwnRasterizerFont() => ownRasterizerFont ??= FontFace.Load(TestFonts.RobotoRegular.AsSpan(), 2048, PixelSize * 72f / 96f);

    public void Dispose()
    {
        freeTypeFont?.Dispose();
        ownRasterizerFont?.Dispose();
    }

    /// <summary>A rasterized bitmap (bottom row first) placed in glyph pixel space (y up).</summary>
    private readonly record struct RasterizedGlyph(byte[] Coverage, int Width, int Height, int MinX, int MinY)
    {
        public byte CoverageAt(int x, int y)
        {
            int column = x - MinX;
            int row = y - MinY;
            if ((uint)column >= (uint)Width || (uint)row >= (uint)Height)
            {
                return 0;
            }
            return Coverage[row * Width + column];
        }
    }
}
