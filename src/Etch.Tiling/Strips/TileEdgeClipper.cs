using System;
using Etch.Geometry;

namespace Etch.Tiling.Strips;

public static class TileEdgeClipper
{
    public static int ClipEdges(
        ReadOnlySpan<(Point Start, Point End)> edges,
        Span<(Point Start, Point End)> clippedEdges,
        int tileX,
        int tileY,
        int tileWidth,
        int tileHeight)
    {
        int count = edges.Length;
        int outCount = 0;

        double minX = tileX * tileWidth;
        double maxX = minX + tileWidth;
        double minY = tileY * tileHeight;
        double maxY = minY + tileHeight;

        for (int i = 0; i < count; i++)
        {
            var edge = edges[i];
            if (ClipEdgeToTile(edge.Start, edge.End, minX, maxX, minY, maxY, clippedEdges, ref outCount))
            {
                if (outCount >= clippedEdges.Length)
                    return clippedEdges.Length;
            }
        }

        return outCount;
    }

    private static bool ClipEdgeToTile(
        Point start,
        Point end,
        double minX,
        double maxX,
        double minY,
        double maxY,
        Span<(Point Start, Point End)> clippedEdges,
        ref int outCount)
    {
        double sx = start.X;
        double sy = start.Y;
        double ex = end.X;
        double ey = end.Y;

        if (!EdgeIntersectsTile(sx, sy, ex, ey, minX, maxX, minY, maxY))
            return false;

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
            return false;
        if (sy < minY || sy > maxY || ey < minY || ey > maxY)
            return false;

        clippedEdges[outCount++] = (new Point(sx, sy), new Point(ex, ey));
        return true;
    }

    private static bool EdgeIntersectsTile(
        double sx, double sy, double ex, double ey,
        double minX, double maxX, double minY, double maxY)
    {
        if ((sx < minX && ex < minX) || (sx > maxX && ex > maxX))
            return false;
        if ((sy < minY && ey < minY) || (sy > maxY && ey > maxY))
            return false;
        return true;
    }

    private static (double x, double y) IntersectX(
        double x1, double y1, double x2, double y2, double x)
    {
        double dx = x2 - x1;
        if (Math.Abs(dx) < 1e-10)
            return (x, y1);
        double t = (x - x1) / dx;
        double y = y1 + t * (y2 - y1);
        return (x, y);
    }

    private static (double x, double y) IntersectY(
        double x1, double y1, double x2, double y2, double y)
    {
        double dy = y2 - y1;
        if (Math.Abs(dy) < 1e-10)
            return (x1, y);
        double t = (y - y1) / dy;
        double x = x1 + t * (x2 - x1);
        return (x, y);
    }
}
