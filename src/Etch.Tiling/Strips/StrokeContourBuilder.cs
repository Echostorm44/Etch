using System;
using Etch.Geometry;

namespace Etch.Tiling.Strips;

/// <summary>
/// Generates closed stroke-outline polygons from flattened subpaths.
/// Produces bevel joins at shared vertices and square caps at open ends.
/// </summary>
internal static class StrokeContourBuilder
{
    public readonly ref struct Subpath
    {
        public readonly ReadOnlySpan<Point> Points;
        public readonly bool IsClosed;

        public Subpath(ReadOnlySpan<Point> points, bool isClosed)
        {
            Points = points;
            IsClosed = isClosed;
        }
    }

    public static void BuildSubpath(
        Subpath subpath,
        float halfWidth,
        Action<Point, Point> emitEdge)
    {
        if (halfWidth <= 0)
            return;

        var pts = subpath.Points;
        if (pts.Length < 2)
            return;

        int m = pts.Length - 1; // segment count
        if (subpath.IsClosed)
        {
            // Closed: last point duplicates first.
            m = pts.Length - 1;
        }

        if (m < 1)
            return;

        // Per-segment offset endpoints: start and end for positive and negative sides.
        Span<Point> bufPosS = stackalloc Point[256];
        Span<Point> bufPosE = stackalloc Point[256];
        Span<Point> bufNegS = stackalloc Point[256];
        Span<Point> bufNegE = stackalloc Point[256];

        if (m > bufPosS.Length)
        {
            bufPosS = new Point[m];
            bufPosE = new Point[m];
            bufNegS = new Point[m];
            bufNegE = new Point[m];
        }

        var posS = bufPosS.Slice(0, m);
        var posE = bufPosE.Slice(0, m);
        var negS = bufNegS.Slice(0, m);
        var negE = bufNegE.Slice(0, m);

        for (int i = 0; i < m; i++)
        {
            var p0 = pts[i];
            var p1 = pts[i + 1];
            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-10)
            {
                posS[i] = p0;
                posE[i] = p1;
                negS[i] = p0;
                negE[i] = p1;
                continue;
            }

            double nx = -dy / len;
            double ny = dx / len;

            posS[i] = new Point(p0.X + nx * halfWidth, p0.Y + ny * halfWidth);
            negS[i] = new Point(p0.X - nx * halfWidth, p0.Y - ny * halfWidth);
            posE[i] = new Point(p1.X + nx * halfWidth, p1.Y + ny * halfWidth);
            negE[i] = new Point(p1.X - nx * halfWidth, p1.Y - ny * halfWidth);
        }

        if (subpath.IsClosed)
        {
            EmitClosedRing(m, posS, posE, emitEdge);
            EmitClosedRing(m, negS, negE, emitEdge);
        }
        else
        {
            EmitOpenOutline(m, pts, posS, posE, negS, negE, halfWidth, emitEdge);
        }
    }

    private static void EmitClosedRing(
        int m,
        ReadOnlySpan<Point> startOffsets,
        ReadOnlySpan<Point> endOffsets,
        Action<Point, Point> emitEdge)
    {
        for (int i = 0; i < m; i++)
        {
            emitEdge(startOffsets[i], endOffsets[i]);
            int next = (i + 1) % m;
            emitEdge(endOffsets[i], startOffsets[next]);
        }
    }

    private static void EmitOpenOutline(
        int m,
        ReadOnlySpan<Point> pts,
        ReadOnlySpan<Point> posS,
        ReadOnlySpan<Point> posE,
        ReadOnlySpan<Point> negS,
        ReadOnlySpan<Point> negE,
        float halfWidth,
        Action<Point, Point> emitEdge)
    {
        // Positive side forward.
        for (int i = 0; i < m; i++)
        {
            emitEdge(posS[i], posE[i]);
            if (i < m - 1)
            {
                emitEdge(posE[i], posS[i + 1]);
            }
        }

        // End cap.
        var pEnd = pts[m];
        var pPrev = pts[m - 1];
        double dxe = pEnd.X - pPrev.X;
        double dye = pEnd.Y - pPrev.Y;
        double lenE = Math.Sqrt(dxe * dxe + dye * dye);
        if (lenE > 1e-10)
        {
            double nxe = -dye / lenE;
            double nye = dxe / lenE;
            var capPos = new Point(pEnd.X + nxe * halfWidth, pEnd.Y + nye * halfWidth);
            var capNeg = new Point(pEnd.X - nxe * halfWidth, pEnd.Y - nye * halfWidth);
            emitEdge(capPos, capNeg);
        }
        else
        {
            emitEdge(posE[m - 1], negE[m - 1]);
        }

        // Negative side backward.
        for (int i = m - 1; i >= 0; i--)
        {
            emitEdge(negE[i], negS[i]);
            if (i > 0)
            {
                emitEdge(negS[i], negE[i - 1]);
            }
        }

        // Start cap.
        var pStart = pts[0];
        var pNext = pts[1];
        double dxs = pNext.X - pStart.X;
        double dys = pNext.Y - pStart.Y;
        double lenS = Math.Sqrt(dxs * dxs + dys * dys);
        if (lenS > 1e-10)
        {
            double nxs = -dys / lenS;
            double nys = dxs / lenS;
            var capNegStart = new Point(pStart.X - nxs * halfWidth, pStart.Y - nys * halfWidth);
            var capPosStart = new Point(pStart.X + nxs * halfWidth, pStart.Y + nys * halfWidth);
            emitEdge(capNegStart, capPosStart);
        }
        else
        {
            emitEdge(negS[0], posS[0]);
        }
    }
}
