using System;

namespace Etch.Text.Rasterize;

internal static class FreeTypeGlyphRasterizer
{
    /// <summary>
    /// Measures the glyph's bitmap. With <paramref name="strokeRadius"/> &gt; 0 it measures the
    /// stroked glyph instead: the glyph grown outward by that many pixels (see
    /// <see cref="Rasterize"/>).
    /// </summary>
    public static void Measure(nint face, ushort glyphId, FontHinting hinting, out int w, out int h, bool lcd = false, float subpixelX = 0, float strokeRadius = 0)
    {
        w = 0;
        h = 0;
        if (face == 0)
        {
            return;
        }

        unsafe
        {
            try
            {
                // Apply the same subpixel transform as Rasterize. A shifted
                // outline can rasterize to different bitmap dimensions than an
                // unshifted one, and the caller sizes its buffer from Measure.
                ApplySubpixelShift(face, subpixelX);

                int loadFlags = GetLoadFlags(hinting);
                FT_Error err = FreeTypeNative.FT_Load_Glyph(face, glyphId, loadFlags);
                if (err != FT_Error.FT_Err_Ok)
                {
                    return;
                }

                var glyphSlot = ((FT_FaceRec*)face)->glyph;
                if (strokeRadius > 0)
                {
                    var stroked = RenderStrokedGlyph(glyphSlot, strokeRadius);
                    if (stroked != null)
                    {
                        w = (int)stroked->bitmap.width;
                        h = (int)stroked->bitmap.rows;
                        FreeTypeNative.FT_Done_Glyph((nint)stroked);
                    }
                    return;
                }

                // Render the glyph to get exact bitmap dimensions.
                // FreeType hinting can cause bitmap dimensions to differ from
                // unhinted metrics by 1 pixel, so we must render for accuracy.
                var renderMode = lcd ? FT_Render_Mode.FT_RENDER_MODE_LCD : FT_Render_Mode.FT_RENDER_MODE_NORMAL;
                err = FreeTypeNative.FT_Render_Glyph((nint)glyphSlot, renderMode);
                if (err != FT_Error.FT_Err_Ok)
                {
                    return;
                }

                if (lcd && (FT_Pixel_Mode)glyphSlot->bitmap.pixel_mode == FT_Pixel_Mode.FT_PIXEL_MODE_LCD)
                {
                    // FreeType LCD mode returns 3 bytes per pixel (R,G,B).
                    // The width is 3× the logical pixel width.
                    w = (int)(glyphSlot->bitmap.width / 3);
                }
                else
                {
                    w = (int)glyphSlot->bitmap.width;
                }
                h = (int)glyphSlot->bitmap.rows;
            }
            finally
            {
                FreeTypeNative.FT_Set_Transform(face, 0, 0);
            }
        }
    }

    /// <summary>
    /// Rasterizes the glyph into <paramref name="dest"/> (A8, or RGBA for LCD), bottom row first.
    /// With <paramref name="strokeRadius"/> &gt; 0 it rasterizes the glyph's outline stroke instead:
    /// FreeType's outside border at that radius, which fills the glyph grown outward by
    /// <paramref name="strokeRadius"/> pixels (counters shrink by as much). Drawn under the glyph
    /// itself it outlines the text. Strokes are always A8 coverage, never LCD.
    /// </summary>
    public static void Rasterize(
        nint face,
        ushort glyphId,
        float subpixelX,
        FontHinting hinting,
        Span<byte> dest,
        out int w,
        out int h,
        out int minX,
        out int minY,
        bool lcd = false,
        float strokeRadius = 0)
    {
        w = 0;
        h = 0;
        minX = 0;
        minY = 0;
        if (face == 0)
        {
            return;
        }

        unsafe
        {
            try
            {
                ApplySubpixelShift(face, subpixelX);

                int loadFlags = GetLoadFlags(hinting);
                FT_Error err = FreeTypeNative.FT_Load_Glyph(face, glyphId, loadFlags);
                if (err != FT_Error.FT_Err_Ok)
                {
                    return;
                }

                var glyphSlot = ((FT_FaceRec*)face)->glyph;
                if (strokeRadius > 0)
                {
                    var stroked = RenderStrokedGlyph(glyphSlot, strokeRadius);
                    if (stroked != null)
                    {
                        CopyGrayBitmap(stroked->bitmap, dest, out w, out h);
                        minX = stroked->left;
                        minY = stroked->top - h;
                        FreeTypeNative.FT_Done_Glyph((nint)stroked);
                    }
                    return;
                }

                var renderMode = lcd ? FT_Render_Mode.FT_RENDER_MODE_LCD : FT_Render_Mode.FT_RENDER_MODE_NORMAL;
                err = FreeTypeNative.FT_Render_Glyph((nint)glyphSlot, renderMode);
                if (err != FT_Error.FT_Err_Ok)
                {
                    return;
                }

                var bitmap = glyphSlot->bitmap;
                bool isLcd = lcd && (FT_Pixel_Mode)bitmap.pixel_mode == FT_Pixel_Mode.FT_PIXEL_MODE_LCD;
                minX = glyphSlot->bitmap_left;
                if (isLcd)
                {
                    CopyLcdBitmap(bitmap, dest, out w, out h);
                }
                else
                {
                    CopyGrayBitmap(bitmap, dest, out w, out h);
                }
                minY = glyphSlot->bitmap_top - h;
            }
            finally
            {
                // Always reset transform so subsequent glyphs on this face
                // are not shifted by a stale transform from a previous call.
                FreeTypeNative.FT_Set_Transform(face, 0, 0);
            }
        }
    }

