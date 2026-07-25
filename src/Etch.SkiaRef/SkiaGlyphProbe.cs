using System;
using SkiaSharp;

namespace Etch.SkiaRef;

public static class SkiaGlyphProbe
{
    public static void RenderCharacter(
        ReadOnlySpan<byte> fontData,
        string character,
        float pointSize,
        float subpixelX,
        Span<byte> dest,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;

        if (pointSize <= 0 || string.IsNullOrEmpty(character))
            return;

        using var skData = SKData.CreateCopy(fontData);
        using var typeface = SKTypeface.FromData(skData);
        if (typeface is null)
            return;

        using var font = new SKFont(typeface, pointSize);
        font.Subpixel = true;
        font.Edging = SKFontEdging.Antialias;
        font.Hinting = SKFontHinting.None;

        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        int canvasSize = (int)Math.Ceiling(pointSize * 3) + 8;
        using var bitmap = new SKBitmap(canvasSize, canvasSize, SKColorType.Alpha8, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear();

        float x = 4 + subpixelX;
        float y = canvasSize * 0.75f;

        canvas.DrawText(character, x, y, font, paint);
        canvas.Flush();

        byte[] pixels = bitmap.Bytes;
        if (pixels is null || pixels.Length < canvasSize * canvasSize)
            return;

        int minX = canvasSize, minY = canvasSize, maxX = 0, maxY = 0;
        for (int py = 0; py < canvasSize; py++)
        {
            for (int px = 0; px < canvasSize; px++)
            {
                if (pixels[py * canvasSize + px] > 0)
                {
                    if (px < minX) minX = px;
                    if (px > maxX) maxX = px;
                    if (py < minY) minY = py;
                    if (py > maxY) maxY = py;
                }
            }
        }

        if (minX > maxX)
            return;

        width = maxX - minX + 1;
        height = maxY - minY + 1;

        if (width <= 0 || height <= 0 || width * height > dest.Length)
            return;

        for (int py = minY; py <= maxY; py++)
        {
            int dstY = py - minY;
            for (int px = minX; px <= maxX; px++)
            {
                dest[dstY * width + (px - minX)] = pixels[py * canvasSize + px];
            }
        }
    }

    /// <summary>
    /// WP-3527 weight axis: renders a character as black text on an opaque white
    /// background through Skia's full pipeline (its real mask-gamma), and returns
    /// the mean displayed ink darkness (1 − luminance, 0..1) over inked pixels.
    /// This is the apples-to-apples "displayed weight" metric used to grade Etch's
    /// perceptual text weight against best-in-class. Returns 0 for empty glyphs.
    /// </summary>
    public static double MeanInkBlackOnWhite(ReadOnlySpan<byte> fontData, string character, float pointSize, double inkThreshold = 0.04)
    {
        if (pointSize <= 0 || string.IsNullOrEmpty(character))
            return 0;

        using var skData = SKData.CreateCopy(fontData);
        using var typeface = SKTypeface.FromData(skData);
        if (typeface is null)
            return 0;

        using var font = new SKFont(typeface, pointSize) { Subpixel = true, Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.None };
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true, Style = SKPaintStyle.Fill };

        int canvasSize = (int)Math.Ceiling(pointSize * 3) + 8;
        var info = new SKImageInfo(canvasSize, canvasSize, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText(character, 4, canvasSize * 0.75f, font, paint);
            canvas.Flush();
        }

        byte[] px = bitmap.Bytes;
        if (px is null)
            return 0;

        double sum = 0;
        long n = 0;
        for (int i = 0; i < canvasSize * canvasSize; i++)
        {
            // BGRA; luminance of an opaque pixel (Rec.709).
            double b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
            double ink = 1.0 - lum;
            if (ink > inkThreshold)
            {
                sum += ink;
                n++;
            }
        }
        return n == 0 ? 0 : sum / n;
    }
}
