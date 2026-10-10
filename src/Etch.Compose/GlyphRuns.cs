using System.Buffers;
using Etch.Text.Atlas;
using Etch.Text.Outline;
using Etch.Text.Rasterize;

namespace Etch.Compose;

/// <summary>
/// Pixel-grid placement of monochrome glyph quads. The horizontal subpixel offset is baked into the
/// rasterized bitmap (one of four quarter-pixel buckets), so the quad sits at the integer pen
/// origin; placing it at the fractional pen position too would apply the shift twice. The baseline
/// snaps to the nearest pixel row (horizontal text has no vertical subpixel bitmaps). Colour glyphs
/// are rasterized without a subpixel offset, so their quad sits at the pen origin rounded to the
/// nearest pixel: every quad maps 1:1 onto its atlas texels and both composers read them exactly
/// (no resampling blur, and no dependence on a GPU's filtering precision).
/// </summary>
public static class GlyphPlacement
{
    /// <summary>Horizontal subpixel bucket [0, 3] for a pen X: the quarter-pixel offset the bitmap is rasterized at.</summary>
    public static byte SubpixelBucket(float penX)
    {
        float frac = penX - MathF.Floor(penX);
        return (byte)Math.Min(3, (int)(frac * 4f));
    }

    /// <summary>Left edge of a mono glyph quad: integer pen origin plus the bitmap's left bearing.</summary>
    public static float QuadOriginX(float penX, int bitmapLeftBearing) => MathF.Floor(penX) + bitmapLeftBearing;

    /// <summary>Left edge of a colour glyph quad: the pen origin rounded to the nearest pixel plus the bitmap's left bearing.</summary>
    public static float ColorQuadOriginX(float penX, int bitmapLeftBearing) => MathF.Round(penX, MidpointRounding.AwayFromZero) + bitmapLeftBearing;

    /// <summary>Top edge of a mono glyph quad: the baseline snapped to the nearest row, minus the bitmap's ascent.</summary>
    public static float QuadOriginY(float penY, int bitmapTopBearing, int bitmapHeight)
        => MathF.Round(penY, MidpointRounding.AwayFromZero) - (bitmapTopBearing + bitmapHeight);
}

/// <summary>
/// A glyph too large for the glyph atlas (taller than a shelf): the builder draws its outline
/// instead (its colour layers, for a colour glyph). Pen position in device pixels.
/// </summary>
public readonly record struct OversizedGlyph(ushort GlyphId, float PenX, float PenY, bool IsColor);

/// <summary>
/// Collects a run's <see cref="OversizedGlyph"/>s, and remembers which colour glyphs are too large
/// for the atlas so they are not rasterized again to find out.
/// </summary>
public sealed class OversizedGlyphs
{
    private const int MaxRemembered = 1024;
    private readonly HashSet<GlyphCacheKey> known = new();

    /// <summary>The current run's glyphs too large for the atlas.</summary>
    public List<OversizedGlyph> Glyphs { get; } = new();

    internal HashSet<GlyphCacheKey> Known => known;

    internal void Remember(GlyphCacheKey key)
    {
        if (known.Count >= MaxRemembered)
        {
            known.Clear();
        }
        known.Add(key);
    }
}

/// <summary>Where one glyph of a run was placed (for draw-provenance tooling).</summary>
public readonly record struct PlacedGlyph(
    float X, float Y, float Width, float Height,
    ushort GlyphId, float RasterSize, int AtlasU, int AtlasV,
    uint ClipIndex, bool IsColor, object? Source);

