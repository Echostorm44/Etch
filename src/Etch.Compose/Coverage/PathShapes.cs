using Etch.Geometry;

namespace Etch.Compose.Coverage;

/// <summary>
/// Exact recognition of the simple shapes UI code builds as paths — a line, an axis-aligned rect, a
/// circle and a rounded rect made of quarter-circle cubics — so they draw as the analytic shape
/// rather than a rasterized mask. Recognition is exact (to float rounding): a path that only
/// approximates a shape is drawn as the path it is.
/// </summary>
public static class PathShapes
{
    /// <summary>The cubic Bézier circle constant: control-handle length / radius for a quarter arc.</summary>
    public const double Kappa = 0.5522847498307936;

    private const double Epsilon = 1e-3;

    /// <summary>MoveTo then a single LineTo.</summary>
    public static bool TryLine(ReadOnlySpan<byte> verbs, ReadOnlySpan<double> coords, out float x0, out float y0, out float x1, out float y1)
    {
        x0 = y0 = x1 = y1 = 0f;
        if (verbs.Length != 2 || verbs[0] != (byte)PathVerb.MoveTo || verbs[1] != (byte)PathVerb.LineTo || coords.Length != 4)
        {
            return false;
        }
        x0 = (float)coords[0];
        y0 = (float)coords[1];
        x1 = (float)coords[2];
        y1 = (float)coords[3];
        return true;
    }

