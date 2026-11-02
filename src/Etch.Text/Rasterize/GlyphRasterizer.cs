using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Text.Atlas;
using Etch.Text.Outline;
using Etch.Text.Shape;

namespace Etch.Text.Rasterize;

public static class GlyphRasterizer
{
    /// <summary>
    /// The size of the bitmap <see cref="Rasterize(FontFace, ushort, float, Span{byte}, out int, out int, out int, out int, float)"/>
    /// produces for the same arguments (without FreeType it may add a column for a subpixel shift).
    /// </summary>
    public static void Measure(
        FontFace face,
        ushort glyphId,
        out int w,
        out int h,
        float subpixelX = 0,
        float outlineWidth = 0)
    {
        if (outlineWidth > 0 && GlyphOutlineBuilder.TryGetColorLayers(face, glyphId, out var layers, out _))
        {
            RasterizeColorGlyphOutline(face, glyphId, layers, subpixelX, outlineWidth, Span<byte>.Empty, measureOnly: true, out w, out h, out _, out _);
            return;
        }
        MeasureMonoGlyph(face, glyphId, out w, out h, subpixelX, outlineWidth);
    }

    private static void MeasureMonoGlyph(FontFace face, ushort glyphId, out int w, out int h, float subpixelX, float outlineWidth)
    {
        // Use FreeType for all glyph measurement when available.
        // Our custom outline builder and rasterizer produce incorrect bounding
        // boxes and bitmaps for some TrueType outlines (especially hinted glyphs
        // and fonts with composite references). FreeType is the authoritative
        // renderer and produces correct results in all cases.
        // subpixelX must match the value later passed to Rasterize — the shifted
        // outline can produce different bitmap dimensions than the unshifted one.
        if (face.FreeTypeFace != 0)
        {
            FreeTypeGlyphRasterizer.Measure(face.FreeTypeFace, glyphId, face.Hinting, out w, out h, lcd: false, subpixelX, outlineWidth);
            return;
        }

        var builder = BezPathBuilder.Begin(64);
        var pathOpt = GlyphOutlineBuilder.Build(face, glyphId, builder);
        builder.Dispose();

        if (!pathOpt.HasValue)
        {
            w = 0;
            h = 0;
            return;
        }
        var path = pathOpt.Value;

        if (path.IsEmpty)
        {
            w = 0;
            h = 0;
            return;
        }

        float pixelScale = face.PointSize * 96.0f / (72.0f * face.UnitsPerEm);
        ComputeDimensions(path, pixelScale, out w, out h, out _, out _);
        if (outlineWidth > 0 && w > 0 && h > 0)
        {
            int pad = OutlinePadding(outlineWidth);
            w += 2 * pad;
            h += 2 * pad;
        }
    }

    public static void Rasterize(
        FontFace face,
        ushort glyphId,
        float subpixelX,
        Span<byte> dest,
        out int w,
        out int h)
    {
        Rasterize(face, glyphId, subpixelX, dest, out w, out h, out _, out _);
    }

    /// <summary>
    /// Rasterizes the glyph's A8 coverage into <paramref name="dest"/>, bottom row first, with its
    /// bearings in <paramref name="minX"/> / <paramref name="minY"/> (pixels, y up). With
    /// <paramref name="outlineWidth"/> &gt; 0 it rasterizes the glyph's outline instead: the glyph
    /// grown outward by that many pixels with round corners. Drawn under the glyph itself, in
    /// another colour, it outlines the text. A colour glyph's outline is that of its whole
    /// picture (base glyph and layers).
    /// </summary>
    public static void Rasterize(
        FontFace face,
        ushort glyphId,
        float subpixelX,
        Span<byte> dest,
        out int w,
        out int h,
        out int minX,
        out int minY,
        float outlineWidth = 0)
    {
        if (outlineWidth > 0 && GlyphOutlineBuilder.TryGetColorLayers(face, glyphId, out var layers, out _))
        {
            RasterizeColorGlyphOutline(face, glyphId, layers, subpixelX, outlineWidth, dest, measureOnly: false, out w, out h, out minX, out minY);
            return;
        }
        RasterizeMonoGlyph(face, glyphId, subpixelX, dest, out w, out h, out minX, out minY, outlineWidth);
    }

