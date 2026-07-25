using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Etch.SkiaRef;
using Etch.Testing;
using Etch.Text.Rasterize;
using Etch.Text.Shape;
using SkiaSharp;
using TUnit;

namespace Etch.Text.Tests.TextParity;

/// <summary>
/// WP-3510: parity of the Etch text stack against SkiaSharp as a reference
/// renderer. Pixel-exact is the wrong bar — FreeType and Skia make different
/// (legitimate) hinting/antialiasing choices — so this asserts the two things
/// that must agree: glyph advance widths (metric-exact) and glyph shape
/// (coverage similarity above a tuned threshold).
///
/// The DPI confound the old tests flagged is resolved by matching ppem on both
/// sides. Etch drives FreeType at 96 DPI, so a FontFace at point size P
/// rasterizes at P×96/72 px-per-em, while its HarfBuzz advances are at P
/// px-per-em. SkiaSharp's SKFont.Size is px-per-em directly. So:
/// <list type="bullet">
///   <item>advances compare with SKFont.Size = P (measured identical to 0.008 px);</item>
///   <item>coverage compares with SKFont.Size = P×96/72 (same raster ppem).</item>
/// </list>
/// </summary>
public class TextSkiaParityTests
{
    private static readonly float[] SpecimenSizes = { 16f, 20f, 24f, 32f, 48f };
    private const string Corpus = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>FreeType-96-DPI raster ppem relative to the HarfBuzz/point ppem.</summary>
    private const float RasterDpiScale = 96f / 72f;

    /// <summary>
    /// Advance tolerance. Measured worst case is 0.008 px (HarfBuzz 26.6
    /// rounding); 0.05 px is comfortably "metric-exact" and imperceptible.
    /// </summary>
    private const float AdvanceTolerancePx = 0.05f;

    /// <summary>
    /// Binarized-coverage IoU floor. Worst observed across the full specimen
    /// set is 0.446 ('j' at 16 px — a thin descender where 1-px stem
    /// differences dominate a binary IoU); 0.40 clears it with margin while
    /// staying 2–4× above a wrong glyph (~0.1–0.2). Below 16 px, thin stems
    /// (1-vs-2 px) make IoU inherently noisy, so the specimen set starts at
    /// 16 px.
    /// </summary>
    private const float CoverageIoUThreshold = 0.40f;