/// <summary>
/// Turns glyph runs into atlas quads: rasterizes glyphs missing from the atlases (FreeType coverage
/// for mono glyphs, COLR layers for colour glyphs), places them on the pixel grid and appends the
/// instances to a <see cref="DrawList"/>.
/// </summary>
public static class GlyphRunBuilder
{
    /// <summary>
    /// Appends the instances of <paramref name="run"/>, shifted by (<paramref name="offsetX"/>,
    /// <paramref name="offsetY"/>) and faded by <paramref name="opacity"/>, to
    /// <paramref name="list"/>'s glyph lists. Glyphs whose quad misses the clip's hard rect are skipped.
    /// Returns the number of glyphs skipped that way. Glyphs too large for the atlas are reported to
    /// <paramref name="oversized"/> (when given) for the caller to draw as outlines.
    /// </summary>
    public static int Append(DrawList list, in GlyphRunData run, float offsetX, float offsetY, float opacity, uint clipIndex,
        GlyphAtlas monoAtlas, GlyphAtlas colorAtlas, List<PlacedGlyph>? placed, OversizedGlyphs? oversized = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(monoAtlas);
        ArgumentNullException.ThrowIfNull(colorAtlas);
        var face = run.Face;
        var clip = list.Clips[(int)clipIndex];
        if (clip.MaxX <= clip.MinX || clip.MaxY <= clip.MinY)
        {
            return run.GlyphIds.Length;
        }

        var color = run.Color;
        float alpha = color.A * opacity;
        float fgLuminance = Luminance(color);
        // Spaces draw nothing and are skipped — but only a real space glyph: in a face without one,
        // spaceGid would be 0, the .notdef glyph, and missing characters' tofu boxes would vanish.
        bool hasSpace = face.TryGetGlyph(0x0020, out uint spaceGid);

        int culled = 0;
        for (int i = 0; i < run.GlyphIds.Length; i++)
        {
            ushort glyphId = run.GlyphIds[i];
            if (IsSpace(hasSpace, spaceGid, glyphId))
            {
                continue;
            }

            float gx = run.Positions[i * 2] + offsetX;
            float gy = run.Positions[i * 2 + 1] + offsetY;
            byte subpixel = GlyphPlacement.SubpixelBucket(gx);

            if (GlyphOutlineBuilder.HasColorLayers(face, glyphId))
            {
                // Colour glyphs have no subpixel variants (bucket 0): one bitmap per size.
                var colorKey = GlyphCacheKey.FromSizeAndSubpixel(run.RasterSize, run.FaceId, glyphId, 0);
                if (!colorAtlas.TryLookup(colorKey, out var colorRegion, out _))
                {
                    if (oversized is not null && oversized.Known.Contains(colorKey))
                    {
                        oversized.Glyphs.Add(new OversizedGlyph(glyphId, gx, gy, true));
                        continue;
                    }
                    bool tooLarge = false;
                    byte[] rented = ArrayPool<byte>.Shared.Rent(256 * 256 * 4);
                    try
                    {
                        // False with a size: the bitmap is larger than the buffer.
                        bool rasterized = GlyphRasterizer.RasterizeColorGlyph(face, glyphId, rented.AsSpan(), out int cw, out int ch, out int cminX, out int cminY);
                        if (cw > 0 && ch > 0 && (!rasterized || !colorAtlas.CanEverHold(cw, ch)))
                        {
                            tooLarge = true;
                        }
                        else if (rasterized && cw > 0 && ch > 0)
                        {
                            colorAtlas.TryInsert(colorKey, rented.AsSpan(0, cw * ch * 4), cw, ch, out colorRegion, out _, (short)cminX, (short)cminY);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                    if (tooLarge && oversized is not null)
                    {
                        oversized.Remember(colorKey);
                        oversized.Glyphs.Add(new OversizedGlyph(glyphId, gx, gy, true));
                        continue;
                    }
                }

                if (colorRegion.W > 0 && colorRegion.H > 0)
                {
                    float cpx = GlyphPlacement.ColorQuadOriginX(gx, colorRegion.OffsetX);
                    float cpy = GlyphPlacement.QuadOriginY(gy, colorRegion.OffsetY, colorRegion.H);
                    if (cpx + colorRegion.W <= clip.MinX || cpx >= clip.MaxX || cpy + colorRegion.H <= clip.MinY || cpy >= clip.MaxY)
                    {
                        culled++;
                        continue;
                    }

                    list.ColorGlyphs.Add(new GlyphInstance
                    {
                        PosX = cpx,
                        PosY = cpy,
                        SizeX = colorRegion.W,
                        SizeY = colorRegion.H,
                        AtlasU0 = colorRegion.U,
                        AtlasV0 = colorRegion.V + colorRegion.H,
                        AtlasU1 = colorRegion.U + colorRegion.W,
                        AtlasV1 = colorRegion.V,
                        R = 1f,
                        G = 1f,
                        B = 1f,
                        A = alpha,
                        ForegroundLuminance = 1f,
                        ClipIndex = clipIndex,
                    });
                    placed?.Add(new PlacedGlyph(cpx, cpy, colorRegion.W, colorRegion.H, glyphId, run.RasterSize,
                        colorRegion.U, colorRegion.V, clipIndex, true, run.Source));
                    continue;
                }
                // Colour rasterization failed — fall through to monochrome.
            }

            var key = GlyphCacheKey.FromSizeAndSubpixel(run.RasterSize, run.FaceId, glyphId, subpixel);
            if (!monoAtlas.TryLookup(key, out var region, out _))
            {
                GlyphRasterizer.Measure(face, glyphId, out int gw, out int gh, subpixel / 4f);
                if (oversized is not null && !monoAtlas.CanEverHold(gw + 1, gh))
                {
                    oversized.Glyphs.Add(new OversizedGlyph(glyphId, gx, gy, false));
                    continue;
                }
                if (gw > 0 && gh > 0)
                {
                    // The rasterizer widens the bitmap by one column when the subpixel shift > 0.
                    int bufSize = (gw + 1) * gh;
                    byte[] rented = ArrayPool<byte>.Shared.Rent(bufSize);
                    try
                    {
                        GlyphRasterizer.Rasterize(face, glyphId, subpixel / 4f, rented.AsSpan(0, bufSize), out int rw, out int rh, out int minX, out int minY);
                        if (rw > 0 && rh > 0)
                        {
                            monoAtlas.TryInsert(key, rented.AsSpan(0, rw * rh), rw, rh, out region, out _, (short)minX, (short)minY);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }
            }

            if (region.W == 0 || region.H == 0)
            {
                continue;
            }

            float px = GlyphPlacement.QuadOriginX(gx, region.OffsetX);
            float py = GlyphPlacement.QuadOriginY(gy, region.OffsetY, region.H);
            if (px + region.W <= clip.MinX || px >= clip.MaxX || py + region.H <= clip.MinY || py >= clip.MaxY)
            {
                culled++;
                continue;
            }

            list.Glyphs.Add(new GlyphInstance
            {
                PosX = px,
                PosY = py,
                SizeX = region.W,
                SizeY = region.H,
                AtlasU0 = region.U,
                AtlasV0 = region.V + region.H,
                AtlasU1 = region.U + region.W,
                AtlasV1 = region.V,
                R = color.R,
                G = color.G,
                B = color.B,
                A = alpha,
                ForegroundLuminance = fgLuminance,
                ClipIndex = clipIndex,
            });
            placed?.Add(new PlacedGlyph(px, py, region.W, region.H, glyphId, run.RasterSize,
                region.U, region.V, clipIndex, false, run.Source));
        }
        return culled;
    }

    /// <summary>
    /// Whether <paramref name="glyphId"/> is the face's space glyph (drawn as nothing). A face with no
    /// space maps U+0020 to glyph 0, .notdef — the tofu box missing characters must still show.
    /// </summary>
    internal static bool IsSpace(bool faceHasSpace, uint spaceGlyph, ushort glyphId)
        => faceHasSpace && spaceGlyph != 0 && glyphId == spaceGlyph;

    /// <summary>Rec. 709 luminance of the colour's sRGB-encoded channels — the space the text-weight curve compares in.</summary>
    public static float Luminance(ComposeColor color)
        => 0.2126f * Srgb.Encode(color.R) + 0.7152f * Srgb.Encode(color.G) + 0.0722f * Srgb.Encode(color.B);
}

/// <summary>Scalar sRGB transfer functions (IEC 61966-2-1).</summary>
public static class Srgb
{
    /// <summary>Linear → sRGB-encoded, both in [0, 1].</summary>
    public static float Encode(float linear)
    {
        float c = Math.Clamp(linear, 0f, 1f);
        return c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
    }

    /// <summary>sRGB-encoded → linear, both in [0, 1].</summary>
    public static float Decode(float encoded)
    {
        float c = Math.Clamp(encoded, 0f, 1f);
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
