using System;
using System.Collections.Concurrent;
using Etch.Geometry;
using Etch.Text.Shape;
using Etch.Text.HarfBuzz;

namespace Etch.Text.Outline;

public static class GlyphOutlineBuilder
{
    private static readonly Tag GlyfTag = new Tag('g', 'l', 'y', 'f');
    private static readonly Tag LocaTag = new Tag('l', 'o', 'c', 'a');
    private static readonly Tag HeadTag = new Tag('h', 'e', 'a', 'd');

    public static BezPath? Build(FontFace face, ushort glyphId, BezPathBuilder dest)
    {
        if (face == null)
        {
            Panic.ArgumentNull(nameof(face));
        }

        var hbFace = GetHarfBuzzFace(face);
        if (hbFace == null)
        {
            dest.Dispose();
            return null;
        }

        var locaBlob = hbFace.ReferenceTable(LocaTag);
        var glyfBlob = hbFace.ReferenceTable(GlyfTag);
        var headBlob = hbFace.ReferenceTable(HeadTag);

        try
        {
            var locaSpan = locaBlob.AsSpan();
            var glyfSpan = glyfBlob.AsSpan();
            var headSpan = headBlob.AsSpan();

            if (locaSpan.Length == 0 || glyfSpan.Length == 0 || headSpan.Length == 0)
            {
                dest.Dispose();
                return null;
            }

            if (headSpan.Length < 54)
            {
                dest.Dispose();
                return null;
            }

            var indexToLocFormat = ReadInt16(headSpan, 50);
            if (indexToLocFormat != 0 && indexToLocFormat != 1)
            {
                dest.Dispose();
                return null;
            }

            var locaOffset = GetGlyphOffset(locaSpan, glyphId, indexToLocFormat, glyfSpan.Length);
            if (locaOffset < 0 || (uint)locaOffset >= glyfSpan.Length)
            {
                dest.Dispose();
                return null;
            }

            var numberOfContours = ReadInt16(glyfSpan, locaOffset);
            if (numberOfContours == -1)
            {
                return BuildComposite(face, glyfSpan, locaOffset, dest);
            }

            if (numberOfContours == 0)
            {
                return dest.Build();
            }

            var contourOffset = locaOffset + 10;
            var endPtsOfContours = new ushort[numberOfContours];
            for (int i = 0; i < numberOfContours; i++)
            {
                endPtsOfContours[i] = ReadUInt16(glyfSpan, contourOffset + i * 2);
            }

            var instructionLength = ReadUInt16(glyfSpan, contourOffset + numberOfContours * 2);
            var pointOffset = contourOffset + numberOfContours * 2 + 2 + instructionLength;

            var numPoints = endPtsOfContours[numberOfContours - 1] + 1;

            var flags = new byte[numPoints];
            var xCoordinates = new short[numPoints];
            var yCoordinates = new short[numPoints];

            int idx = 0;
            int currentOffset = pointOffset;
            while (idx < numPoints)
            {
                var flag = glyfSpan[currentOffset++];
                flags[idx] = flag;

                if ((flag & 8) != 0)
                {
                    var repeat = glyfSpan[currentOffset++];
                    for (int j = 0; j < repeat && idx + j + 1 < numPoints; j++)
                    {
                        flags[idx + j + 1] = flag;
                    }
                    idx += repeat + 1;
                }
                else
                {
                    idx++;
                }
            }



            short x = 0;
            for (idx = 0; idx < numPoints; idx++)
            {
                var flag = flags[idx];
                if ((flag & 2) != 0)
                {
                    if ((flag & 16) != 0)
                    {
                        x += (short)glyfSpan[currentOffset++];
                    }
                    else
                    {
                        x -= (short)glyfSpan[currentOffset++];
                    }
                }
                else if ((flag & 16) == 0)
                {
                    x = (short)(x + ReadInt16(glyfSpan, currentOffset));
                    currentOffset += 2;
                }
                xCoordinates[idx] = x;
            }

            short y = 0;
            for (idx = 0; idx < numPoints; idx++)
            {
                var flag = flags[idx];
                if ((flag & 4) != 0)
                {
                    if ((flag & 32) != 0)
                    {
                        y += (short)glyfSpan[currentOffset++];
                    }
                    else
                    {
                        y -= (short)glyfSpan[currentOffset++];
                    }
                }
                else if ((flag & 32) == 0)
                {
                    y = (short)(y + ReadInt16(glyfSpan, currentOffset));
                    currentOffset += 2;
                }
                yCoordinates[idx] = y;
            }

            float scale = 1f;
            int startPoint = 0;
            for (int contourIdx = 0; contourIdx < numberOfContours; contourIdx++)
            {
                var endPoint = endPtsOfContours[contourIdx];

                dest.MoveTo(new Point((float)xCoordinates[startPoint] * scale, (float)yCoordinates[startPoint] * scale));

                int i = startPoint + 1;
                while (i <= endPoint)
                {
                    if ((flags[i] & 1) != 0)
                    {
                        // On-curve point: emit a straight line to it.
                        dest.LineTo(new Point((float)xCoordinates[i] * scale, (float)yCoordinates[i] * scale));
                        i++;
                    }
                    else
                    {
                        // Off-curve point: start of a quadratic Bezier.
                        float cx = (float)xCoordinates[i] * scale;
                        float cy = (float)yCoordinates[i] * scale;
                        i++;

                        float ex, ey;
                        if (i > endPoint)
                        {
                            // Wrap around to the contour start point.
                            ex = (float)xCoordinates[startPoint] * scale;
                            ey = (float)yCoordinates[startPoint] * scale;
                        }
                        else if ((flags[i] & 1) == 0)
                        {
                            // Two consecutive off-curve points: implied on-curve at midpoint.
                            float nextX = (float)xCoordinates[i] * scale;
                            float nextY = (float)yCoordinates[i] * scale;
                            ex = (cx + nextX) * 0.5f;
                            ey = (cy + nextY) * 0.5f;
                            // Do NOT increment i here; the next off-curve becomes the control
                            // for the following segment in the next iteration.
                        }
                        else
                        {
                            // Off-curve followed by on-curve: standard quadratic.
                            ex = (float)xCoordinates[i] * scale;
                            ey = (float)yCoordinates[i] * scale;
                            i++;
                        }

                        dest.QuadTo(new Point(cx, cy), new Point(ex, ey));
                    }
                }

                dest.Close();
                startPoint = endPoint + 1;
            }

            return dest.Build();
        }
        finally
        {
            locaBlob.Dispose();
            glyfBlob.Dispose();
            headBlob.Dispose();
        }
    }