    [Test]
    public async Task Advances_MatchSkia_AcrossSpecimenSet()
    {
        byte[] fontData = TestFonts.RobotoRegular;
        using var skData = SKData.CreateCopy(fontData);
        using var typeface = SKTypeface.FromData(skData);

        var failures = new List<string>();

        foreach (float size in SpecimenSizes)
        {
            using var face = FontFace.Load(fontData, 2048, size);
            using var skFont = new SKFont(typeface, size) { Hinting = SKFontHinting.None, Subpixel = true };

            foreach (char c in Corpus)
            {
                var shaped = Shaper.Shape(new ShapeRequest(c.ToString(), face, BiDiLevel.LeftToRight, "latn"));
                if (shaped.GlyphCount == 0)
                {
                    continue;
                }

                float etchAdvance = shaped.Glyphs[0].XAdvance;
                ushort skGlyph = skFont.GetGlyph(c);
                if (skGlyph == 0)
                {
                    continue;
                }
                float skiaAdvance = skFont.MeasureText(new[] { skGlyph }, out _);

                float diff = Math.Abs(etchAdvance - skiaAdvance);
                if (diff > AdvanceTolerancePx)
                {
                    failures.Add(FormattableString.Invariant(
                        $"'{c}' {size}px: Etch advance {etchAdvance:F4} vs Skia {skiaAdvance:F4} (diff {diff:F4} > {AdvanceTolerancePx})"));
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Advance parity failed for {failures.Count} specimen entries:\n" + string.Join("\n", failures));
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task Coverage_SimilarToSkia_AcrossSpecimenSet()
    {
        byte[] fontData = TestFonts.RobotoRegular;
        var failures = new List<string>();

        foreach (float size in SpecimenSizes)
        {
            using var face = FontFace.Load(fontData, 2048, size);
            face.Hinting = FontHinting.None;
            float coverageSize = size * RasterDpiScale;

            foreach (char c in Corpus)
            {
                var shaped = Shaper.Shape(new ShapeRequest(c.ToString(), face, BiDiLevel.LeftToRight, "latn"));
                if (shaped.GlyphCount == 0)
                {
                    continue;
                }
                ushort gid = shaped.Glyphs[0].GlyphId;

                byte[] etchBuf = ArrayPool<byte>.Shared.Rent(1 << 20);
                byte[] skiaBuf = ArrayPool<byte>.Shared.Rent(1 << 20);
                try
                {
                    GlyphRasterizer.Rasterize(face, gid, 0, etchBuf, out int ew, out int eh);
                    SkiaGlyphProbe.RenderCharacter(fontData, c.ToString(), coverageSize, 0, skiaBuf, out int kw, out int kh);

                    if (ew <= 0 || eh <= 0)
                    {
                        failures.Add(FormattableString.Invariant($"'{c}' {size}px: Etch produced empty output"));
                        continue;
                    }
                    if (kw <= 0 || kh <= 0)
                    {
                        // Skia could not render this glyph — nothing to compare against.
                        continue;
                    }

                    float iou = CoverageIoU(etchBuf, ew, eh, skiaBuf, kw, kh);
                    if (iou < CoverageIoUThreshold)
                    {
                        failures.Add(FormattableString.Invariant(
                            $"'{c}' {size}px: coverage IoU {iou:F3} < {CoverageIoUThreshold} (Etch {ew}x{eh}, Skia {kw}x{kh})"));
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(etchBuf);
                    ArrayPool<byte>.Shared.Return(skiaBuf);
                }
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                $"Coverage parity failed for {failures.Count} specimen entries:\n" + string.Join("\n", failures));
        }

        await Assert.That(failures).IsEmpty();
    }

    /// <summary>
    /// The "designer can't tell which is which" instrument: a comparison sheet
    /// PNG with each specimen glyph rendered by Etch (top row of each pair) and
    /// SkiaSharp (bottom row) at matched ppem, written to the test output as a
    /// build artifact. Both stacks render live; the test asserts the sheet was
    /// produced with real ink.
    /// </summary>
    [Test]
    public async Task SideBySideSpecimen_RendersBothStacks()
    {
        byte[] fontData = TestFonts.RobotoRegular;
        const string text = "Hamburgefonts 0123";
        const float size = 40f;
        float coverageSize = size * RasterDpiScale;

        using var face = FontFace.Load(fontData, 2048, size);
        face.Hinting = FontHinting.None;

        const int cellW = 56;
        const int cellH = 64;
        const int pad = 6;
        int cols = text.Length;
        int sheetW = cols * cellW;
        int sheetH = cellH * 2 + pad;
        var rgba = new byte[sheetW * sheetH * 4];

        // Opaque dark background so the white glyph ink reads unambiguously.
        for (int p = 0; p < sheetW * sheetH; p++)
        {
            rgba[p * 4] = 24;
            rgba[p * 4 + 1] = 24;
            rgba[p * 4 + 2] = 28;
            rgba[p * 4 + 3] = 255;
        }

        long etchInk = 0, skiaInk = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ')
            {
                continue;
            }

            var shaped = Shaper.Shape(new ShapeRequest(c.ToString(), face, BiDiLevel.LeftToRight, "latn"));
            if (shaped.GlyphCount == 0)
            {
                continue;
            }
            ushort gid = shaped.Glyphs[0].GlyphId;

            byte[] etchBuf = ArrayPool<byte>.Shared.Rent(1 << 20);
            byte[] skiaBuf = ArrayPool<byte>.Shared.Rent(1 << 20);
            try
            {
                GlyphRasterizer.Rasterize(face, gid, 0, etchBuf, out int ew, out int eh);
                SkiaGlyphProbe.RenderCharacter(fontData, c.ToString(), coverageSize, 0, skiaBuf, out int kw, out int kh);

                // Etch row (top), flipped bottom-up → top-down.
                etchInk += BlitCell(rgba, sheetW, i * cellW, 0, cellW, cellH, etchBuf, ew, eh, flipVertical: true);
                // Skia row (bottom).
                skiaInk += BlitCell(rgba, sheetW, i * cellW, cellH + pad, cellW, cellH, skiaBuf, kw, kh, flipVertical: false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(etchBuf);
                ArrayPool<byte>.Shared.Return(skiaBuf);
            }
        }

        string outPath = System.IO.Path.Combine(AppContext.BaseDirectory, "text-parity-specimen.png");
        ImageWriter.WriteRgbaToPng(outPath, rgba, sheetW, sheetH);

        await Assert.That(System.IO.File.Exists(outPath)).IsTrue();
        await Assert.That(etchInk).IsGreaterThan(0);
        await Assert.That(skiaInk).IsGreaterThan(0);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Translation-tolerant IoU of two binarized (≥ 50 % coverage) glyph masks,
    /// best over integer shifts in [-2,2]². Etch stores rows bottom-up, so its
    /// rows are read flipped to match Skia's top-down orientation. 1 = identical
    /// shape, ~0 = disjoint.
    /// </summary>
    private static float CoverageIoU(byte[] a, int aw, int ah, byte[] b, int bw, int bh)
    {
        float best = 0f;
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)
            {
                long inter = 0, union = 0;
                int minX = Math.Min(0, dx), maxX = Math.Max(aw, bw + dx);
                int minY = Math.Min(0, dy), maxY = Math.Max(ah, bh + dy);
                for (int y = minY; y < maxY; y++)
                {
                    for (int x = minX; x < maxX; x++)
                    {
                        bool av = x >= 0 && x < aw && y >= 0 && y < ah && a[(ah - 1 - y) * aw + x] >= 128;
                        int bx = x - dx, by = y - dy;
                        bool bv = bx >= 0 && bx < bw && by >= 0 && by < bh && b[by * bw + bx] >= 128;
                        if (av && bv)
                        {
                            inter++;
                        }
                        if (av || bv)
                        {
                            union++;
                        }
                    }
                }
                if (union > 0)
                {
                    float s = (float)inter / union;
                    if (s > best)
                    {
                        best = s;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Blits an A8 glyph coverage into a cell of the RGBA sheet (white ink,
    /// opaque), bottom-aligned and horizontally centred. Returns total ink.
    /// </summary>
    private static long BlitCell(
        byte[] rgba, int sheetW, int cellX, int cellY, int cellW, int cellH,
        byte[] glyph, int gw, int gh, bool flipVertical)
    {
        if (gw <= 0 || gh <= 0)
        {
            return 0;
        }

        int drawW = Math.Min(gw, cellW);
        int drawH = Math.Min(gh, cellH);
        int offX = cellX + (cellW - drawW) / 2;
        int offY = cellY + (cellH - drawH);

        long ink = 0;
        for (int y = 0; y < drawH; y++)
        {
            int srcY = flipVertical ? (gh - 1 - y) : y;
            for (int x = 0; x < drawW; x++)
            {
                byte cov = glyph[srcY * gw + x];
                ink += cov;
                int px = offX + x;
                int py = offY + y;
                int idx = (py * sheetW + px) * 4;
                // Blend white ink onto whatever background is present.
                rgba[idx] = (byte)(rgba[idx] + (255 - rgba[idx]) * cov / 255);
                rgba[idx + 1] = (byte)(rgba[idx + 1] + (255 - rgba[idx + 1]) * cov / 255);
                rgba[idx + 2] = (byte)(rgba[idx + 2] + (255 - rgba[idx + 2]) * cov / 255);
                rgba[idx + 3] = 255;
            }
        }
        return ink;
    }
}