    private static void RasterizeMonoGlyph(
        FontFace face,
        ushort glyphId,
        float subpixelX,
        Span<byte> dest,
        out int w,
        out int h,
        out int minX,
        out int minY,
        float outlineWidth)
    {
        // Use FreeType for all glyph rasterization when available.
        // Our custom outline builder and rasterizer produce incorrect bounding
        // boxes and bitmaps for some TrueType outlines. FreeType is authoritative.
        if (face.FreeTypeFace != 0)
        {
            FreeTypeGlyphRasterizer.Rasterize(face.FreeTypeFace, glyphId, subpixelX, face.Hinting, dest, out w, out h, out minX, out minY, lcd: false, outlineWidth);
            return;
        }

        if (outlineWidth <= 0)
        {
            RasterizeOwnOutline(face, glyphId, subpixelX, dest, out w, out h, out minX, out minY);
            return;
        }

        // Without FreeType's stroker, grow the glyph's own coverage instead.
        MeasureMonoGlyph(face, glyphId, out int glyphW, out int glyphH, subpixelX, 0f);
        int glyphSize = (glyphW + 1) * glyphH;
        if (glyphSize <= 0)
        {
            w = 0; h = 0; minX = 0; minY = 0;
            return;
        }
        byte[] glyphCoverage = ArrayPool<byte>.Shared.Rent(glyphSize);
        try
        {
            RasterizeOwnOutline(face, glyphId, subpixelX, glyphCoverage.AsSpan(0, glyphSize), out glyphW, out glyphH, out minX, out minY);
            GrowCoverage(glyphCoverage.AsSpan(0, glyphW * glyphH), glyphW, glyphH, outlineWidth, dest, out w, out h);
            int pad = OutlinePadding(outlineWidth);
            minX -= pad;
            minY -= pad;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(glyphCoverage);
        }
    }

    /// <summary>
    /// The outline of a COLR colour glyph: the union of the outlines of its base glyph and of every
    /// layer. Growing distributes over union, so this is the outline of the whole picture (the base
    /// glyph alone is often empty). With <paramref name="measureOnly"/> it only reports the size.
    /// </summary>
    private static void RasterizeColorGlyphOutline(
        FontFace face,
        ushort glyphId,
        ColorLayer[] layers,
        float subpixelX,
        float outlineWidth,
        Span<byte> dest,
        bool measureOnly,
        out int w,
        out int h,
        out int minX,
        out int minY)
    {
        w = 0; h = 0; minX = 0; minY = 0;
        var parts = new (byte[] Coverage, int W, int H, int MinX, int MinY)[layers.Length + 1];
        try
        {
            int left = int.MaxValue, bottom = int.MaxValue, right = int.MinValue, top = int.MinValue;
            for (int i = 0; i < parts.Length; i++)
            {
                ushort partGlyph = i == 0 ? glyphId : layers[i - 1].GlyphId;
                MeasureMonoGlyph(face, partGlyph, out int partW, out int partH, subpixelX, outlineWidth);
                if (partW <= 0 || partH <= 0)
                {
                    continue;
                }
                // Etch's own rasterizer adds a column for a subpixel shift that Measure leaves out.
                byte[] coverage = ArrayPool<byte>.Shared.Rent((partW + 1) * partH);
                parts[i].Coverage = coverage;
                RasterizeMonoGlyph(face, partGlyph, subpixelX, coverage, out partW, out partH, out int partMinX, out int partMinY, outlineWidth);
                if (partW <= 0 || partH <= 0)
                {
                    continue;
                }
                parts[i] = (coverage, partW, partH, partMinX, partMinY);
                left = Math.Min(left, partMinX);
                bottom = Math.Min(bottom, partMinY);
                right = Math.Max(right, partMinX + partW);
                top = Math.Max(top, partMinY + partH);
            }
            if (left == int.MaxValue)
            {
                return;
            }

            w = right - left;
            h = top - bottom;
            minX = left;
            minY = bottom;
            if (measureOnly)
            {
                return;
            }
            if ((long)w * h > dest.Length)
            {
                Panic.Invariant(PanicCodes.AtlasInsertFailedAfterEviction, "Dest span too small for colour glyph outline bitmap");
            }

            var union = dest.Slice(0, w * h);
            union.Clear();
            foreach (var part in parts)
            {
                if (part.W <= 0)
                {
                    continue;
                }
                for (int row = 0; row < part.H; row++)
                {
                    var source = part.Coverage.AsSpan(row * part.W, part.W);
                    var target = union.Slice((part.MinY - bottom + row) * w + (part.MinX - left), part.W);
                    for (int column = 0; column < source.Length; column++)
                    {
                        target[column] = Math.Max(target[column], source[column]);
                    }
                }
            }
        }
        finally
        {
            foreach (var part in parts)
            {
                if (part.Coverage is not null)
                {
                    ArrayPool<byte>.Shared.Return(part.Coverage);
                }
            }
        }
    }

    /// <summary>How far an outline of <paramref name="outlineWidth"/> pixels reaches past the glyph's bitmap.</summary>
    private static int OutlinePadding(float outlineWidth) => (int)MathF.Ceiling(outlineWidth);

