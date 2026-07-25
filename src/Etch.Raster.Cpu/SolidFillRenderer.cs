using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Scene;
using Etch.Tiling;
using Etch.Tiling.Classify;

namespace Etch.Raster.Cpu;

public static class SolidFillRenderer
{
    [SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static void Render<TTile>(
        SceneBuffer scene,
        ClassifiedScene classified,
        TileGrid<TTile> grid,
        Framebuffer target)
        where TTile : struct, ITileSize
    {
        Render(scene, classified, grid, target, ClipMask.Empty);
    }

    [SuppressMessage("CodeAnalysis", "CA1062:Validate arguments before using them", Justification = "Project uses Panic system for validation")]
    public static void Render<TTile>(
        SceneBuffer scene,
        ClassifiedScene classified,
        TileGrid<TTile> grid,
        Framebuffer target,
        ClipMask clipMask)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (scene is null)
            Panic.ArgumentNull(nameof(scene));
#pragma warning restore CA1062

        if (target.Width != grid.SurfaceWidth || target.Height != grid.SurfaceHeight)
            Panic.Invariant(PanicCodes.InvariantViolation, "Framebuffer dimensions must match grid dimensions");

        var commands = scene.Commands;

        for (int tileIndex = 0; tileIndex < classified.TileCount; tileIndex++)
        {
            var entries = classified.Entries(tileIndex);
            if (entries.Length == 0)
                continue;

            int tileX = tileIndex % grid.TileCountX;
            int tileY = tileIndex / grid.TileCountX;
            var tileBounds = grid.TileBounds(tileX, tileY);

            foreach (ref readonly var entry in entries)
            {
                switch (entry.Kind)
                {
                    case ClassificationKind.FillRect:
                        RenderFillRect(scene, commands, entry.CommandOrder, tileBounds, target, clipMask);
                        break;

                    case ClassificationKind.FillPath:
                        RenderFillPath(scene, commands, entry.CommandOrder, tileBounds, target, clipMask);
                        break;

                    case ClassificationKind.StrokePath:
                        Panic.NotImplemented("CPU-003 supports only FillRect; stroke fills land in CPU-004.");
                        break;

                    case ClassificationKind.DrawImage:
                        Panic.NotImplemented("CPU-003 supports only FillRect; image draws land in CPU-005.");
                        break;

                    case ClassificationKind.DrawGlyphRun:
                        Panic.NotImplemented("CPU-003 supports only FillRect; glyph runs land in CPU-007.");
                        break;
                }
            }
        }
    }

    private static void RenderFillRect(
        SceneBuffer scene,
        ReadOnlySpan<SceneCommand> commands,
        int commandOrder,
        Rect tileBounds,
        Framebuffer target,
        ClipMask clipMask)
    {
        ref readonly var cmd = ref commands[commandOrder];
        if (cmd.Op != SceneOpcode.FillRect)
            return;

        var rect = scene.GetRect(cmd.FillRect.RectId);
        if (rect.IsEmpty)
            return;

        var transform = scene.GetTransform(cmd.FillRect.TransformId);
        var paint = scene.GetPaint(cmd.FillRect.PaintId);

        if (paint.Kind != PaintKind.Solid)
            Panic.NotImplemented("CPU-003 supports only solid paints; gradient/image paints land in CPU-010.");

        var deviceRect = rect.Transform(transform);

        var intersection = tileBounds.Intersect(deviceRect);
        if (intersection.IsEmpty)
            return;

        int minX = (int)Math.Ceiling(Math.Max(0, intersection.MinX));
        int maxX = (int)Math.Ceiling(Math.Min(target.Width, intersection.MaxX)) - 1;
        int minY = (int)Math.Ceiling(Math.Max(0, intersection.MinY));
        int maxY = (int)Math.Ceiling(Math.Min(target.Height, intersection.MaxY)) - 1;

        if (minX > maxX || minY > maxY)
            return;

        uint argb = paint.Color;
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);

