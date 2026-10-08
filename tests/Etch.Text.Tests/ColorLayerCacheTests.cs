using Etch.TestFonts;
using Etch.Text.Outline;
using Etch.Text.Shape;
using TUnit;

namespace Etch.Text.Tests;

/// <summary>
/// The CPAL palette cache must belong to the face it was read from. It was keyed by the native
/// HarfBuzz face pointer and never evicted, so a face created where a released one had lived
/// inherited that face's palette — a colour font then reported no colour layers (Cascade's
/// Emoji_IsColorGlyph_RasterizesChromaticRgba failed in CI depending on test order).
/// </summary>
internal sealed class ColorLayerCacheTests
{
    private const int GrinningFace = 0x1F600;

    [Test]
    public async Task ColorFace_ReportsColorLayers_AfterPlainFacesWereReleased()
    {
        byte[] plain = TestFontCache.RobotoRegular();
        byte[] emoji = TestFontCache.ColorEmoji();

        // Each round releases a face that cached "no palette", then creates a colour face — the
        // allocator readily hands the colour face the released face's native address.
        for (int round = 0; round < 64; round++)
        {
            using (var plainFace = FontFace.Load(plain, 2048, 12f))
            {
                await Assert.That(GlyphOutlineBuilder.HasColorLayers(plainFace, 1)).IsFalse();
            }

            using var colorFace = FontFace.Load(emoji, 2048, 12f);
            await Assert.That(colorFace.TryGetGlyph(GrinningFace, out uint gid)).IsTrue();
            await Assert.That(GlyphOutlineBuilder.HasColorLayers(colorFace, (ushort)gid)).IsTrue();
        }
    }

    [Test]
    public async Task PlainFace_ReportsNoColorLayers_AfterColorFacesWereReleased()
    {
        byte[] plain = TestFontCache.RobotoRegular();
        byte[] emoji = TestFontCache.ColorEmoji();

        for (int round = 0; round < 64; round++)
        {
            using (var colorFace = FontFace.Load(emoji, 2048, 12f))
            {
                await Assert.That(colorFace.TryGetGlyph(GrinningFace, out uint gid)).IsTrue();
                await Assert.That(GlyphOutlineBuilder.HasColorLayers(colorFace, (ushort)gid)).IsTrue();
            }

            using var plainFace = FontFace.Load(plain, 2048, 12f);
            await Assert.That(GlyphOutlineBuilder.HasColorLayers(plainFace, 1)).IsFalse();
        }
    }
}