    private static int GetGlyphOffset(Span<byte> locaSpan, ushort glyphId, int indexToLocFormat, int glyfLength)
    {
        if (indexToLocFormat == 0)
        {
            var entryOffset = glyphId * 2;
            if (entryOffset + 2 > locaSpan.Length)
            {
                return -1;
            }
            var lo = (uint)(locaSpan[entryOffset] << 8 | locaSpan[entryOffset + 1]);
            if (lo == 0xFFFF)
            {
                return -1;
            }
            return (int)(lo * 2);
        }
        else
        {
            var entryOffset = glyphId * 4;
            if (entryOffset + 4 > locaSpan.Length)
            {
                return -1;
            }
            return (int)(uint)(locaSpan[entryOffset] << 24 | locaSpan[entryOffset + 1] << 16 | locaSpan[entryOffset + 2] << 8 | locaSpan[entryOffset + 3]);
        }
    }

    /// <summary>
    /// Returns true if the glyph is a composite glyph (uses other glyphs as components).
    /// </summary>
    public static bool IsCompositeGlyph(FontFace face, ushort glyphId)
    {
        var hbFace = GetHarfBuzzFace(face);
        if (hbFace == null)
        {
            return false;
        }

        var locaBlob = hbFace.ReferenceTable(LocaTag);
        var glyfBlob = hbFace.ReferenceTable(GlyfTag);
        var headBlob = hbFace.ReferenceTable(HeadTag);

        try
        {
            var locaSpan = locaBlob.AsSpan();
            var glyfSpan = glyfBlob.AsSpan();
            var headSpan = headBlob.AsSpan();

            if (locaSpan.Length == 0 || glyfSpan.Length == 0 || headSpan.Length < 54)
            {
                return false;
            }

            var indexToLocFormat = ReadInt16(headSpan, 50);
            if (indexToLocFormat != 0 && indexToLocFormat != 1)
            {
                return false;
            }

            var locaOffset = GetGlyphOffset(locaSpan, glyphId, indexToLocFormat, glyfSpan.Length);
            if (locaOffset < 0 || (uint)locaOffset >= glyfSpan.Length)
            {
                return false;
            }

            var numberOfContours = ReadInt16(glyfSpan, locaOffset);
            return numberOfContours == -1;
        }
        finally
        {
            locaBlob.Dispose();
            glyfBlob.Dispose();
            headBlob.Dispose();
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static short ReadInt16(Span<byte> data, int offset)
    {
        return (short)(data[offset] << 8 | data[offset + 1]);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static ushort ReadUInt16(Span<byte> data, int offset)
    {
        return (ushort)(data[offset] << 8 | data[offset + 1]);
    }

    private static Face? GetHarfBuzzFace(FontFace face)
    {
        var fontField = typeof(FontFace).GetField("_face", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (Face?)fontField?.GetValue(face);
    }

    private static BezPath? BuildComposite(FontFace face, Span<byte> glyfSpan, int glyphOffset, BezPathBuilder dest)
    {
        int cursor = glyphOffset + 10;

        while (true)
        {
            if (cursor + 4 > glyfSpan.Length)
            {
                return null;
            }

            ushort flags = ReadUInt16(glyfSpan, cursor);
            ushort componentGlyphId = ReadUInt16(glyfSpan, cursor + 2);
            cursor += 4;

            bool argWords = (flags & 0x0001) != 0;
            bool argsAreXY = (flags & 0x0002) != 0;

            float arg1, arg2;
            if (argWords)
            {
                if (cursor + 4 > glyfSpan.Length)
                {
                    return null;
                }

                arg1 = ReadInt16(glyfSpan, cursor);
                arg2 = ReadInt16(glyfSpan, cursor + 2);
                cursor += 4;
            }
            else
            {
                if (cursor + 2 > glyfSpan.Length)
                {
                    return null;
                }

                arg1 = (sbyte)glyfSpan[cursor];
                arg2 = (sbyte)glyfSpan[cursor + 1];
                cursor += 2;
            }

            double a = 1.0, b = 0.0, c = 0.0, d = 1.0;

            if ((flags & 0x0008) != 0) // WE_HAVE_A_SCALE
            {
                if (cursor + 2 > glyfSpan.Length)
                {
                    return null;
                }

                double scale = F2Dot14(ReadInt16(glyfSpan, cursor));
                a = scale;
                d = scale;
                cursor += 2;
            }
            else if ((flags & 0x0040) != 0) // WE_HAVE_AN_X_AND_Y_SCALE
            {
                if (cursor + 4 > glyfSpan.Length)
                {
                    return null;
                }

                a = F2Dot14(ReadInt16(glyfSpan, cursor));
                d = F2Dot14(ReadInt16(glyfSpan, cursor + 2));
                cursor += 4;
            }
            else if ((flags & 0x0080) != 0) // WE_HAVE_A_TWO_BY_TWO
            {
                if (cursor + 8 > glyfSpan.Length)
                {
                    return null;
                }

                a = F2Dot14(ReadInt16(glyfSpan, cursor));
                b = F2Dot14(ReadInt16(glyfSpan, cursor + 2));
                c = F2Dot14(ReadInt16(glyfSpan, cursor + 4));
                d = F2Dot14(ReadInt16(glyfSpan, cursor + 6));
                cursor += 8;
            }

            float dx = argsAreXY ? arg1 : 0f;
            float dy = argsAreXY ? arg2 : 0f;

            var tempBuilder = BezPathBuilder.Begin();
            try
            {
                var componentPath = Build(face, componentGlyphId, tempBuilder);
                if (componentPath.HasValue)
                {
                    var path = componentPath.Value;
                    if (!path.IsEmpty)
                    {
                        var affine = new Affine(a, b, c, d, dx, dy);
                        var transformed = path.TransformedBy(affine);

                        var enumerator = transformed.Iterate();
                        while (enumerator.MoveNext())
                        {
                            var seg = enumerator.Current;
                            switch (seg.Verb)
                            {
                                case PathVerb.MoveTo:
                                    dest.MoveTo(seg.End);
                                    break;
                                case PathVerb.LineTo:
                                    dest.LineTo(seg.End);
                                    break;
                                case PathVerb.QuadTo:
                                    dest.QuadTo(seg.Control0, seg.End);
                                    break;
                                case PathVerb.CubicTo:
                                    dest.CubicTo(seg.Control0, seg.Control1, seg.End);
                                    break;
                                case PathVerb.Close:
                                    dest.Close();
                                    break;
                            }
                        }
                    }
                }
            }
            finally
            {
                tempBuilder.Dispose();
            }

            if ((flags & 0x0020) == 0) // no MORE_COMPONENTS
            {
                break;
            }
        }

        return dest.Build();
    }

    private static double F2Dot14(short value)
    {
        return value / 16384.0;
    }

    // ════════════════════════════════════════════════════════════════
    // COLR / CPAL color-font support
    // ════════════════════════════════════════════════════════════════

    private static readonly Tag ColrTag = new Tag('C', 'O', 'L', 'R');
    private static readonly Tag CpalTag = new Tag('C', 'P', 'A', 'L');

    // CPAL palette cache: keyed by native face handle. Palettes are per-font,
    // not per-glyph, so parsing once and reusing avoids repeated LOH allocations.
    private static readonly ConcurrentDictionary<nint, GlyphColorValue[]> s_paletteCache = new();

    /// <summary>
    /// Parses the CPAL table once per font face and caches the result.
    /// Returns false if the font has no CPAL table or the palette is malformed.
    /// </summary>
    private static bool TryGetCachedPalette(FontFace face, out GlyphColorValue[] palette)
    {
        palette = Array.Empty<GlyphColorValue>();

        var hbFace = GetHarfBuzzFace(face);
        if (hbFace == null)
        {
            return false;
        }

        nint key = hbFace.Handle;
        if (s_paletteCache.TryGetValue(key, out var cachedPalette))
        {
            palette = cachedPalette;
            return palette.Length > 0;
        }

        using var cpalBlob = hbFace.ReferenceTable(CpalTag);
        var cpalSpan = cpalBlob.AsSpan();

        if (cpalSpan.Length < 12)
        {
            s_paletteCache.TryAdd(key, Array.Empty<GlyphColorValue>());
            return false;
        }

        ushort cpalVersion = ReadUInt16(cpalSpan, 0);
        if (cpalVersion > 1)
        {
            s_paletteCache.TryAdd(key, Array.Empty<GlyphColorValue>());
            return false;
        }

        ushort numPaletteEntries = ReadUInt16(cpalSpan, 2);
        ushort numPalettes = ReadUInt16(cpalSpan, 4);
        ushort numColorRecords = ReadUInt16(cpalSpan, 6);
        uint offsetFirstColorRecord = (uint)(cpalSpan[8] << 24 | cpalSpan[9] << 16 | cpalSpan[10] << 8 | cpalSpan[11]);

        if (offsetFirstColorRecord + numColorRecords * 4 > cpalSpan.Length)
        {
            s_paletteCache.TryAdd(key, Array.Empty<GlyphColorValue>());
            return false;
        }

        int paletteIndex = 0;
        if (paletteIndex >= numPalettes)
        {
            s_paletteCache.TryAdd(key, Array.Empty<GlyphColorValue>());
            return false;
        }

        int colorRecordIndex;
        if (cpalVersion == 0)
        {
            colorRecordIndex = paletteIndex * numPaletteEntries;
        }
        else
        {
            int indicesOffset = 12 + paletteIndex * 2;
            if (indicesOffset + 2 > cpalSpan.Length)
            {
                s_paletteCache.TryAdd(key, Array.Empty<GlyphColorValue>());
                return false;
            }
            colorRecordIndex = ReadUInt16(cpalSpan, indicesOffset);
        }

        palette = new GlyphColorValue[numPaletteEntries];
        for (int i = 0; i < numPaletteEntries; i++)
        {
            int recordIdx = colorRecordIndex + i;
            if (recordIdx >= numColorRecords)
            {
                palette[i] = new GlyphColorValue(0, 0, 0, 255);
                continue;
            }

            int offset = (int)offsetFirstColorRecord + recordIdx * 4;
            byte b = cpalSpan[offset];
            byte g = cpalSpan[offset + 1];
            byte r = cpalSpan[offset + 2];
            byte a = cpalSpan[offset + 3];
            palette[i] = new GlyphColorValue(r, g, b, a);
        }

        s_paletteCache.TryAdd(key, palette);
        return true;
    }

    /// <summary>
    /// Returns true if the font has a COLR table and the glyph has color layers.
    /// </summary>
    public static bool HasColorLayers(FontFace face, ushort glyphId)
    {
        return TryGetColorLayers(face, glyphId, out _, out _);
    }

    /// <summary>
    /// Parses the COLR table for the glyph and returns the cached CPAL palette.
    /// CPAL is parsed once per font face and reused; COLR is parsed per-glyph.
    /// </summary>
    public static bool TryGetColorLayers(FontFace face, ushort glyphId, out ColorLayer[] layers, out GlyphColorValue[] palette)
    {
        layers = Array.Empty<ColorLayer>();
        palette = Array.Empty<GlyphColorValue>();

        if (!TryGetCachedPalette(face, out palette))
        {
            return false;
        }

        var hbFace = GetHarfBuzzFace(face);
        if (hbFace == null)
        {
            return false;
        }

        using var colrBlob = hbFace.ReferenceTable(ColrTag);
        var colrSpan = colrBlob.AsSpan();

        if (colrSpan.Length < 14)
        {
            return false;
        }

        // Parse COLR header (v0 or v1)
        ushort colrVersion = ReadUInt16(colrSpan, 0);
        if (colrVersion != 0 && colrVersion != 1)
        {
            return false;
        }

        ushort numBaseGlyphRecords = ReadUInt16(colrSpan, 2);
        uint offsetBaseGlyphRecord = (uint)(colrSpan[4] << 24 | colrSpan[5] << 16 | colrSpan[6] << 8 | colrSpan[7]);
        uint offsetLayerRecord = (uint)(colrSpan[8] << 24 | colrSpan[9] << 16 | colrSpan[10] << 8 | colrSpan[11]);
        ushort numLayerRecords = ReadUInt16(colrSpan, 12);

        if (offsetBaseGlyphRecord + numBaseGlyphRecords * 6 > colrSpan.Length ||
            offsetLayerRecord + numLayerRecords * 4 > colrSpan.Length)
        {
            return false;
        }

        // Find the base glyph record
        int firstLayerIndex = -1;
        int numLayers = 0;
        for (int i = 0; i < numBaseGlyphRecords; i++)
        {
            int offset = (int)offsetBaseGlyphRecord + i * 6;
            ushort recordGlyphId = ReadUInt16(colrSpan, offset);
            if (recordGlyphId == glyphId)
            {
                firstLayerIndex = ReadUInt16(colrSpan, offset + 2);
                numLayers = ReadUInt16(colrSpan, offset + 4);
                break;
            }
        }

        if (firstLayerIndex < 0 || numLayers <= 0)
        {
            return false;
        }

        if (firstLayerIndex + numLayers > numLayerRecords)
        {
            return false;
        }

        // Read layer records
        layers = new ColorLayer[numLayers];
        for (int i = 0; i < numLayers; i++)
        {
            int offset = (int)offsetLayerRecord + (firstLayerIndex + i) * 4;
            layers[i] = new ColorLayer(
                ReadUInt16(colrSpan, offset),
                ReadUInt16(colrSpan, offset + 2));
        }

        return true;
    }
}

/// <summary>
/// A single layer in a COLR color glyph: a glyph ID and a palette index.
/// </summary>
public readonly record struct ColorLayer(ushort GlyphId, ushort PaletteIndex);

/// <summary>
/// Simple RGBA color value for CPAL palette entries.
/// </summary>
public readonly record struct GlyphColorValue(byte R, byte G, byte B, byte A);