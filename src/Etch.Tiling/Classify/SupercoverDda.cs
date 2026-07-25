using System;
using Etch.Geometry;

namespace Etch.Tiling.Classify;

public static class SupercoverDda
{
    public static int Walk(Point from, Point to, int tileLog2Width, int tileLog2Height, Span<int> outTileIndices)
    {
        int x0 = (int)Math.Floor(from.X) >> tileLog2Width;
        int y0 = (int)Math.Floor(from.Y) >> tileLog2Height;
        int x1 = (int)Math.Floor(to.X) >> tileLog2Width;
        int y1 = (int)Math.Floor(to.Y) >> tileLog2Height;

        int dx = Math.Abs(x1 - x0);
        int dy = Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        int x = x0;
        int y = y0;
        int count = 0;

        while (true)
        {
            if (count >= outTileIndices.Length)
                return count;

            uint tileKey = ((uint)y << 16) | (uint)x;
            if (!Contains(outTileIndices, count, (int)tileKey))
                outTileIndices[count++] = (int)tileKey;

            if (x == x1 && y == y1)
                break;

            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x += sx;
                if (count < outTileIndices.Length)
                {
                    tileKey = ((uint)y << 16) | (uint)x;
                    if (!Contains(outTileIndices, count, (int)tileKey))
                        outTileIndices[count++] = (int)tileKey;
                }
            }
            if (e2 < dx)
            {
                err += dx;
                y += sy;
                if (count < outTileIndices.Length)
                {
                    tileKey = ((uint)y << 16) | (uint)x;
                    if (!Contains(outTileIndices, count, (int)tileKey))
                        outTileIndices[count++] = (int)tileKey;
                }
            }
        }

        return count;
    }

    private static bool Contains(ReadOnlySpan<int> arr, int count, int value)
    {
        for (int i = 0; i < count; i++)
        {
            if (arr[i] == value)
                return true;
        }
        return false;
    }
}