    private static unsafe void ApplySubpixelShift(nint face, float subpixelX)
    {
        if (subpixelX > 0)
        {
            FT_Matrix matrix = new() { xx = 1 << 16, xy = 0, yx = 0, yy = 1 << 16 };
            FT_Vector delta = new() { x = (int)(subpixelX * 64), y = 0 };
            FreeTypeNative.FT_Set_Transform(face, (nint)(&matrix), (nint)(&delta));
        }
    }

    /// <summary>
    /// Strokes the outline glyph loaded in <paramref name="glyphSlot"/> with round joins and
    /// renders its outside border to an A8 bitmap glyph, which the caller frees with
    /// <c>FT_Done_Glyph</c>. Null when the slot holds no outline (a bitmap-only glyph) or FreeType
    /// fails; the glyph then has no stroke.
    /// </summary>
    private static unsafe FT_BitmapGlyphRec* RenderStrokedGlyph(FT_GlyphSlotRec* glyphSlot, float strokeRadius)
    {
        if (glyphSlot->format != FT_Glyph_Format.Outline)
        {
            return null;
        }
        if (FreeTypeNative.FT_Stroker_New(FreeTypeLibrary.Handle, out nint stroker) != FT_Error.FT_Err_Ok)
        {
            return null;
        }

        nint glyph = 0;
        try
        {
            // The radius is in 26.6 pixels; round joins keep sharp corners from spiking.
            FreeTypeNative.FT_Stroker_Set(stroker, (nint)MathF.Round(strokeRadius * 64f),
                FT_Stroker_LineCap.FT_STROKER_LINECAP_ROUND, FT_Stroker_LineJoin.FT_STROKER_LINEJOIN_ROUND, 0);
            if (FreeTypeNative.FT_Get_Glyph((nint)glyphSlot, out glyph) != FT_Error.FT_Err_Ok)
            {
                glyph = 0;
                return null;
            }
            // destroy = 1 replaces the glyph on success; on failure it is left for the finally.
            if (FreeTypeNative.FT_Glyph_StrokeBorder(ref glyph, stroker, inside: 0, destroy: 1) != FT_Error.FT_Err_Ok
                || FreeTypeNative.FT_Glyph_To_Bitmap(ref glyph, FT_Render_Mode.FT_RENDER_MODE_NORMAL, 0, destroy: 1) != FT_Error.FT_Err_Ok)
            {
                return null;
            }

            var bitmapGlyph = (FT_BitmapGlyphRec*)glyph;
            if (bitmapGlyph->format != FT_Glyph_Format.Bitmap
                || (FT_Pixel_Mode)bitmapGlyph->bitmap.pixel_mode != FT_Pixel_Mode.FT_PIXEL_MODE_GRAY)
            {
                return null;
            }
            glyph = 0;
            return bitmapGlyph;
        }
        finally
        {
            if (glyph != 0)
            {
                FreeTypeNative.FT_Done_Glyph(glyph);
            }
            FreeTypeNative.FT_Stroker_Done(stroker);
        }
    }

    /// <summary>Copies an A8 FreeType bitmap into <paramref name="dest"/>, bottom row first.</summary>
    private static unsafe void CopyGrayBitmap(in FT_Bitmap bitmap, Span<byte> dest, out int w, out int h)
    {
        w = (int)bitmap.width;
        h = (int)bitmap.rows;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        int pitch = bitmap.pitch;
        int maxRows = Math.Min(h, dest.Length / w);
        byte* buf = (byte*)bitmap.buffer;
        for (int row = 0; row < maxRows; row++)
        {
            int srcRow = pitch > 0 ? row : (h - 1 - row);
            var source = new ReadOnlySpan<byte>(buf + srcRow * pitch, w);
            source.CopyTo(dest.Slice((h - 1 - row) * w, w));
        }
    }

    /// <summary>
    /// Copies a FreeType LCD bitmap (three bytes per pixel, R, G, B) into <paramref name="dest"/> as
    /// RGBA with alpha the strongest channel, bottom row first. <paramref name="w"/> is the logical
    /// pixel width (a third of FreeType's).
    /// </summary>
    private static unsafe void CopyLcdBitmap(in FT_Bitmap bitmap, Span<byte> dest, out int w, out int h)
    {
        int srcW = (int)bitmap.width;
        w = srcW / 3;
        h = (int)bitmap.rows;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        int pitch = bitmap.pitch;
        int maxRows = Math.Min(h, dest.Length / (w * 4));
        byte* buf = (byte*)bitmap.buffer;
        for (int row = 0; row < maxRows; row++)
        {
            int srcRow = pitch > 0 ? row : (h - 1 - row);
            int srcOffset = srcRow * pitch;
            int dstOffset = (h - 1 - row) * w * 4;
            for (int col = 0; col < w; col++)
            {
                byte r = buf[srcOffset + col * 3 + 0];
                byte g = buf[srcOffset + col * 3 + 1];
                byte b = buf[srcOffset + col * 3 + 2];
                byte a = Math.Max(r, Math.Max(g, b));
                dest[dstOffset + col * 4 + 0] = r;
                dest[dstOffset + col * 4 + 1] = g;
                dest[dstOffset + col * 4 + 2] = b;
                dest[dstOffset + col * 4 + 3] = a;
            }
        }
    }

    private static int GetLoadFlags(FontHinting hinting)
    {
        return hinting switch
        {
            FontHinting.None => FreeTypeNative.FT_LOAD_NO_HINTING,
            FontHinting.Slight => FreeTypeNative.FT_LOAD_TARGET_LIGHT,
            FontHinting.Full => FreeTypeNative.FT_LOAD_TARGET_NORMAL,
            _ => FreeTypeNative.FT_LOAD_DEFAULT,
        };
    }
}
