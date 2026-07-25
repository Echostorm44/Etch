using System;

namespace Etch.Text.Rasterize;

internal static class FreeTypeGlyphRasterizer
{
    public static void Measure(nint face, ushort glyphId, FontHinting hinting, out int w, out int h, bool lcd = false, float subpixelX = 0)
    {
        if (face == 0)
        {
            w = 0;
            h = 0;
            return;
        }

        unsafe
        {
            try
            {
                // Apply the same subpixel transform as Rasterize. A shifted
                // outline can rasterize to different bitmap dimensions than an
                // unshifted one, and the caller sizes its buffer from Measure.
                if (subpixelX > 0)
                {
                    FT_Matrix matrix = new() { xx = 1 << 16, xy = 0, yx = 0, yy = 1 << 16 };
                    FT_Vector delta = new() { x = (int)(subpixelX * 64), y = 0 };
                    FreeTypeNative.FT_Set_Transform(face, (nint)(&matrix), (nint)(&delta));
                }

                int loadFlags = GetLoadFlags(hinting);
                FT_Error err = FreeTypeNative.FT_Load_Glyph(face, glyphId, loadFlags);
                if (err != FT_Error.FT_Err_Ok)
                {
                    w = 0;
                    h = 0;
                    return;
                }

                // Render the glyph to get exact bitmap dimensions.
                // FreeType hinting can cause bitmap dimensions to differ from
                // unhinted metrics by 1 pixel, so we must render for accuracy.
                var glyphSlot = ((FT_FaceRec*)face)->glyph;
                var renderMode = lcd ? FT_Render_Mode.FT_RENDER_MODE_LCD : FT_Render_Mode.FT_RENDER_MODE_NORMAL;
                err = FreeTypeNative.FT_Render_Glyph((nint)glyphSlot, renderMode);
                if (err != FT_Error.FT_Err_Ok)
                {
                    w = 0;
                    h = 0;
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
        bool lcd = false)
    {
        if (face == 0)
        {
            w = 0;
            h = 0;
            minX = 0;
            minY = 0;
            return;
        }

        unsafe
        {
            try
            {
                // Apply subpixel shift via transform
                if (subpixelX > 0)
                {
                    FT_Matrix matrix = new() { xx = 1 << 16, xy = 0, yx = 0, yy = 1 << 16 };
                    FT_Vector delta = new() { x = (int)(subpixelX * 64), y = 0 };
                    FreeTypeNative.FT_Set_Transform(face, (nint)(&matrix), (nint)(&delta));
                }

                int loadFlags = GetLoadFlags(hinting);
                FT_Error err = FreeTypeNative.FT_Load_Glyph(face, glyphId, loadFlags);
                if (err != FT_Error.FT_Err_Ok)
                {
                    w = 0; h = 0; minX = 0; minY = 0;
                    return;
                }

                var glyphSlot = ((FT_FaceRec*)face)->glyph;
                var renderMode = lcd ? FT_Render_Mode.FT_RENDER_MODE_LCD : FT_Render_Mode.FT_RENDER_MODE_NORMAL;
                err = FreeTypeNative.FT_Render_Glyph((nint)glyphSlot, renderMode);
                if (err != FT_Error.FT_Err_Ok)
                {
                    w = 0; h = 0; minX = 0; minY = 0;
                    return;
                }

                var bitmap = glyphSlot->bitmap;
                bool isLcd = lcd && (FT_Pixel_Mode)bitmap.pixel_mode == FT_Pixel_Mode.FT_PIXEL_MODE_LCD;
                int srcW = (int)bitmap.width;
                h = (int)bitmap.rows;
                minX = glyphSlot->bitmap_left;
                minY = glyphSlot->bitmap_top - h;

                if (srcW > 0 && h > 0)
                {
                    int pitch = (int)bitmap.pitch;

                    if (isLcd)
                    {
                        // LCD mode: source is 3 bytes per pixel (R,G,B).
                        // Logical width is source width / 3.
                        w = srcW / 3;
                        int destCapacity = dest.Length;
                        int maxRows = Math.Min(h, destCapacity / (w * 4));

                        for (int row = 0; row < maxRows; row++)
                        {
                            int srcRow = pitch > 0 ? row : (h - 1 - row);
                            int srcOffset = srcRow * pitch;
                            int dstOffset = (h - 1 - row) * w * 4;

                            unsafe
                            {
                                byte* buf = (byte*)bitmap.buffer;
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
                    }
                    else
                    {
                        // Normal A8 mode: source is 1 byte per pixel.
                        w = srcW;
                        int destCapacity = dest.Length;
                        int maxRows = Math.Min(h, destCapacity / w);

                        for (int row = 0; row < maxRows; row++)
                        {
                            int srcRow = pitch > 0 ? row : (h - 1 - row);
                            int srcOffset = srcRow * pitch;
                            int dstOffset = (h - 1 - row) * w;

                            unsafe
                            {
                                byte* buf = (byte*)bitmap.buffer;
                                for (int col = 0; col < w; col++)
                                {
                                    dest[dstOffset + col] = buf[srcOffset + col];
                                }
                            }
                        }
                    }
                }
                else
                {
                    w = isLcd ? srcW / 3 : srcW;
                }
            }
            finally
            {
                // Always reset transform so subsequent glyphs on this face
                // are not shifted by a stale transform from a previous call.
                FreeTypeNative.FT_Set_Transform(face, 0, 0);
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