    /// <summary>
    /// Dilates A8 coverage by <paramref name="radius"/> pixels with a round brush: each output pixel
    /// takes the strongest source pixel within reach, faded over the last pixel so the edge is
    /// anti-aliased. Distances run centre to centre, so the reach is the radius plus a pixel: a
    /// solid source pixel's edge lies half a pixel from its centre, and a destination pixel is half
    /// covered when the grown edge passes through its centre. The output is padded by
    /// <see cref="OutlinePadding"/> on every side.
    /// </summary>
    internal static void GrowCoverage(ReadOnlySpan<byte> source, int sourceW, int sourceH, float radius, Span<byte> dest, out int destW, out int destH)
    {
        int pad = OutlinePadding(radius);
        destW = sourceW + 2 * pad;
        destH = sourceH + 2 * pad;
        if ((long)destW * destH > dest.Length)
        {
            Panic.Invariant(PanicCodes.AtlasInsertFailedAfterEviction, "Dest span too small for glyph outline bitmap");
        }

        float reach = radius + 1f;
        for (int y = 0; y < destH; y++)
        {
            for (int x = 0; x < destW; x++)
            {
                float strongest = 0f;
                for (int dy = -pad; dy <= pad; dy++)
                {
                    int sy = y - pad + dy;
                    if ((uint)sy >= (uint)sourceH)
                    {
                        continue;
                    }
                    for (int dx = -pad; dx <= pad; dx++)
                    {
                        int sx = x - pad + dx;
                        if ((uint)sx >= (uint)sourceW)
                        {
                            continue;
                        }
                        byte coverage = source[sy * sourceW + sx];
                        if (coverage == 0)
                        {
                            continue;
                        }
                        float edge = Math.Clamp(reach - MathF.Sqrt(dx * dx + dy * dy), 0f, 1f);
                        strongest = MathF.Max(strongest, coverage * edge);
                    }
                }
                dest[y * destW + x] = (byte)MathF.Round(strongest);
            }
        }
    }

    /// <summary>Etch's own outline rasterizer, for faces loaded without FreeType.</summary>
    private static void RasterizeOwnOutline(
        FontFace face,
        ushort glyphId,
        float subpixelX,
        Span<byte> dest,
        out int w,
        out int h,
        out int minX,
        out int minY)
    {

        var builder = BezPathBuilder.Begin(64);
        var pathOpt = GlyphOutlineBuilder.Build(face, glyphId, builder);
        builder.Dispose();

        if (!pathOpt.HasValue)
        {
            w = 0;
            h = 0;
            minX = 0;
            minY = 0;
            return;
        }
        var path = pathOpt.Value;

        if (path.IsEmpty)
        {
            w = 0;
            h = 0;
            minX = 0;
            minY = 0;
            return;
        }

        double translateX = subpixelX;
        float pixelScale = face.PointSize * 96.0f / (72.0f * face.UnitsPerEm);

        ComputeDimensions(path, pixelScale, out w, out h, out minX, out minY);

        if (w <= 0 || h <= 0)
        {
            w = 0; h = 0; minX = 0; minY = 0;
            return;
        }

        // Subpixel shift expands the right edge by up to 0.75 pixels.
        // Add exactly 1 pixel of padding to capture the shifted edge without
        // creating extra columns with partial coverage that look like artifacts.
        int rasterW = w;
        if (translateX > 0)
        {
            rasterW = w + 1;
        }

        if ((ulong)rasterW * (uint)h > (ulong)dest.Length)
        {
            Panic.Invariant(PanicCodes.AtlasInsertFailedAfterEviction, "Dest span too small for glyph bitmap");
        }

        // 4x supersampling: render at quadruple resolution, then downsample
        const int ss = 4;
        int ssW = rasterW * ss;
        int ssH = h * ss;
        int ssMinX = minX * ss;
        int ssMinY = minY * ss;
        int ssBufSize = ssW * ssH;

        if (ssW <= 256 && ssH <= 256 && ssBufSize <= 4096)
        {
            // Small glyph: use stackalloc
            Span<byte> ssBuf = stackalloc byte[ssBufSize];
            RasterizeImpl(path, pixelScale * ss, translateX * ss, ssBuf, ssW, ssH, ssMinX, ssMinY);
            Downsample4x(ssBuf, ssW, ssH, dest, rasterW, h);
        }
        else if (ssW <= 1024 && ssH <= 1024)
        {
            // Larger glyph: use ArrayPool to avoid stack overflow
            byte[]? ssRented = ArrayPool<byte>.Shared.Rent(ssBufSize);
            try
            {
                Span<byte> ssBuf = ssRented.AsSpan(0, ssBufSize);
                RasterizeImpl(path, pixelScale * ss, translateX * ss, ssBuf, ssW, ssH, ssMinX, ssMinY);
                Downsample4x(ssBuf, ssW, ssH, dest, rasterW, h);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(ssRented);
            }
        }
        else
        {
            // Very large glyph: fall back to 1x
            RasterizeImpl(path, pixelScale, translateX, dest, rasterW, h, minX, minY);
        }

        w = rasterW;
    }

