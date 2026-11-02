using System;
using Etch.Geometry;
using Etch.Text.Shape;

namespace Etch.Text.Outline;

/// <summary>
/// A glyph's outline as FreeType loads it, in font units (y up). Used for variable fonts: the
/// glyf table holds only the default instance, while FreeType applies the face's design
/// coordinates (gvar deltas) when it loads the glyph.
/// </summary>
internal static class FreeTypeGlyphOutline
{
    /// <summary>
    /// Builds the outline of <paramref name="glyphId"/> into <paramref name="dest"/> (disposed on
    /// failure). Null when the glyph cannot be loaded as an outline.
    /// </summary>
    public static unsafe BezPath? Build(FontFace face, ushort glyphId, BezPathBuilder dest)
    {
        nint ftFace = face.FreeTypeFace;
        if (FreeTypeNative.FT_Load_Glyph(ftFace, glyphId, FreeTypeNative.FT_LOAD_NO_SCALE | FreeTypeNative.FT_LOAD_NO_BITMAP) != FT_Error.FT_Err_Ok)
        {
            dest.Dispose();
            return null;
        }
        var slot = ((FT_FaceRec*)ftFace)->glyph;
        if (slot->format != FT_Glyph_Format.Outline)
        {
            dest.Dispose();
            return null;
        }

        var outline = slot->outline;
        int start = 0;
        for (int contour = 0; contour < outline.n_contours; contour++)
        {
            int end = outline.contours[contour];
            if (end < start || end >= outline.n_points)
            {
                break;
            }
            AppendContour(ref dest, outline.points + start, outline.tags + start, end - start + 1);
            start = end + 1;
        }
        return dest.Build();
    }

    /// <summary>
    /// Appends one closed contour. TrueType contours may run two conic controls in a row with an
    /// implied on-curve point midway between them, and may contain no on-curve point at all.
    /// </summary>
    private static unsafe void AppendContour(ref BezPathBuilder dest, FT_Vector* points, byte* tags, int count)
    {
        if (count < 2)
        {
            return;
        }

        int firstOnCurve = -1;
        for (int i = 0; i < count; i++)
        {
            if ((tags[i] & 1) != 0)
            {
                firstOnCurve = i;
                break;
            }
        }

        Point start;
        int next;
        int steps;
        if (firstOnCurve >= 0)
        {
            start = At(points, firstOnCurve);
            next = firstOnCurve + 1;
            steps = count - 1;
        }
        else
        {
            start = Midpoint(At(points, count - 1), At(points, 0));
            next = 0;
            steps = count;
        }

        dest.MoveTo(start);
        Point? conic = null;
        Point? cubic1 = null;
        Point? cubic2 = null;
        for (int step = 0; step < steps; step++)
        {
            int index = (next + step) % count;
            var point = At(points, index);
            byte tag = tags[index];
            if ((tag & 1) != 0)
            {
                EmitTo(ref dest, point, ref conic, ref cubic1, ref cubic2);
            }
            else if ((tag & 2) != 0)
            {
                if (cubic1 is null)
                {
                    cubic1 = point;
                }
                else
                {
                    cubic2 = point;
                }
            }
            else
            {
                if (conic is Point previous)
                {
                    var implied = Midpoint(previous, point);
                    dest.QuadTo(previous, implied);
                }
                conic = point;
            }
        }
        EmitTo(ref dest, start, ref conic, ref cubic1, ref cubic2);
        dest.Close();
    }

    private static void EmitTo(ref BezPathBuilder dest, Point end, ref Point? conic, ref Point? cubic1, ref Point? cubic2)
    {
        if (conic is Point control)
        {
            dest.QuadTo(control, end);
        }
        else if (cubic1 is Point c1)
        {
            dest.CubicTo(c1, cubic2 ?? c1, end);
        }
        else
        {
            dest.LineTo(end);
        }
        conic = null;
        cubic1 = null;
        cubic2 = null;
    }

    private static unsafe Point At(FT_Vector* points, int index) => new(points[index].x, points[index].y);

    private static Point Midpoint(Point a, Point b) => new((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
}