    /// <summary>MoveTo, three or four LineTos round an axis-aligned rect, optional Close.</summary>
    public static bool TryRect(ReadOnlySpan<byte> verbs, ReadOnlySpan<double> coords, out float x, out float y, out float w, out float h)
    {
        x = y = w = h = 0f;
        int n = verbs.Length;
        if (n > 0 && verbs[n - 1] == (byte)PathVerb.Close)
        {
            n--;
        }
        if (n < 4 || n > 5 || verbs[0] != (byte)PathVerb.MoveTo)
        {
            return false;
        }
        for (int i = 1; i < n; i++)
        {
            if (verbs[i] != (byte)PathVerb.LineTo)
            {
                return false;
            }
        }
        if (coords.Length != n * 2)
        {
            return false;
        }
        // A fifth point must return to the start.
        if (n == 5 && (coords[8] != coords[0] || coords[9] != coords[1]))
        {
            return false;
        }

        // Consecutive points (cyclically, over the first four) must alternate horizontal/vertical.
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            double dx = coords[j * 2] - coords[i * 2];
            double dy = coords[j * 2 + 1] - coords[i * 2 + 1];
            if (dx != 0 && dy != 0)
            {
                return false;
            }
        }
        double minX = Math.Min(Math.Min(coords[0], coords[2]), Math.Min(coords[4], coords[6]));
        double maxX = Math.Max(Math.Max(coords[0], coords[2]), Math.Max(coords[4], coords[6]));
        double minY = Math.Min(Math.Min(coords[1], coords[3]), Math.Min(coords[5], coords[7]));
        double maxY = Math.Max(Math.Max(coords[1], coords[3]), Math.Max(coords[5], coords[7]));
        // Every corner must be a corner of the bounds (rules out degenerate zig-zags).
        for (int i = 0; i < 4; i++)
        {
            bool onX = coords[i * 2] == minX || coords[i * 2] == maxX;
            bool onY = coords[i * 2 + 1] == minY || coords[i * 2 + 1] == maxY;
            if (!onX || !onY)
            {
                return false;
            }
        }
        x = (float)minX;
        y = (float)minY;
        w = (float)(maxX - minX);
        h = (float)(maxY - minY);
        return w > 0 && h > 0;
    }

    /// <summary>MoveTo, four quarter-circle cubics between the cardinal points, optional Close.</summary>
    public static bool TryCircle(ReadOnlySpan<byte> verbs, ReadOnlySpan<double> coords, out float cx, out float cy, out float r)
    {
        cx = cy = r = 0f;
        int n = verbs.Length;
        if (n > 0 && verbs[n - 1] == (byte)PathVerb.Close)
        {
            n--;
        }
        if (n != 5 || verbs[0] != (byte)PathVerb.MoveTo || coords.Length != 2 + 4 * 6)
        {
            return false;
        }
        for (int i = 1; i < 5; i++)
        {
            if (verbs[i] != (byte)PathVerb.CubicTo)
            {
                return false;
            }
        }

        // The four segment end points are the cardinal points; their mean is the centre.
        double mx = 0, my = 0;
        for (int i = 0; i < 4; i++)
        {
            mx += coords[2 + i * 6 + 4];
            my += coords[2 + i * 6 + 5];
        }
        mx /= 4;
        my /= 4;
        double radius = Math.Sqrt(Sq(coords[0] - mx) + Sq(coords[1] - my));
        if (radius <= 0)
        {
            return false;
        }

        double px = coords[0], py = coords[1];
        for (int i = 0; i < 4; i++)
        {
            int c = 2 + i * 6;
            if (!IsQuarterArc(px, py, coords[c], coords[c + 1], coords[c + 2], coords[c + 3], coords[c + 4], coords[c + 5], mx, my, radius))
            {
                return false;
            }
            px = coords[c + 4];
            py = coords[c + 5];
        }
        if (Math.Abs(px - coords[0]) > Epsilon * radius || Math.Abs(py - coords[1]) > Epsilon * radius)
        {
            return false;
        }
        cx = (float)mx;
        cy = (float)my;
        r = (float)radius;
        return true;
    }

    /// <summary>
    /// A closed contour of four axis-aligned lines and four quarter-circle corner cubics of one
    /// radius (zero-length lines allowed), in either winding and starting anywhere. The corners
    /// must be convex — each arc centred r inside a different corner of the bounds — and every
    /// point on the bounds: a rect with concave quarter-circle notches is not a rounded rect.
    /// </summary>
    public static bool TryRoundedRect(ReadOnlySpan<byte> verbs, ReadOnlySpan<double> coords,
        out float x, out float y, out float w, out float h, out float radius)
    {
        x = y = w = h = radius = 0f;
        int n = verbs.Length;
        if (n > 0 && verbs[n - 1] == (byte)PathVerb.Close)
        {
            n--;
        }
        if (n < 5 || n > 10 || verbs[0] != (byte)PathVerb.MoveTo || coords.Length < 2)
        {
            return false;
        }

        Span<double> centres = stackalloc double[16];
        Span<double> points = stackalloc double[20];
        points[0] = coords[0];
        points[1] = coords[1];
        int pointCount = 1;
        double px = coords[0], py = coords[1];
        int c = 2;
        int cubics = 0;
        double r = -1;
        double minX = px, minY = py, maxX = px, maxY = py;
        for (int i = 1; i < n; i++)
        {
            if (verbs[i] == (byte)PathVerb.LineTo)
            {
                if (c + 2 > coords.Length)
                {
                    return false;
                }
                double qx = coords[c], qy = coords[c + 1];
                if (qx != px && qy != py)
                {
                    return false;
                }
                px = qx;
                py = qy;
                c += 2;
            }
            else if (verbs[i] == (byte)PathVerb.CubicTo)
            {
                if (c + 6 > coords.Length || cubics == 4)
                {
                    return false;
                }
                double ex = coords[c + 4], ey = coords[c + 5];
                double dx = Math.Abs(ex - px), dy = Math.Abs(ey - py);
                if (Math.Abs(dx - dy) > Epsilon * Math.Max(dx, 1))
                {
                    return false;
                }
                // The corner's centre: the end points are a quarter turn apart about it.
                double ccx, ccy;
                if (coords[c] == px)
                {
                    // Leaves vertically: the centre is level with the start and in line with the end.
                    ccx = ex;
                    ccy = py;
                }
                else
                {
                    // Leaves horizontally: in line with the start and level with the end.
                    ccx = px;
                    ccy = ey;
                }
                double cr = dx;
                if (cr <= 0 || !IsQuarterArc(px, py, coords[c], coords[c + 1], coords[c + 2], coords[c + 3], ex, ey, ccx, ccy, cr))
                {
                    return false;
                }
                if (r < 0)
                {
                    r = cr;
                }
                else if (Math.Abs(cr - r) > Epsilon * r)
                {
                    return false;
                }
                // Which way the arc bulges from its centre: toward the bounds corner it rounds.
                centres[cubics * 4] = ccx;
                centres[cubics * 4 + 1] = ccy;
                centres[cubics * 4 + 2] = Math.Sign(px + ex - 2 * ccx);
                centres[cubics * 4 + 3] = Math.Sign(py + ey - 2 * ccy);
                cubics++;
                px = ex;
                py = ey;
                c += 6;
            }
            else
            {
                return false;
            }
            points[pointCount * 2] = px;
            points[pointCount * 2 + 1] = py;
            pointCount++;
            minX = Math.Min(minX, px);
            minY = Math.Min(minY, py);
            maxX = Math.Max(maxX, px);
            maxY = Math.Max(maxY, py);
        }
        if (cubics != 4 || c != coords.Length)
        {
            return false;
        }
        if (Math.Abs(px - coords[0]) > Epsilon * r || Math.Abs(py - coords[1]) > Epsilon * r)
        {
            return false;
        }
        // Convex corners: each centre r inside a corner of the bounds, the four corners distinct.
        double tol = Epsilon * Math.Max(r, 1);
        int seen = 0;
        for (int k = 0; k < 4; k++)
        {
            double ccx = centres[k * 4], ccy = centres[k * 4 + 1];
            double sx = centres[k * 4 + 2], sy = centres[k * 4 + 3];
            // A convex corner bulges toward the bounds corner, with its centre r inside both edges.
            int qx = sx < 0 && Math.Abs(ccx - (minX + r)) <= tol ? 0 : sx > 0 && Math.Abs(ccx - (maxX - r)) <= tol ? 1 : -1;
            int qy = sy < 0 && Math.Abs(ccy - (minY + r)) <= tol ? 0 : sy > 0 && Math.Abs(ccy - (maxY - r)) <= tol ? 1 : -1;
            if (qx < 0 || qy < 0)
            {
                return false;
            }
            seen |= 1 << (qy * 2 + qx);
        }
        if (seen != 0b1111)
        {
            return false;
        }
        // Every vertex on the bounds (no line cuts inward).
        for (int k = 0; k < pointCount; k++)
        {
            double vx = points[k * 2], vy = points[k * 2 + 1];
            if (Math.Abs(vx - minX) > tol && Math.Abs(vx - maxX) > tol && Math.Abs(vy - minY) > tol && Math.Abs(vy - maxY) > tol)
            {
                return false;
            }
        }
        x = (float)minX;
        y = (float)minY;
        w = (float)(maxX - minX);
        h = (float)(maxY - minY);
        radius = (float)r;
        return w > 0 && h > 0 && radius <= Math.Min(w, h) * 0.5f + Epsilon;
    }

    // True when (p0, c1, c2, p1) is the standard cubic for the quarter circle about (cx, cy) from p0 to p1.
    private static bool IsQuarterArc(double p0x, double p0y, double c1x, double c1y, double c2x, double c2y,
        double p1x, double p1y, double cx, double cy, double r)
    {
        double tol = Epsilon * Math.Max(r, 1);
        if (Math.Abs(Math.Sqrt(Sq(p0x - cx) + Sq(p0y - cy)) - r) > tol || Math.Abs(Math.Sqrt(Sq(p1x - cx) + Sq(p1y - cy)) - r) > tol)
        {
            return false;
        }
        // Quarter turn: the radii to the end points are perpendicular.
        if (Math.Abs((p0x - cx) * (p1x - cx) + (p0y - cy) * (p1y - cy)) > tol * r)
        {
            return false;
        }
        double e1x = p0x + Kappa * (p1x - cx), e1y = p0y + Kappa * (p1y - cy);
        double e2x = p1x + Kappa * (p0x - cx), e2y = p1y + Kappa * (p0y - cy);
        // Builders round the constant differently (0.5522847498 vs 0.55228475); allow that.
        double handleTol = Math.Max(tol, 1e-5 * r + 1e-6);
        return Math.Abs(c1x - e1x) <= handleTol && Math.Abs(c1y - e1y) <= handleTol
            && Math.Abs(c2x - e2x) <= handleTol && Math.Abs(c2y - e2y) <= handleTol;
    }

    private static double Sq(double v) => v * v;
}