        float rLin = Srgb.DecodeChannelScalar(r);
        float gLin = Srgb.DecodeChannelScalar(g);
        float bLin = Srgb.DecodeChannelScalar(b);
        float aLin = a * (1.0f / 255.0f);

        Rgba16f color = Rgba16f.From(rLin, gLin, bLin, aLin);

        bool hasClip = clipMask.Coverage.Width > 0;

        for (int y = minY; y <= maxY; y++)
        {
            var row = target.RowSpan(y);
            if (!hasClip)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    row[x] = color;
                }
            }
            else
            {
                var clipRow = clipMask.Coverage.RowSpan(y);
                for (int x = minX; x <= maxX; x++)
                {
                    float clipAlpha = (float)clipRow[x].R;
                    if (clipAlpha <= 0)
                        continue;
                    row[x] = Rgba16f.From(rLin, gLin, bLin, aLin * clipAlpha);
                }
            }
        }
    }

    private static void RenderFillPath(
        SceneBuffer scene,
        ReadOnlySpan<SceneCommand> commands,
        int commandOrder,
        Rect tileBounds,
        Framebuffer target,
        ClipMask clipMask)
    {
        ref readonly var cmd = ref commands[commandOrder];
        if (cmd.Op != SceneOpcode.FillPath)
            return;

        var paint = scene.GetPaint(cmd.FillPath.PaintId);
        if (paint.Kind != PaintKind.Solid)
            Panic.NotImplemented("CPU-004 supports only solid paints; gradient/image paints land in CPU-010.");

        if (!scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
            return;

        var transform = scene.GetTransform(cmd.FillPath.TransformId);
        var path = pathData.Path;

        const int MaxVertices = 4096;
        var vertices = ArrayPool<Point>.Shared.Rent(MaxVertices);
        var edgeStartIndices = ArrayPool<int>.Shared.Rent(MaxVertices / 2);
        int vertexCount = 0;
        int edgeCount = 0;

        Point lastPoint = default;
        bool hasMoveTo = false;
        int pathStartVertex = 0;

        foreach (PathSegment seg in path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    if (vertexCount < MaxVertices)
                        vertices[vertexCount] = transform * seg.End;
                    lastPoint = vertices[vertexCount];
                    vertexCount++;
                    hasMoveTo = true;
                    pathStartVertex = vertexCount - 1;
                    break;

                case PathVerb.LineTo:
                    {
                        Point p0 = hasMoveTo ? lastPoint : default;
                        Point p1 = transform * seg.End;
                        if (vertexCount < MaxVertices)
                            vertices[vertexCount] = p1;
                        if (edgeCount < MaxVertices / 2)
                            edgeStartIndices[edgeCount++] = vertexCount - 1;
                        lastPoint = p1;
                        vertexCount++;
                        hasMoveTo = true;
                    }
                    break;

                case PathVerb.QuadTo:
                    {
                        Point p0 = hasMoveTo ? lastPoint : default;
                        Point p1 = transform * seg.Control0;
                        Point p2 = transform * seg.End;
                        int startIdx = vertexCount;
                        FlattenQuadIntoBuffer(p0, p1, p2, vertices, ref vertexCount, MaxVertices);
                        int segEdgeCount = vertexCount - startIdx;
                        for (int i = 0; i < segEdgeCount - 1 && edgeCount < MaxVertices / 2; i++)
                            edgeStartIndices[edgeCount++] = startIdx + i;
                        lastPoint = p2;
                        hasMoveTo = true;
                    }
                    break;

                case PathVerb.CubicTo:
                    {
                        Point p0 = hasMoveTo ? lastPoint : default;
                        Point p1 = transform * seg.Control0;
                        Point p2 = transform * seg.Control1;
                        Point p3 = transform * seg.End;
                        int startIdx = vertexCount;
                        FlattenCubicIntoBuffer(p0, p1, p2, p3, vertices, ref vertexCount, MaxVertices);
                        int segEdgeCount = vertexCount - startIdx;
                        for (int i = 0; i < segEdgeCount - 1 && edgeCount < MaxVertices / 2; i++)
                            edgeStartIndices[edgeCount++] = startIdx + i;
                        lastPoint = p3;
                        hasMoveTo = true;
                    }
                    break;

                case PathVerb.Close:
                    if (edgeCount < MaxVertices / 2 && edgeCount > 0)
                    {
                        edgeStartIndices[edgeCount++] = vertexCount - 1;
                    }
                    hasMoveTo = false;
                    break;
            }
        }

        if (edgeCount < 3)
        {
            ArrayPool<Point>.Shared.Return(vertices);
            ArrayPool<int>.Shared.Return(edgeStartIndices);
            return;
        }

        int minY = (int)Math.Ceiling(Math.Max(0, tileBounds.MinY));
        int maxY = (int)Math.Floor(Math.Min(target.Height - 1, tileBounds.MaxY - 1));
        int minX = (int)Math.Ceiling(Math.Max(0, tileBounds.MinX));
        int maxX = (int)Math.Floor(Math.Min(target.Width - 1, tileBounds.MaxX - 1));

        if (minY > maxY || minX > maxX)
        {
            ArrayPool<Point>.Shared.Return(vertices);
            ArrayPool<int>.Shared.Return(edgeStartIndices);
            return;
        }

        uint argb = paint.Color;
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);

        float rLin = Srgb.DecodeChannelScalar(r);
        float gLin = Srgb.DecodeChannelScalar(g);
        float bLin = Srgb.DecodeChannelScalar(b);
        float aLin = a * (1.0f / 255.0f);

        Rgba16f color = Rgba16f.From(rLin, gLin, bLin, aLin);

        bool hasClip = clipMask.Coverage.Width > 0;

        Span<int> xints = stackalloc int[256];

        for (int y = minY; y <= maxY; y++)
        {
            double yd = y + 0.5;
            int count = 0;

            for (int i = 0; i < edgeCount; i++)
            {
                int idx0 = edgeStartIndices[i];
                int idx1 = (i + 1 < edgeCount) ? edgeStartIndices[i + 1] : pathStartVertex;
                if (idx1 >= vertexCount || idx0 >= vertexCount) continue;

                Point p0 = vertices[idx0];
                Point p1 = vertices[idx1];
                double y0 = p0.Y;
                double y1 = p1.Y;

                if (y0 > y1)
                {
                    (y0, y1) = (y1, y0);
                }

                if (yd >= y0 && yd < y1)
                {
                    double x = p0.X + (yd - y0) * (p1.X - p0.X) / (y1 - y0);
                    if (count < 256)
                        xints[count++] = (int)Math.Floor(x);
                }
            }

            for (int i = 1; i < count; i++)
            {
                int x = xints[i];
                int j = i - 1;
                while (j >= 0 && xints[j] > x)
                {
                    xints[j + 1] = xints[j];
                    j--;
                }
                xints[j + 1] = x;
            }

            var row = target.RowSpan(y);
            var clipRow = hasClip ? clipMask.Coverage.RowSpan(y) : Span<Rgba16f>.Empty;

            for (int i = 0; i + 1 < count; i += 2)
            {
                int x0 = xints[i];
                int x1 = xints[i + 1];

                if (x0 > maxX) continue;
                if (x1 < minX) continue;

                int startX = x0 < minX ? minX : x0;
                int endX = x1 > maxX ? maxX : x1;

                if (!hasClip)
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        row[x] = color;
                    }
                }
                else
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        float clipAlpha = (float)clipRow[x].R;
                        if (clipAlpha <= 0)
                            continue;
                        row[x] = Rgba16f.From(rLin, gLin, bLin, aLin * clipAlpha);
                    }
                }
            }
        }

        ArrayPool<Point>.Shared.Return(vertices);
        ArrayPool<int>.Shared.Return(edgeStartIndices);
    }

    private static void FlattenQuadIntoBuffer(Point p0, Point p1, Point p2, Point[] buffer, ref int count, int max)
    {
        if (count < max)
            buffer[count++] = p0;

        const double Tolerance = 0.25;
        Span<(Point a, Point b)> stack = stackalloc (Point, Point)[64];
        int stackTop = 0;
        stack[stackTop++] = (p0, p2);

        Span<Point> quad = stackalloc Point[3];
        quad[0] = p0;
        quad[1] = p1;
        quad[2] = p2;

        while (stackTop > 0 && count < max - 1)
        {
            stackTop--;
            var (a, c) = stack[stackTop];
            quad[0] = a;
            quad[2] = c;

            double d = DistToLine(quad[1], a, c);
            if (d <= Tolerance)
            {
                if (count < max)
                    buffer[count++] = c;
            }
            else
            {
                Point b = quad[1];
                Point leftMid = new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                Point rightMid = new Point((b.X + c.X) * 0.5, (b.Y + c.Y) * 0.5);
                Point mid = new Point((leftMid.X + rightMid.X) * 0.5, (leftMid.Y + rightMid.Y) * 0.5);

                if (stackTop < 62)
                {
                    stack[stackTop++] = (a, mid);
                    stack[stackTop++] = (mid, c);
                }
                else
                {
                    if (count < max)
                        buffer[count++] = mid;
                    if (count < max)
                        buffer[count++] = c;
                }
            }
        }
    }

    private static void FlattenCubicIntoBuffer(Point p0, Point p1, Point p2, Point p3, Point[] buffer, ref int count, int max)
    {
        if (count < max)
            buffer[count++] = p0;

        const double Tolerance = 0.25;
        Span<(Point a, Point d)> stack = stackalloc (Point, Point)[64];
        int stackTop = 0;
        stack[stackTop++] = (p0, p3);

        Span<Point> cubic = stackalloc Point[4];
        cubic[0] = p0;
        cubic[1] = p1;
        cubic[2] = p2;
        cubic[3] = p3;

        while (stackTop > 0 && count < max - 1)
        {
            stackTop--;
            var (a, d) = stack[stackTop];
            cubic[0] = a;
            cubic[3] = d;

            double d1 = DistToLine(cubic[1], a, d);
            double d2 = DistToLine(cubic[2], a, d);
            double dmax = d1 > d2 ? d1 : d2;

            if (dmax <= Tolerance)
            {
                if (count < max)
                    buffer[count++] = d;
            }
            else
            {
                Point b = cubic[1];
                Point c = cubic[2];

                Point leftMid = new Point((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5);
                Point rightMid = new Point((c.X + d.X) * 0.5, (c.Y + d.Y) * 0.5);
                Point midLeft = new Point((leftMid.X + (b.X + c.X) * 0.25) * 0.5, (leftMid.Y + (b.Y + c.Y) * 0.25) * 0.5);
                Point midRight = new Point(((b.X + c.X) * 0.25 + rightMid.X) * 0.5, ((b.Y + c.Y) * 0.25 + rightMid.Y) * 0.5);
                Point mid = new Point((midLeft.X + midRight.X) * 0.5, (midLeft.Y + midRight.Y) * 0.5);

                if (stackTop < 60)
                {
                    stack[stackTop++] = (a, mid);
                    stack[stackTop++] = (mid, d);
                }
                else
                {
                    if (count < max)
                        buffer[count++] = mid;
                    if (count < max)
                        buffer[count++] = d;
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double DistToLine(Point pt, Point lineStart, Point lineEnd)
    {
        Vec2 line = lineEnd - lineStart;
        double lineLenSq = line.X * line.X + line.Y * line.Y;
        if (lineLenSq < 1e-20) return (pt - lineStart).Length;
        Vec2 diff = pt - lineStart;
        double t = Math.Max(0, Math.Min(1, diff.Dot(line) / lineLenSq));
        Point projection = lineStart + line * t;
        return (pt - projection).Length;
    }
}