    private static void Downsample4x(Span<byte> src, int srcW, int srcH, Span<byte> dst, int dstW, int dstH)
    {
        for (int y = 0; y < dstH; y++)
        {
            for (int x = 0; x < dstW; x++)
            {
                int sy = y * 4;
                int sx = x * 4;
                int sum = 0;
                for (int dy = 0; dy < 4; dy++)
                {
                    int row = (sy + dy) * srcW;
                    for (int dx = 0; dx < 4; dx++)
                    {
                        sum += src[row + (sx + dx)];
                    }
                }
                dst[y * dstW + x] = (byte)((sum + 8) / 16);
            }
        }
    }

    private static void ComputeDimensions(BezPath path, float pixelScale, out int w, out int h, out int minX, out int minY)
    {
        double edgeMinY = double.MaxValue, edgeMaxY = double.MinValue;
        double edgeMinX = double.MaxValue, edgeMaxX = double.MinValue;
        foreach (var seg in path.Iterate())
        {
            if (seg.Verb == PathVerb.LineTo || seg.Verb == PathVerb.QuadTo || seg.Verb == PathVerb.CubicTo)
            {
                double sx = seg.Start.X * pixelScale;
                double sy = seg.Start.Y * pixelScale;
                double ex = seg.End.X * pixelScale;
                double ey = seg.End.Y * pixelScale;
                if (sx < edgeMinX) edgeMinX = sx;
                if (ex < edgeMinX) edgeMinX = ex;
                if (sx > edgeMaxX) edgeMaxX = sx;
                if (ex > edgeMaxX) edgeMaxX = ex;
                if (sy < edgeMinY) edgeMinY = sy;
                if (ey < edgeMinY) edgeMinY = ey;
                if (sy > edgeMaxY) edgeMaxY = sy;
                if (ey > edgeMaxY) edgeMaxY = ey;

                if (seg.Verb == PathVerb.QuadTo)
                {
                    double cx = seg.Control0.X * pixelScale;
                    double cy = seg.Control0.Y * pixelScale;
                    if (cx < edgeMinX) edgeMinX = cx;
                    if (cx > edgeMaxX) edgeMaxX = cx;
                    if (cy < edgeMinY) edgeMinY = cy;
                    if (cy > edgeMaxY) edgeMaxY = cy;
                }
                else if (seg.Verb == PathVerb.CubicTo)
                {
                    double c0x = seg.Control0.X * pixelScale;
                    double c0y = seg.Control0.Y * pixelScale;
                    double c1x = seg.Control1.X * pixelScale;
                    double c1y = seg.Control1.Y * pixelScale;
                    if (c0x < edgeMinX) edgeMinX = c0x;
                    if (c0x > edgeMaxX) edgeMaxX = c0x;
                    if (c0y < edgeMinY) edgeMinY = c0y;
                    if (c0y > edgeMaxY) edgeMaxY = c0y;
                    if (c1x < edgeMinX) edgeMinX = c1x;
                    if (c1x > edgeMaxX) edgeMaxX = c1x;
                    if (c1y < edgeMinY) edgeMinY = c1y;
                    if (c1y > edgeMaxY) edgeMaxY = c1y;
                }
            }
        }

        minX = (int)Math.Floor(edgeMinX);
        int maxX = (int)Math.Ceiling(edgeMaxX);
        minY = (int)Math.Floor(edgeMinY);
        int maxY = (int)Math.Ceiling(edgeMaxY);

        w = maxX - minX;
        h = maxY - minY;
    }

