using System;
using System.Buffers;
using Etch.Geometry;
using Etch.Geometry.Flatten;
using Etch.Scene;
using Etch.Tiling.Classify;

namespace Etch.Tiling.Strips;

public static class StripEmitter
{
    private const int MaxClipDepth = 16;

    public static StripBuffer Emit<TTile>(SceneBuffer scene, ClassifiedScene classified, TileGrid<TTile> grid)
        where TTile : struct, ITileSize
    {
#pragma warning disable CA1062
        if (scene == null)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentNull, "scene must not be null");

        if (classified.AllEntries.Length == 0)
            return new StripBuffer([], [], [], 0);

        var builder = new StripsBuilder();
        builder.Reset(grid.TotalTiles);

        var edges = ArrayPool<(Point, Point)>.Shared.Rent(2048);
        var coverage = new byte[TTile.Width];

        try
        {
            int sceneCursor = 0;
            var xform = Affine.Identity;
            var commands = scene.Commands;
#pragma warning restore CA1062

            Span<Rect> clipStack = stackalloc Rect[MaxClipDepth];
            int clipDepth = 0;

            int numEntries = classified.AllEntries.Length;
            var allEntries = classified.AllEntries;

            for (int i = 0; i < numEntries; i++)
            {
                int targetOrder = allEntries[i].CommandOrder;

                while (sceneCursor < commands.Length && sceneCursor <= targetOrder)
                {
                    ref readonly var cmd = ref commands[sceneCursor];
                    switch (cmd.Op)
                    {
                        case SceneOpcode.SetTransform:
                            xform = scene.GetTransform(cmd.SetTransform.TransformId);
                            break;
                        case SceneOpcode.PushClip:
                            if (clipDepth < MaxClipDepth &&
                                scene.TryGetPath(cmd.PushClip.ClipId, out var pathData))
                            {
                                var clipAabb = pathData.Path.Aabb();
                                if (!clipAabb.IsEmpty)
                                {
                                    var deviceAabb = TransformRect(xform, clipAabb);
                                    if (cmd.PushClip.ClipMode == 0)
                                        clipStack[clipDepth] = deviceAabb;
                                    else
                                        clipStack[clipDepth] = Rect.Empty;
                                }
                                else
                                {
                                    clipStack[clipDepth] = Rect.Empty;
                                }
                                clipDepth++;
                            }
                            break;
                        case SceneOpcode.PopClip:
                            if (clipDepth > 0)
                                clipDepth--;
                            break;
                    }
                    sceneCursor++;
                }

                ref readonly var entry = ref allEntries[i];
                int tileIndex = entry.TileIndex;
                var (tileX, tileY) = grid.TileXY(tileIndex);
                var tileBounds = grid.TileBounds(tileX, tileY);

                if (!TileOverlapsClipStack(tileBounds, clipStack, clipDepth))
                    continue;

                switch (entry.Kind)
                {
                    case ClassificationKind.FillRect:
                        EmitFillRect(scene, entry, xform, tileX, tileY, tileBounds, grid, ref builder);
                        break;
                    case ClassificationKind.FillPath:
                        EmitFillPath(scene, entry, xform, grid, tileX, tileY, edges, coverage, ref builder);
                        break;
                    case ClassificationKind.StrokePath:
                        EmitStrokePath(scene, entry, xform, grid, tileX, tileY, edges, coverage, ref builder);
                        break;
                    case ClassificationKind.DrawImage:
                    case ClassificationKind.DrawGlyphRun:
                        if (TryGetCommandAtOrder(scene, entry.CommandOrder, out var imgCmd))
                        {
                            int paintId = imgCmd.Op == SceneOpcode.DrawImage
                                ? imgCmd.DrawImage.PaintId
                                : imgCmd.DrawGlyphRun.PaintId;
                            EmitFullCoverage<TTile>(tileIndex, paintId, ref builder);
                        }
                        break;
                }
            }
        }
        finally
        {
            ArrayPool<(Point, Point)>.Shared.Return(edges);
        }

