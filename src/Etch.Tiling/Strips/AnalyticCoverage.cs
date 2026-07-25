using System;
using Etch.Geometry;

namespace Etch.Tiling.Strips;

public static class AnalyticCoverage
{
    // Known limitation: paths whose edges fall exactly on tile boundaries may receive
    // minimal/zero coverage because the clipping step treats boundary-coincident
    // edges as outside the tile. This is a geometric edge case; normal rendered shapes
    // have sub-pixel positioning that avoids this. The coverage tolerance below helps
    // ensure non-zero coverage when overlap > 0 but computes to 0.
    public static void ComputeColumnCoverage(
        ReadOnlySpan<(Point Start, Point End)> edges,
        Span<byte> coveragePerColumn,
        int tileX,
        int tileY,
        int tileWidth,
        int rowIndex)
    {
        double tileMinX = tileX * tileWidth;
        double tileMinY = tileY * tileWidth;
        double rowY = tileMinY + rowIndex + 0.5;

        Span<(double x, int winding)> crossings = stackalloc (double, int)[256];
        int count = 0;

        for (int i = 0; i < edges.Length; i++)
        {
            var (p0, p1) = edges[i];
            double y0 = p0.Y;
            double y1 = p1.Y;

            if (Math.Abs(y1 - y0) < 1e-10)
                continue;

            int edgeWinding = 1;
            if (y0 > y1)
            {
                (y0, y1) = (y1, y0);
                edgeWinding = -1;
            }

            if (rowY < y0 || rowY >= y1)
                continue;

            double x = p0.X + (rowY - y0) * (p1.X - p0.X) / (y1 - y0);
            if (count < crossings.Length)
                crossings[count++] = (x, edgeWinding);
        }

        if (count == 0)
        {
            coveragePerColumn[..tileWidth].Clear();
            return;
        }

        for (int i = 1; i < count; i++)
        {
            var xw = crossings[i];
            int j = i - 1;
            while (j >= 0 && crossings[j].x > xw.x)
            {
                crossings[j + 1] = crossings[j];
                j--;
            }
            crossings[j + 1] = xw;
        }

        // Even-odd fill: if odd number of crossings, determine interior direction from
        // the last crossing's edge direction. Upward edge => interior to the right;
        // downward edge => interior to the left. Then add a virtual crossing at the
        // appropriate tile boundary so the existing paired logic handles it correctly.
        if (count % 2 == 1 && count < crossings.Length)
        {
            double lastX = crossings[count - 1].x;
            int lastWinding = crossings[count - 1].winding;

            // For even-odd: the last crossing toggles the inside state.
            // If last edge was upward (winding=+1), we just entered interior, so
            // interior extends to the right. Add virtual crossing at tileMaxX.
            // If last edge was downward (winding=-1), we just exited interior, so
            // interior extends to the left. Add virtual crossing at tileMinX.
            if (lastWinding > 0)
            {
                // Virtual crossing at right boundary, sorted after all real crossings
                crossings[count++] = (tileMinX + tileWidth, -1);
            }
            else
            {
                // Virtual crossing at left boundary; shift existing crossings right
                for (int i = count; i > 0; i--)
                    crossings[i] = crossings[i - 1];
                crossings[0] = (tileMinX, 1);
                count++;
            }
        }

        for (int col = 0; col < tileWidth; col++)
        {
            double colMinX = tileMinX + col;
            double colMaxX = colMinX + 1;
            double overlap = 0.0;

            // Even-odd: process crossings in pairs
            for (int i = 0; i < count - 1; i += 2)
            {
                double x0 = crossings[i].x;
                double x1 = crossings[i + 1].x;

                if (x1 <= colMinX || x0 >= colMaxX)
                    continue;

                double left = x0 < colMinX ? colMinX : x0;
                double right = x1 > colMaxX ? colMaxX : x1;

                if (right > left)
                    overlap += right - left;
            }

            int cov = (int)(overlap * 255.0);
            if (cov > 255) cov = 255;
            if (overlap > 0.0 && cov == 0) cov = 1;
            coveragePerColumn[col] = (byte)cov;
        }
    }

    public static void ComputeColumnCoverageNonZero(
        ReadOnlySpan<(Point Start, Point End)> edges,
        Span<byte> coveragePerColumn,
        int tileX,
        int tileY,
        int tileWidth,
        int rowIndex)
    {
        double tileMinX = tileX * tileWidth;
        double tileMinY = tileY * tileWidth;
        double rowY = tileMinY + rowIndex + 0.5;

        Span<(double x, int winding)> crossings = stackalloc (double, int)[256];
        int count = 0;

        for (int i = 0; i < edges.Length; i++)
        {
            var (p0, p1) = edges[i];
            double y0 = p0.Y;
            double y1 = p1.Y;

            if (Math.Abs(y1 - y0) < 1e-10)
                continue;

            int edgeWinding = 1;
            if (y0 > y1)
            {
                (y0, y1) = (y1, y0);
                (p0, p1) = (p1, p0);
                edgeWinding = -1;
            }

            if (rowY < y0 || rowY >= y1)
                continue;

            double x = p0.X + (rowY - y0) * (p1.X - p0.X) / (y1 - y0);
            if (count < crossings.Length)
                crossings[count++] = (x, edgeWinding);
        }

        if (count == 0)
        {
            coveragePerColumn[..tileWidth].Clear();
            return;
        }

        for (int i = 1; i < count; i++)
        {
            var xw = crossings[i];
            int j = i - 1;
            while (j >= 0 && crossings[j].x > xw.x)
            {
                crossings[j + 1] = crossings[j];
                j--;
            }
            crossings[j + 1] = xw;
        }

        // Non-zero fill: if final winding is non-zero, the path enters/exits the tile
        // and the interior extends to the tile boundary. Add a virtual crossing with
        // opposite winding at the appropriate boundary so the paired logic produces
        // exact fractional coverage instead of half-tile overfill.
        if (count > 0)
        {
            int finalWinding = 0;
            for (int i = 0; i < count; i++)
                finalWinding += crossings[i].winding;

            if (finalWinding != 0 && count < crossings.Length)
            {
                if (finalWinding > 0)
                {
                    // Interior extends to the right; virtual crossing at tileMaxX
                    crossings[count++] = (tileMinX + tileWidth, -finalWinding);
                }
                else
                {
                    // Interior extends to the left; virtual crossing at tileMinX
                    for (int i = count; i > 0; i--)
                        crossings[i] = crossings[i - 1];
                    crossings[0] = (tileMinX, -finalWinding);
                    count++;
                }
            }
        }

        int winding = 0;
        for (int col = 0; col < tileWidth; col++)
        {
            double colMinX = tileMinX + col;
            double colMaxX = colMinX + 1;
            double overlap = 0.0;

            for (int i = 0; i < count; i++)
            {
                double cx = crossings[i].x;

                // Add overlap for segment from previous crossing to current crossing
                if (i > 0 && winding != 0)
                {
                    double x0 = crossings[i - 1].x;
                    double x1 = cx;

                    if (x1 > colMinX && x0 < colMaxX)
                    {
                        double left = x0 < colMinX ? colMinX : x0;
                        double right = x1 > colMaxX ? colMaxX : x1;

                        if (right > left)
                            overlap += right - left;
                    }
                }

                winding += crossings[i].winding;
            }

            int cov = (int)(overlap * 255.0);
            if (cov > 255) cov = 255;
            coveragePerColumn[col] = (byte)cov;
            winding = 0; // Reset for next column
        }
    }
}