    private static void RasterizeImpl(
        BezPath path,
        float pixelScale,
        double translateX,
        Span<byte> dest,
        int w,
        int h,
        int minX,
        int minY)
    {
        Span<(Point Start, Point End)> edges = stackalloc (Point, Point)[4096];
        int edgeCount = 0;

        Point current = new Point(0, 0);
        Point pathStart = current;

        foreach (var seg in path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    current = seg.End;
                    pathStart = current;
                    break;
                case PathVerb.LineTo:
                    {
                        var start = new Point((float)(seg.Start.X * pixelScale + translateX), (float)(seg.Start.Y * pixelScale));
                        var end = new Point((float)(seg.End.X * pixelScale + translateX), (float)(seg.End.Y * pixelScale));
                        if (edgeCount < edges.Length)
                            edges[edgeCount++] = (start, end);
                        current = seg.End;
                    }
                    break;
                case PathVerb.QuadTo:
                    {
                        QuadBez q = new QuadBez(
                            new Point((float)(seg.Start.X * pixelScale + translateX), (float)(seg.Start.Y * pixelScale)),
                            new Point((float)(seg.Control0.X * pixelScale + translateX), (float)(seg.Control0.Y * pixelScale)),
                            new Point((float)(seg.End.X * pixelScale + translateX), (float)(seg.End.Y * pixelScale)));
                        FlattenQuadBezEdges(q, edges, ref edgeCount);
                        current = seg.End;
                    }
                    break;
                case PathVerb.CubicTo:
                    {
                        CubicBez c = new CubicBez(
                            new Point((float)(seg.Start.X * pixelScale + translateX), (float)(seg.Start.Y * pixelScale)),
                            new Point((float)(seg.Control0.X * pixelScale + translateX), (float)(seg.Control0.Y * pixelScale)),
                            new Point((float)(seg.Control1.X * pixelScale + translateX), (float)(seg.Control1.Y * pixelScale)),
                            new Point((float)(seg.End.X * pixelScale + translateX), (float)(seg.End.Y * pixelScale)));
                        FlattenCubicBezEdges(c, edges, ref edgeCount);
                        current = seg.End;
                    }
                    break;
                case PathVerb.Close:
                    {
                        if (current != pathStart)
                        {
                            var start = new Point((float)(current.X * pixelScale + translateX), (float)(current.Y * pixelScale));
                            var end = new Point((float)(pathStart.X * pixelScale + translateX), (float)(pathStart.Y * pixelScale));
                            if (edgeCount < edges.Length)
                                edges[edgeCount++] = (start, end);
                        }
                        current = pathStart;
                    }
                    break;
            }
        }

        Span<double> xs = stackalloc double[4096];