        return builder.Finish();
    }

    private static bool TileOverlapsClipStack(Rect tileBounds, ReadOnlySpan<Rect> clipStack, int clipDepth)
    {
        if (clipDepth == 0)
            return true;

        for (int i = 0; i < clipDepth; i++)
        {
            ref readonly var clip = ref clipStack[i];
            if (clip.IsEmpty)
                continue;

            if (!RectsOverlap(tileBounds, clip))
                return false;
        }

        return true;
    }

    private static bool RectsOverlap(Rect a, Rect b)
    {
        if (a.IsEmpty || b.IsEmpty)
            return false;

        return a.MinX < b.MaxX && a.MaxX > b.MinX &&
               a.MinY < b.MaxY && a.MaxY > b.MinY;
    }

    private static void EmitFillRect<TTile>(
        SceneBuffer scene,
        ClassificationEntry entry,
        Affine xform,
        int tileX,
        int tileY,
        Rect tileBounds,
        TileGrid<TTile> grid,
        ref StripsBuilder builder)
        where TTile : struct, ITileSize
    {
        if (!TryGetCommandAtOrder(scene, entry.CommandOrder, out var cmd))
            return;
        if (cmd.Op != SceneOpcode.FillRect)
            return;

        var localXform = xform * scene.GetTransform(cmd.FillRect.TransformId);
        var rect = scene.GetRect(cmd.FillRect.RectId);
        if (rect.IsEmpty)
            return;

        var deviceRect = TransformRect(localXform, rect);
        if (deviceRect.IsEmpty)
            return;

        var clipped = Intersect(deviceRect, tileBounds);
        if (clipped.IsEmpty)
            return;

        int tileMinX = tileX * TTile.Width;
        int tileMinY = tileY * TTile.Height;

        int x0 = (int)Math.Ceiling(clipped.MinX) - tileMinX;
        int x1 = (int)Math.Ceiling(clipped.MaxX) - 1 - tileMinX;
        int y0 = (int)Math.Ceiling(clipped.MinY) - tileMinY;
        int y1 = (int)Math.Ceiling(clipped.MaxY) - 1 - tileMinY;

        if (x0 < 0) x0 = 0;
        if (x1 >= TTile.Width) x1 = TTile.Width - 1;
        if (y0 < 0) y0 = 0;
        if (y1 >= TTile.Height) y1 = TTile.Height - 1;

        if (x0 > x1 || y0 > y1)
            return;

        int numRows = y1 - y0 + 1;
        int numCols = x1 - x0 + 1;
        int totalCoverage = numRows * numCols;

        ushort rowMask = (ushort)(((1 << numRows) - 1) << y0);

        var coverage = new byte[totalCoverage];
        for (int i = 0; i < totalCoverage; i++)
            coverage[i] = 0xFF;

        var strip = new Strip(
            (uint)grid.TileIndex(tileX, tileY),
            rowMask,
            (ushort)x0,
            (ushort)x1,
            (uint)builder.CoverageCount,
            (uint)cmd.FillRect.PaintId);
        builder.AddStrip(strip, coverage);
    }

    private static void EmitFillPath<TTile>(
        SceneBuffer scene,
        ClassificationEntry entry,
        Affine xform,
        TileGrid<TTile> grid,
        int tileX,
        int tileY,
        (Point, Point)[] edges,
        byte[] coverage,
        ref StripsBuilder builder)
        where TTile : struct, ITileSize
    {
        if (!TryGetCommandAtOrder(scene, entry.CommandOrder, out var cmd))
            return;
        if (cmd.Op != SceneOpcode.FillPath)
            return;

        if (!scene.TryGetPath(cmd.FillPath.PathId, out var pathData))
            return;

        var localXform = xform * scene.GetTransform(cmd.FillPath.TransformId);
        IFillStrategy fillStrategy = cmd.FillPath.FillRule == 0
            ? NonZeroFillStrategy.Instance
            : EvenOddFillStrategy.Instance;

        var flatPoints = ArrayPool<Point>.Shared.Rent(65536);
        try
        {
            FlattenSink flatSink = new FlattenSink(flatPoints.AsSpan(), autoflush: true);
            CurveFlattener.BezPath(in pathData.Path, 0.05, ref flatSink);

            var written = flatSink.Written;
            if (written.Length < 2)
                return;

            int edgeCount = 0;
            for (int i = 0; i < written.Length - 1 && edgeCount < edges.Length; i++)
            {
                var start = localXform.Transform(written[i]);
                var end = localXform.Transform(written[i + 1]);

                var clipped = ClipEdgeToTile(start, end, tileX, tileY, TTile.Width, TTile.Height);
                if (clipped.HasValue)
                    edges[edgeCount++] = clipped.Value;
            }

            if (edgeCount == 0)
            {
                // No boundary edges intersect this tile — check if tile center is inside the path
                if (!IsPointInsidePath(written, localXform, tileX, tileY, TTile.Width, TTile.Height))
                    return;

                // Interior tile: full coverage for all rows
                for (int row = 0; row < TTile.Height; row++)
                {
                    Array.Fill(coverage, (byte)255, 0, TTile.Width);
                    EmitRowStrips<TTile>(coverage, grid.TileIndex(tileX, tileY), row, cmd.FillPath.PaintId, ref builder);
                }
                return;
            }

            var tileEdges = edges.AsSpan(0, edgeCount);

            for (int row = 0; row < TTile.Height; row++)
            {
                fillStrategy.ComputeRowCoverage(
                    tileEdges,
                    coverage,
                    tileX,
                    tileY,
                    TTile.Width,
                    row);

                bool rowHasCoverage = false;
                for (int col = 0; col < TTile.Width; col++)
                {
                    if (coverage[col] > 0)
                    {
                        rowHasCoverage = true;
                        break;
                    }
                }

                if (!rowHasCoverage)
                {
                    double rowCenterY = tileY * TTile.Height + row + 0.5;
                    bool anyInside = false;
                    for (int col = 0; col < TTile.Width; col++)
                    {
                        double testX = tileX * TTile.Width + col + 0.5;
                        if (IsPointInsidePathAt(written, localXform, testX, rowCenterY))
                        {
                            anyInside = true;
                            break;
                        }
                    }
                    if (anyInside)
                    {
                        Array.Fill(coverage, (byte)255, 0, TTile.Width);
                    }
                }
                else
                {
                    // Check for unpaired crossings: AnalyticCoverage's heuristic may
                    // overfill toward the tile boundary. Recompute coverage for these
                    // rows using supersampling for accurate fractional coverage.
                    double rowY = tileY * TTile.Height + row + 0.5;
                    int crossingCount = 0;
                    for (int i = 0; i < edgeCount; i++)
                    {
                        var (p0, p1) = tileEdges[i];
                        double y0 = p0.Y;
                        double y1 = p1.Y;
                        if (Math.Abs(y1 - y0) < 1e-10)
                            continue;
                        if (y0 > y1)
                        {
                            (y0, y1) = (y1, y0);
                        }
                        if (rowY >= y0 && rowY < y1)
                            crossingCount++;
                    }

                    if (crossingCount % 2 == 1)
                    {
                        double tileMinX = tileX * TTile.Width;
                        for (int col = 0; col < TTile.Width; col++)
                        {
                            double colMinX = tileMinX + col;
                            int insideCount = 0;
                            // 4-sample supersampling per column
                            for (int s = 0; s < 4; s++)
                            {
                                double testX = colMinX + (s + 0.5) / 4.0;
                                if (IsPointInsidePathAt(written, localXform, testX, rowY))
                                    insideCount++;
                            }
                            coverage[col] = (byte)(insideCount * 255 / 4);
                        }
                    }
                }

                EmitRowStrips<TTile>(coverage, grid.TileIndex(tileX, tileY), row, cmd.FillPath.PaintId, ref builder);
            }
        }
        finally
        {
            ArrayPool<Point>.Shared.Return(flatPoints);
        }
    }

    private static void EmitStrokePath<TTile>(
        SceneBuffer scene,
        ClassificationEntry entry,
        Affine xform,
        TileGrid<TTile> grid,
        int tileX,
        int tileY,
        (Point, Point)[] edges,
        byte[] coverage,
        ref StripsBuilder builder)
        where TTile : struct, ITileSize
    {
        if (!TryGetCommandAtOrder(scene, entry.CommandOrder, out var cmd))
            return;
        if (cmd.Op != SceneOpcode.StrokePath)
            return;

        if (!scene.TryGetPath(cmd.StrokePath.PathId, out var pathData))
            return;

        var localXform = xform * scene.GetTransform(cmd.StrokePath.TransformId);
        float halfWidth = cmd.StrokePath.StrokeWidth * 0.5f;

        int edgeCount = 0;

        void EmitClipped(Point a, Point b)
        {
            var clipped = ClipEdgeToTile(a, b, tileX, tileY, TTile.Width, TTile.Height);
            if (clipped.HasValue && edgeCount < edges.Length)
                edges[edgeCount++] = clipped.Value;
        }

        var subpathBuilder = BezPathBuilder.Begin(64);
        bool hasMoveTo = false;
        bool currentClosed = false;

        foreach (var seg in pathData.Path.Iterate())
        {
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    if (hasMoveTo)
                    {
                        ProcessStrokeSubpath(
                            subpathBuilder.Build(), localXform, currentClosed,
                            halfWidth, EmitClipped);
                        subpathBuilder.Dispose();
                        subpathBuilder = BezPathBuilder.Begin(64);
                    }
                    subpathBuilder.MoveTo(localXform.Transform(seg.End));
                    hasMoveTo = true;
                    currentClosed = false;
                    break;
                case PathVerb.LineTo:
                    subpathBuilder.LineTo(localXform.Transform(seg.End));
                    break;
                case PathVerb.QuadTo:
                    subpathBuilder.QuadTo(
                        localXform.Transform(seg.Control0),
                        localXform.Transform(seg.End));
                    break;
                case PathVerb.CubicTo:
                    subpathBuilder.CubicTo(
                        localXform.Transform(seg.Control0),
                        localXform.Transform(seg.Control1),
                        localXform.Transform(seg.End));
                    break;
                case PathVerb.Close:
                    subpathBuilder.Close();
                    currentClosed = true;
                    break;
            }
        }

        if (hasMoveTo)
        {
            ProcessStrokeSubpath(
                subpathBuilder.Build(), localXform, currentClosed,
                halfWidth, EmitClipped);
        }
        subpathBuilder.Dispose();

        if (edgeCount == 0)
        {
            // Interior tile fallback: no contour edges intersect this tile,
            // but the tile may be completely inside the stroke band.
            // Compute distance from tile center to the original path.
            double tileCenterX = tileX * TTile.Width + TTile.Width * 0.5;
            double tileCenterY = tileY * TTile.Height + TTile.Height * 0.5;
            var tileCenter = new Point(tileCenterX, tileCenterY);

            double minDist = double.MaxValue;
            Point? pathStart = null;
            Point? prevPoint = null;

            foreach (var seg in pathData.Path.Iterate())
            {
                switch (seg.Verb)
                {
                    case PathVerb.MoveTo:
                        pathStart = localXform.Transform(seg.End);
                        prevPoint = pathStart;
                        break;
                    case PathVerb.LineTo:
                        if (prevPoint.HasValue)
                        {
                            var curr = localXform.Transform(seg.End);
                            var dist = new Line(prevPoint.Value, curr).DistanceTo(tileCenter);
                            if (dist < minDist)
                                minDist = dist;
                            prevPoint = curr;
                        }
                        break;
                    case PathVerb.QuadTo:
                        if (prevPoint.HasValue)
                        {
                            var p0 = prevPoint.Value;
                            var p1 = localXform.Transform(seg.Control0);
                            var p2 = localXform.Transform(seg.End);
                            for (int t = 1; t <= 4; t++)
                            {
                                double tt = t / 4.0;
                                double mt = 1.0 - tt;
                                var pt = new Point(
                                    mt * mt * p0.X + 2.0 * mt * tt * p1.X + tt * tt * p2.X,
                                    mt * mt * p0.Y + 2.0 * mt * tt * p1.Y + tt * tt * p2.Y);
                                var dist = new Line(prevPoint.Value, pt).DistanceTo(tileCenter);
                                if (dist < minDist)
                                    minDist = dist;
                                prevPoint = pt;
                            }
                        }
                        break;
                    case PathVerb.CubicTo:
                        if (prevPoint.HasValue)
                        {
                            var p0 = prevPoint.Value;
                            var p1 = localXform.Transform(seg.Control0);
                            var p2 = localXform.Transform(seg.Control1);
                            var p3 = localXform.Transform(seg.End);
                            for (int t = 1; t <= 4; t++)
                            {
                                double tt = t / 4.0;
                                double mt = 1.0 - tt;
                                double mt2 = mt * mt;
                                double mt3 = mt2 * mt;
                                double tt2 = tt * tt;
                                double tt3 = tt2 * tt;
                                var pt = new Point(
                                    mt3 * p0.X + 3.0 * mt2 * tt * p1.X + 3.0 * mt * tt2 * p2.X + tt3 * p3.X,
                                    mt3 * p0.Y + 3.0 * mt2 * tt * p1.Y + 3.0 * mt * tt2 * p2.Y + tt3 * p3.Y);
                                var dist = new Line(prevPoint.Value, pt).DistanceTo(tileCenter);
                                if (dist < minDist)
                                    minDist = dist;
                                prevPoint = pt;
                            }
                        }
                        break;
                    case PathVerb.Close:
                        if (prevPoint.HasValue && pathStart.HasValue)
                        {
                            var dist = new Line(prevPoint.Value, pathStart.Value).DistanceTo(tileCenter);
                            if (dist < minDist)
                                minDist = dist;
                        }
                        break;
                }
            }

            if (minDist <= halfWidth)
            {
                for (int row = 0; row < TTile.Height; row++)
                {
                    Array.Fill(coverage, (byte)255, 0, TTile.Width);
                    EmitRowStrips<TTile>(coverage, grid.TileIndex(tileX, tileY), row, cmd.StrokePath.PaintId, ref builder);
                }
            }
            return;
        }

        var tileEdges = edges.AsSpan(0, edgeCount);

        for (int row = 0; row < TTile.Height; row++)
        {
            AnalyticCoverage.ComputeColumnCoverage(
                tileEdges,
                coverage,
                tileX,
                tileY,
                TTile.Width,
                row);

            EmitRowStrips<TTile>(coverage, grid.TileIndex(tileX, tileY), row, cmd.StrokePath.PaintId, ref builder);
        }
    }

    private static void ProcessStrokeSubpath(
        BezPath subpath,
        Affine xform,
        bool isClosed,
        float halfWidth,
        Action<Point, Point> emitEdge)
    {
        var flatPoints = ArrayPool<Point>.Shared.Rent(65536);
        try
        {
            FlattenSink sink = new FlattenSink(flatPoints.AsSpan(), autoflush: true);
            CurveFlattener.BezPath(subpath, 0.05, ref sink);
            var pts = sink.Written;

            if (isClosed && pts.Length > 1 && pts[0] == pts[pts.Length - 1])
            {
                pts = pts.Slice(0, pts.Length - 1);
            }

            if (pts.Length < 2)
                return;

            var subpathStruct = new StrokeContourBuilder.Subpath(pts, isClosed);
            StrokeContourBuilder.BuildSubpath(subpathStruct, halfWidth, emitEdge);
        }
        finally
        {
            ArrayPool<Point>.Shared.Return(flatPoints);
        }
    }

    private static void EmitFullCoverage<TTile>(int tileIndex, int paintId, ref StripsBuilder builder)
        where TTile : struct, ITileSize
    {
        ushort rowMask = (ushort)((1 << TTile.Height) - 1);
        int totalCoverage = TTile.Height * TTile.Width;
        var coverage = new byte[totalCoverage];
        for (int i = 0; i < totalCoverage; i++)
            coverage[i] = 0xFF;

        var strip = new Strip(
            (uint)tileIndex,
            rowMask,
            0,
            (ushort)(TTile.Width - 1),
            (uint)builder.CoverageCount,
            (uint)paintId);
        builder.AddStrip(strip, coverage);
    }

    private static void EmitRowStrips<TTile>(byte[] coverage, int tileIndex, int row, int paintId, ref StripsBuilder builder)
        where TTile : struct, ITileSize
    {
        int tileWidth = TTile.Width;

        int stripStart = -1;
        for (int col = 0; col <= tileWidth; col++)
        {
            bool hasCoverage = col < tileWidth && coverage[col] > 0;

            if (hasCoverage && stripStart < 0)
            {
                stripStart = col;
            }
            else if (!hasCoverage && stripStart >= 0)
            {
                EmitStrip<TTile>(tileIndex, row, stripStart, col - 1, paintId, coverage, ref builder);
                stripStart = -1;
            }
        }
    }

    private static void EmitStrip<TTile>(int tileIndex, int row, int x0, int x1, int paintId, byte[] coverage, ref StripsBuilder builder)
        where TTile : struct, ITileSize
    {
        if (x0 > x1)
            return;

        int numCols = x1 - x0 + 1;
        var stripCoverage = new byte[numCols];

        for (int col = x0; col <= x1; col++)
            stripCoverage[col - x0] = coverage[col];

        ushort rowMask = (ushort)(1 << row);

        var strip = new Strip(
            (uint)tileIndex,
            rowMask,
            (ushort)x0,
            (ushort)x1,
            (uint)builder.CoverageCount,
            (uint)paintId);
        builder.AddStrip(strip, stripCoverage);
    }

    private const double EdgeEpsilon = 1e-6;

    private static (Point, Point)? ClipEdgeToTile(Point start, Point end, int tileX, int tileY, int tileWidth, int tileHeight)
    {
        double minX = tileX * tileWidth;
        double maxX = minX + tileWidth;
        double minY = tileY * tileHeight;
        double maxY = minY + tileHeight;

        double sx = start.X;
        double sy = start.Y;
        double ex = end.X;
        double ey = end.Y;

        if ((sx < minX && ex < minX) || (sx > maxX && ex > maxX))
            return null;
        if ((sy < minY && ey < minY) || (sy > maxY && ey > maxY))
            return null;

        if (sx < minX)
        {
            (sx, sy) = IntersectX(sx, sy, ex, ey, minX);
        }
        if (ex < minX)
        {
            (ex, ey) = IntersectX(ex, ey, sx, sy, minX);
        }
        if (sx > maxX)
        {
            (sx, sy) = IntersectX(sx, sy, ex, ey, maxX);
        }
        if (ex > maxX)
        {
            (ex, ey) = IntersectX(ex, ey, sx, sy, maxX);
        }

        if (sy < minY)
        {
            (sx, sy) = IntersectY(sx, sy, ex, ey, minY);
        }
        if (ey < minY)
        {
            (ex, ey) = IntersectY(ex, ey, sx, sy, minY);
        }
        if (sy > maxY)
        {
            (sx, sy) = IntersectY(sx, sy, ex, ey, maxY);
        }
        if (ey > maxY)
        {
            (ex, ey) = IntersectY(ex, ey, sx, sy, maxY);
        }

        if (sx < minX || sx > maxX || ex < minX || ex > maxX)
            return null;
        if (sy < minY || sy > maxY || ey < minY || ey > maxY)
            return null;

        double eps = EdgeEpsilon;
        bool startNeedsInflateX = Math.Abs(sx - minX) < eps || Math.Abs(sx - maxX) < eps;
        bool startNeedsInflateY = Math.Abs(sy - minY) < eps || Math.Abs(sy - maxY) < eps;
        bool endNeedsInflateX = Math.Abs(ex - minX) < eps || Math.Abs(ex - maxX) < eps;
        bool endNeedsInflateY = Math.Abs(ey - minY) < eps || Math.Abs(ey - maxY) < eps;

        if (startNeedsInflateX)
            sx = sx < minX + (maxX - minX) / 2 ? minX + eps : maxX - eps;
        if (startNeedsInflateY)
            sy = sy < minY + (maxY - minY) / 2 ? minY + eps : maxY - eps;
        if (endNeedsInflateX)
            ex = ex < minX + (maxX - minX) / 2 ? minX + eps : maxX - eps;
        if (endNeedsInflateY)
            ey = ey < minY + (maxY - minY) / 2 ? minY + eps : maxY - eps;

        return (new Point(sx, sy), new Point(ex, ey));
    }

    private static (double x, double y) IntersectX(double x1, double y1, double x2, double y2, double x)
    {
        double dx = x2 - x1;
        if (Math.Abs(dx) < 1e-10)
            return (x, y1);
        double t = (x - x1) / dx;
        double y = y1 + t * (y2 - y1);
        return (x, y);
    }

    private static (double x, double y) IntersectY(double x1, double y1, double x2, double y2, double y)
    {
        double dy = y2 - y1;
        if (Math.Abs(dy) < 1e-10)
            return (x1, y);
        double t = (y - y1) / dy;
        double x = x1 + t * (x2 - x1);
        return (x, y);
    }

    private static bool TryGetCommandAtOrder(SceneBuffer scene, int order, out SceneCommand cmd)
    {
        var commands = scene.Commands;
        if (order < 0 || order >= commands.Length)
        {
            cmd = default;
            return false;
        }
        cmd = commands[order];
        return true;
    }

    private static Rect TransformRect(Affine a, Rect r)
    {
        if (r.IsEmpty)
            return Rect.Empty;

        double minX = r.MinX;
        double minY = r.MinY;
        double maxX = r.MaxX;
        double maxY = r.MaxY;

        Point p0 = a.Transform(new Point(minX, minY));
        Point p1 = a.Transform(new Point(maxX, minY));
        Point p2 = a.Transform(new Point(maxX, maxY));
        Point p3 = a.Transform(new Point(minX, maxY));

        double resultMinX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double resultMinY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double resultMaxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double resultMaxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        if (resultMinX >= resultMaxX || resultMinY >= resultMaxY)
            return Rect.Empty;

        return Rect.FromLTRB(resultMinX, resultMinY, resultMaxX, resultMaxY);
    }

    private static Rect Intersect(Rect a, Rect b)
    {
        if (a.IsEmpty || b.IsEmpty)
            return Rect.Empty;

        double minX = Math.Max(a.MinX, b.MinX);
        double minY = Math.Max(a.MinY, b.MinY);
        double maxX = Math.Min(a.MaxX, b.MaxX);
        double maxY = Math.Min(a.MaxY, b.MaxY);

        if (minX >= maxX || minY >= maxY)
            return Rect.Empty;

        return Rect.FromLTRB(minX, minY, maxX, maxY);
    }

    private static bool IsPointInsidePath(ReadOnlySpan<Point> polyline, Affine xform, int tileX, int tileY, int tileW, int tileH)
    {
        double cx = tileX * tileW + tileW * 0.5;
        double cy = tileY * tileH + tileH * 0.5;
        return IsPointInsidePathAt(polyline, xform, cx, cy);
    }

    private static bool IsPointInsidePathAt(ReadOnlySpan<Point> polyline, Affine xform, double testX, double testY)
    {
        var testPoint = new Point(testX, testY);

        bool inside = false;
        for (int i = 0, j = polyline.Length - 1; i < polyline.Length; j = i++)
        {
            var pi = xform.Transform(polyline[i]);
            var pj = xform.Transform(polyline[j]);

            if ((pi.Y > testPoint.Y) != (pj.Y > testPoint.Y) &&
                testPoint.X < (pj.X - pi.X) * (testPoint.Y - pi.Y) / (pj.Y - pi.Y) + pi.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