        for (int row = 0; row < h; row++)
        {
            double rowY = minY + row + 0.5;
            int count = 0;

            for (int i = 0; i < edgeCount; i++)
            {
                var (p0, p1) = edges[i];
                double y0 = p0.Y;
                double y1 = p1.Y;

                if (Math.Abs(y1 - y0) < 1e-10)
                    continue;

                if (y0 > y1)
                {
                    (y0, y1) = (y1, y0);
                    (p0, p1) = (p1, p0);
                }

                if (rowY < y0 || rowY >= y1)
                    continue;

                double x = p0.X + (rowY - y0) * (p1.X - p0.X) / (y1 - y0);
                xs[count++] = x;
            }

            if (count == 0)
            {
                for (int col = 0; col < w; col++)
                    dest[row * w + col] = 0;
                continue;
            }

            for (int i = 1; i < count; i++)
            {
                double x = xs[i];
                int j = i - 1;
                while (j >= 0 && xs[j] > x)
                {
                    xs[j + 1] = xs[j];
                    j--;
                }
                xs[j + 1] = x;
            }

            for (int col = 0; col < w; col++)
            {
                double colMinX = minX + col;
                double colMaxX = colMinX + 1;
                double overlap = 0.0;

                if (count >= 2)
                {
                    for (int i = 0; i < count - 1; i += 2)
                    {
                        double x0 = xs[i];
                        double x1 = xs[i + 1];

                        if (x1 <= colMinX || x0 >= colMaxX)
                            continue;

                        double left = x0 < colMinX ? colMinX : x0;
                        double right = x1 > colMaxX ? colMaxX : x1;

                        if (right > left)
                            overlap += right - left;
                    }
                }

                int cov = (int)(overlap * 255.0 + 0.5);
                if (cov > 255) cov = 255;
                if (cov < 0) cov = 0;
                dest[row * w + col] = (byte)cov;
            }
        }
    }

    private const int MaxFlattenDepth = 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenQuadBezEdges(QuadBez q, Span<(Point, Point)> edges, ref int count, int depth = 0)
    {
        if (depth >= MaxFlattenDepth || QuadFlatEnough(q))
        {
            if (count < edges.Length)
                edges[count++] = (q.P0, q.P2);
        }
        else
        {
            var (l, r) = q.Subdivide(0.5);
            FlattenQuadBezEdges(l, edges, ref count, depth + 1);
            FlattenQuadBezEdges(r, edges, ref count, depth + 1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FlattenCubicBezEdges(CubicBez c, Span<(Point, Point)> edges, ref int count, int depth = 0)
    {
        if (depth >= MaxFlattenDepth || CubicFlatEnough(c))
        {
            if (count < edges.Length)
                edges[count++] = (c.P0, c.P3);
        }
        else
        {
            var (l, r) = c.Subdivide(0.5);
            FlattenCubicBezEdges(l, edges, ref count, depth + 1);
            FlattenCubicBezEdges(r, edges, ref count, depth + 1);
        }
    }

    private const double MinCurveArea = 0.0001;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool QuadFlatEnough(QuadBez q)
    {
        double dx = q.P2.X - q.P0.X;
        double dy = q.P2.Y - q.P0.Y;
        double d = Math.Sqrt(dx * dx + dy * dy);
        double area = Math.Abs(Cross(q.P1 - q.P0, q.P2 - q.P0));

        // If the curve is too small to be visible (sub-pixel area),
        // treat it as flat regardless of relative deviation.
        if (area < MinCurveArea)
            return true;

        double deviation = area / (d + 1e-10);
        return deviation <= 0.05;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CubicFlatEnough(CubicBez c)
    {
        double dx = c.P3.X - c.P0.X;
        double dy = c.P3.Y - c.P0.Y;
        double d = Math.Sqrt(dx * dx + dy * dy);
        double area1 = Math.Abs(Cross(c.P1 - c.P0, c.P3 - c.P0));
        double area2 = Math.Abs(Cross(c.P2 - c.P0, c.P3 - c.P0));

        // If the curve is too small to be visible, treat it as flat.
        if (area1 < MinCurveArea && area2 < MinCurveArea)
            return true;

        double d1 = area1 / (d + 1e-10);
        double d2 = area2 / (d + 1e-10);
        return Math.Max(d1, d2) <= 0.05;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>
    /// Renders a COLR color glyph to an RGBA buffer.
    /// Returns true if the glyph was successfully rendered as a color glyph.
    /// Uses FreeType for measurement and rendering when available to ensure
    /// correct results for composite glyphs and consistent coordinate systems.
    /// </summary>
    public static bool RasterizeColorGlyph(
        FontFace face,
        ushort glyphId,
        Span<byte> rgbaDest,
        out int w,
        out int h,
        out int minX,
        out int minY)
    {
        w = 0; h = 0; minX = 0; minY = 0;

        if (!GlyphOutlineBuilder.TryGetColorLayers(face, glyphId, out var layers, out var palette))
        {
            return false;
        }

        bool useFreeType = face.FreeTypeFace != 0;

        // Compute overall bounds by measuring the base glyph and each layer
        // using a consistent coordinate system. FreeType provides pixel-perfect
        // bounds for composite glyphs that our outline builder may mishandle.
        int overallMinX = int.MaxValue;
        int overallMinY = int.MaxValue;
        int overallMaxX = int.MinValue;
        int overallMaxY = int.MinValue;

        void ExpandBounds(int gMinX, int gMinY, int gW, int gH)
        {
            int gMaxX = gMinX + gW;
            int gMaxY = gMinY + gH;
            if (gMinX < overallMinX) overallMinX = gMinX;
            if (gMinY < overallMinY) overallMinY = gMinY;
            if (gMaxX > overallMaxX) overallMaxX = gMaxX;
            if (gMaxY > overallMaxY) overallMaxY = gMaxY;
        }

        // Base glyph bounds
        if (useFreeType)
        {
            FreeTypeGlyphRasterizer.Measure(face.FreeTypeFace, glyphId, face.Hinting, out int bw, out int bh);
            if (bw > 0 && bh > 0)
            {
                byte[] buf = ArrayPool<byte>.Shared.Rent(bw * bh);
                try
                {
                    FreeTypeGlyphRasterizer.Rasterize(face.FreeTypeFace, glyphId, 0, face.Hinting,
                        buf.AsSpan(0, bw * bh), out bw, out bh, out int bMinX, out int bMinY);
                    if (bw > 0 && bh > 0)
                    {
                        ExpandBounds(bMinX, bMinY, bw, bh);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
        }
        else
        {
            Measure(face, glyphId, out int bw, out int bh);
            if (bw > 0 && bh > 0)
            {
                var baseBuilder = BezPathBuilder.Begin(64);
                var basePathOpt = GlyphOutlineBuilder.Build(face, glyphId, baseBuilder);
                baseBuilder.Dispose();
                if (basePathOpt.HasValue && !basePathOpt.Value.IsEmpty)
                {
                    float ps = face.PointSize * 96.0f / (72.0f * face.UnitsPerEm);
                    ComputeDimensions(basePathOpt.Value, ps, out _, out _, out int bminX, out int bminY);
                    ExpandBounds(bminX, bminY, bw, bh);
                }
            }
        }

        foreach (var layer in layers)
        {
            if (useFreeType)
            {
                FreeTypeGlyphRasterizer.Measure(face.FreeTypeFace, layer.GlyphId, face.Hinting, out int lw, out int lh);
                if (lw > 0 && lh > 0)
                {
                    byte[] buf = ArrayPool<byte>.Shared.Rent(lw * lh);
                    try
                    {
                        FreeTypeGlyphRasterizer.Rasterize(face.FreeTypeFace, layer.GlyphId, 0, face.Hinting,
                            buf.AsSpan(0, lw * lh), out lw, out lh, out int lMinX, out int lMinY);
                        if (lw > 0 && lh > 0)
                        {
                            ExpandBounds(lMinX, lMinY, lw, lh);
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buf);
                    }
                }
            }
            else
            {
                Measure(face, layer.GlyphId, out int lw, out int lh);
                if (lw > 0 && lh > 0)
                {
                    var builder = BezPathBuilder.Begin(64);
                    var pathOpt = GlyphOutlineBuilder.Build(face, layer.GlyphId, builder);
                    builder.Dispose();
                    if (pathOpt.HasValue && !pathOpt.Value.IsEmpty)
                    {
                        float pixelScale = face.PointSize * 96.0f / (72.0f * face.UnitsPerEm);
                        ComputeDimensions(pathOpt.Value, pixelScale, out _, out _, out int lminX, out int lminY);
                        ExpandBounds(lminX, lminY, lw, lh);
                    }
                }
            }
        }

        if (overallMinX == int.MaxValue)
        {
            return false;
        }

        w = overallMaxX - overallMinX;
        h = overallMaxY - overallMinY;
        minX = overallMinX;
        minY = overallMinY;

        if (w <= 0 || h <= 0)
        {
            return false;
        }

        if (rgbaDest.Length < w * h * 4)
        {
            return false;
        }

        // Clear to transparent
        rgbaDest.Slice(0, w * h * 4).Clear();

        // Render the base glyph as background using palette[0] (typical face-body
        // color). Some COLR fonts keep the base shape out of the layer list.
        if (palette.Length > 0)
        {
            if (useFreeType)
            {
                FreeTypeGlyphRasterizer.Measure(face.FreeTypeFace, glyphId, face.Hinting, out int bw, out int bh);
                if (bw > 0 && bh > 0)
                {
                    byte[] baseA8 = ArrayPool<byte>.Shared.Rent(bw * bh);
                    try
                    {
                        FreeTypeGlyphRasterizer.Rasterize(face.FreeTypeFace, glyphId, 0, face.Hinting,
                            baseA8.AsSpan(0, bw * bh), out bw, out bh, out int bminX, out int bminY);
                        if (bw > 0 && bh > 0)
                        {
                            int offX = bminX - overallMinX;
                            int offY = bminY - overallMinY;
                            var baseColor = palette[0];
                            for (int y = 0; y < bh; y++)
                            {
                                int srcRow = y * bw;
                                int dstRow = (y + offY) * w;
                                for (int x = 0; x < bw; x++)
                                {
                                    byte alpha = baseA8[srcRow + x];
                                    if (alpha == 0) continue;
                                    int dstIdx = (dstRow + (x + offX)) * 4;
                                    rgbaDest[dstIdx + 0] = baseColor.R;
                                    rgbaDest[dstIdx + 1] = baseColor.G;
                                    rgbaDest[dstIdx + 2] = baseColor.B;
                                    rgbaDest[dstIdx + 3] = alpha;
                                }
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(baseA8);
                    }
                }
            }
            else
            {
                Measure(face, glyphId, out int bw, out int bh);
                if (bw > 0 && bh > 0)
                {
                    var baseBuilder = BezPathBuilder.Begin(64);
                    var basePathOpt = GlyphOutlineBuilder.Build(face, glyphId, baseBuilder);
                    baseBuilder.Dispose();
                    if (basePathOpt.HasValue && !basePathOpt.Value.IsEmpty)
                    {
                        float ps = face.PointSize * 96.0f / (72.0f * face.UnitsPerEm);
                        ComputeDimensions(basePathOpt.Value, ps, out _, out _, out int bminX, out int bminY);
                        byte[] baseA8 = new byte[bw * bh];
                        RasterizeImpl(basePathOpt.Value, ps, 0, baseA8.AsSpan(), bw, bh, bminX, bminY);
                        int offX = bminX - overallMinX;
                        int offY = bminY - overallMinY;
                        var baseColor = palette[0];
                        for (int y = 0; y < bh; y++)
                        {
                            int srcRow = y * bw;
                            int dstRow = (y + offY) * w;
                            for (int x = 0; x < bw; x++)
                            {
                                byte alpha = baseA8[srcRow + x];
                                if (alpha == 0) continue;
                                int dstIdx = (dstRow + (x + offX)) * 4;
                                rgbaDest[dstIdx + 0] = baseColor.R;
                                rgbaDest[dstIdx + 1] = baseColor.G;
                                rgbaDest[dstIdx + 2] = baseColor.B;
                                rgbaDest[dstIdx + 3] = alpha;
                            }
                        }
                    }
                }
            }
        }

        // Render each layer on top of the base glyph
        foreach (var layer in layers)
        {
            if (layer.PaletteIndex >= palette.Length)
            {
                continue;
            }

            var color = palette[layer.PaletteIndex];

            if (useFreeType)
            {
                FreeTypeGlyphRasterizer.Measure(face.FreeTypeFace, layer.GlyphId, face.Hinting, out int lw, out int lh);
                if (lw <= 0 || lh <= 0)
                {
                    continue;
                }

                byte[] a8buf = ArrayPool<byte>.Shared.Rent(lw * lh);
                try
                {
                    FreeTypeGlyphRasterizer.Rasterize(face.FreeTypeFace, layer.GlyphId, 0, face.Hinting,
                        a8buf.AsSpan(0, lw * lh), out lw, out lh, out int lminX, out int lminY);
                    if (lw <= 0 || lh <= 0)
                    {
                        continue;
                    }

                    int offsetX = lminX - overallMinX;
                    int offsetY = lminY - overallMinY;

                    for (int y = 0; y < lh; y++)
                    {
                        int srcRow = y * lw;
                        int dstRow = (y + offsetY) * w;
                        for (int x = 0; x < lw; x++)
                        {
                            byte alpha = a8buf[srcRow + x];
                            if (alpha == 0)
                            {
                                continue;
                            }

                            int dstIdx = (dstRow + (x + offsetX)) * 4;
                            byte dstA = rgbaDest[dstIdx + 3];

                            if (dstA == 0)
                            {
                                rgbaDest[dstIdx + 0] = color.R;
                                rgbaDest[dstIdx + 1] = color.G;
                                rgbaDest[dstIdx + 2] = color.B;
                                rgbaDest[dstIdx + 3] = alpha;
                            }
                            else
                            {
                                float sa = alpha / 255.0f;
                                float da = dstA / 255.0f;
                                float invSa = 1.0f - sa;
                                float outA = sa + da * invSa;
                                if (outA > 0)
                                {
                                    rgbaDest[dstIdx + 0] = (byte)((color.R * sa + rgbaDest[dstIdx + 0] * da * invSa) / outA);
                                    rgbaDest[dstIdx + 1] = (byte)((color.G * sa + rgbaDest[dstIdx + 1] * da * invSa) / outA);
                                    rgbaDest[dstIdx + 2] = (byte)((color.B * sa + rgbaDest[dstIdx + 2] * da * invSa) / outA);
                                    rgbaDest[dstIdx + 3] = (byte)(outA * 255);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(a8buf);
                }
            }
            else
            {
                Measure(face, layer.GlyphId, out int lw, out int lh);
                if (lw <= 0 || lh <= 0)
                {
                    continue;
                }

                var builder = BezPathBuilder.Begin(64);
                var pathOpt = GlyphOutlineBuilder.Build(face, layer.GlyphId, builder);
                builder.Dispose();
                if (!pathOpt.HasValue || pathOpt.Value.IsEmpty)
                {
                    continue;
                }

                float pixelScale = face.PointSize * 96.0f / (72.0f * face.UnitsPerEm);
                ComputeDimensions(pathOpt.Value, pixelScale, out _, out _, out int lminX, out int lminY);

                byte[] a8buf = new byte[lw * lh];
                RasterizeImpl(pathOpt.Value, pixelScale, 0, a8buf.AsSpan(), lw, lh, lminX, lminY);

                int offsetX = lminX - overallMinX;
                int offsetY = lminY - overallMinY;

                for (int y = 0; y < lh; y++)
                {
                    int srcRow = y * lw;
                    int dstRow = (y + offsetY) * w;
                    for (int x = 0; x < lw; x++)
                    {
                        byte alpha = a8buf[srcRow + x];
                        if (alpha == 0)
                        {
                            continue;
                        }

                        int dstIdx = (dstRow + (x + offsetX)) * 4;
                        byte dstA = rgbaDest[dstIdx + 3];

                        if (dstA == 0)
                        {
                            rgbaDest[dstIdx + 0] = color.R;
                            rgbaDest[dstIdx + 1] = color.G;
                            rgbaDest[dstIdx + 2] = color.B;
                            rgbaDest[dstIdx + 3] = alpha;
                        }
                        else
                        {
                            float sa = alpha / 255.0f;
                            float da = dstA / 255.0f;
                            float invSa = 1.0f - sa;
                            float outA = sa + da * invSa;
                            if (outA > 0)
                            {
                                rgbaDest[dstIdx + 0] = (byte)((color.R * sa + rgbaDest[dstIdx + 0] * da * invSa) / outA);
                                rgbaDest[dstIdx + 1] = (byte)((color.G * sa + rgbaDest[dstIdx + 1] * da * invSa) / outA);
                                rgbaDest[dstIdx + 2] = (byte)((color.B * sa + rgbaDest[dstIdx + 2] * da * invSa) / outA);
                                rgbaDest[dstIdx + 3] = (byte)(outA * 255);
                            }
                        }
                    }
                }
            }
        }

        return true;
    }
}
