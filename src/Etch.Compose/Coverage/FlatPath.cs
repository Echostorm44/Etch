using System.Runtime.InteropServices;
using Etch.Geometry;

namespace Etch.Compose.Coverage;

/// <summary>One polyline of a <see cref="FlatPath"/>: points [Start, Start + Count), optionally closed.</summary>
public readonly record struct FlatContour(int Start, int Count, bool Closed);

/// <summary>
/// A path flattened to polylines: a flat array of points and the contours that partition it.
/// Reused across calls; nothing allocates once the arrays have grown.
/// </summary>
public sealed class FlatPath
{
    private float[] xs = new float[256];
    private float[] ys = new float[256];
    private readonly List<FlatContour> contours = new();
    private int count;
    private int contourStart = -1;

    /// <summary>Number of points.</summary>
    public int PointCount => count;

    /// <summary>X coordinates of the points.</summary>
    public ReadOnlySpan<float> X => xs.AsSpan(0, count);

    /// <summary>Y coordinates of the points.</summary>
    public ReadOnlySpan<float> Y => ys.AsSpan(0, count);

    /// <summary>The contours.</summary>
    public ReadOnlySpan<FlatContour> Contours => CollectionsMarshal.AsSpan(contours);

    /// <summary>Empties the path.</summary>
    public void Clear()
    {
        count = 0;
        contours.Clear();
        contourStart = -1;
    }

    /// <summary>Starts a new contour at (x, y), ending any open one.</summary>
    public void MoveTo(float x, float y)
    {
        EndContour(closed: false);
        contourStart = count;
        Append(x, y);
    }

    /// <summary>Adds a point to the current contour (skipping exact repeats).</summary>
    public void LineTo(float x, float y)
    {
        if (contourStart < 0)
        {
            MoveTo(x, y);
            return;
        }
        if (count > contourStart && xs[count - 1] == x && ys[count - 1] == y)
        {
            return;
        }
        Append(x, y);
    }

    /// <summary>Ends the current contour, marking it closed.</summary>
    public void Close()
    {
        EndContour(closed: true);
    }

    /// <summary>Ends the current contour as open (a no-op when none is open).</summary>
    public void EndContour(bool closed)
    {
        if (contourStart < 0)
        {
            return;
        }
        int n = count - contourStart;
        if (closed && n > 1 && xs[count - 1] == xs[contourStart] && ys[count - 1] == ys[contourStart])
        {
            // A Close that returns to the start point needs no duplicate vertex.
            count--;
            n--;
        }
        if (n > 0)
        {
            contours.Add(new FlatContour(contourStart, n, closed));
        }
        else
        {
            count = contourStart;
        }
        contourStart = -1;
    }

    /// <summary>Axis-aligned bounds of every point, or false when the path is empty.</summary>
    public bool TryGetBounds(out float minX, out float minY, out float maxX, out float maxY)
    {
        minX = minY = float.MaxValue;
        maxX = maxY = float.MinValue;
        if (count == 0)
        {
            return false;
        }
        var px = X;
        var py = Y;
        for (int i = 0; i < px.Length; i++)
        {
            minX = Math.Min(minX, px[i]);
            maxX = Math.Max(maxX, px[i]);
            minY = Math.Min(minY, py[i]);
            maxY = Math.Max(maxY, py[i]);
        }
        return true;
    }

    /// <summary>Applies <paramref name="transform"/> to every point in place.</summary>
    public void Transform(in Affine transform)
    {
        float m00 = (float)transform.M00, m01 = (float)transform.M01, m02 = (float)transform.M02;
        float m10 = (float)transform.M10, m11 = (float)transform.M11, m12 = (float)transform.M12;
        for (int i = 0; i < count; i++)
        {
            float x = xs[i];
            float y = ys[i];
            xs[i] = m00 * x + m01 * y + m02;
            ys[i] = m10 * x + m11 * y + m12;
        }
    }

    private void Append(float x, float y)
    {
        if (count == xs.Length)
        {
            Array.Resize(ref xs, count * 2);
            Array.Resize(ref ys, count * 2);
        }
        xs[count] = x;
        ys[count] = y;
        count++;
    }
}

/// <summary>
/// Flattens Bézier paths to polylines. The segment count of each curve comes from Wang's
/// formula, so the deviation from the true curve stays under the tolerance and the result is a
/// pure function of the input (deterministic, no recursion).
/// </summary>
public static class PathFlattener
{
    /// <summary>
    /// Flattens <paramref name="path"/>, transformed by <paramref name="transform"/>, into
    /// <paramref name="output"/> (cleared first). Curves are flattened after transforming their
    /// control points, so <paramref name="tolerance"/> is in output units.
    /// </summary>
    public static void Flatten(BezPath path, in Affine transform, float tolerance, FlatPath output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        float tol = Math.Max(tolerance, 1e-4f);

        double sx = 0, sy = 0;
        double cx = 0, cy = 0;
        bool open = false;
        var it = path.Iterate();
        while (it.MoveNext())
        {
            var seg = it.Current;
            switch (seg.Verb)
            {
                case PathVerb.MoveTo:
                    {
                        var p = transform.Transform(seg.End);
                        output.MoveTo((float)p.X, (float)p.Y);
                        sx = cx = p.X;
                        sy = cy = p.Y;
                        open = true;
                        break;
                    }
                case PathVerb.LineTo:
                    {
                        var p = transform.Transform(seg.End);
                        EnsureOpen(output, ref open, cx, cy);
                        output.LineTo((float)p.X, (float)p.Y);
                        cx = p.X;
                        cy = p.Y;
                        break;
                    }
                case PathVerb.QuadTo:
                    {
                        var c1 = transform.Transform(seg.Control0);
                        var p = transform.Transform(seg.End);
                        EnsureOpen(output, ref open, cx, cy);
                        FlattenQuad(output, cx, cy, c1.X, c1.Y, p.X, p.Y, tol);
                        cx = p.X;
                        cy = p.Y;
                        break;
                    }
                case PathVerb.CubicTo:
                    {
                        var c1 = transform.Transform(seg.Control0);
                        var c2 = transform.Transform(seg.Control1);
                        var p = transform.Transform(seg.End);
                        EnsureOpen(output, ref open, cx, cy);
                        FlattenCubic(output, cx, cy, c1.X, c1.Y, c2.X, c2.Y, p.X, p.Y, tol);
                        cx = p.X;
                        cy = p.Y;
                        break;
                    }
                case PathVerb.Close:
                    if (open)
                    {
                        output.Close();
                        open = false;
                    }
                    cx = sx;
                    cy = sy;
                    break;
            }
        }
        output.EndContour(closed: false);
    }

    /// <summary>
    /// Flattens a path given as verbs (<see cref="PathVerb"/> values) and their coordinates
    /// (x, y pairs: one point per MoveTo/LineTo, two per QuadTo, three per CubicTo), transformed by
    /// <paramref name="transform"/>, into <paramref name="output"/> (cleared first).
    /// </summary>
    public static void Flatten(ReadOnlySpan<byte> verbs, ReadOnlySpan<double> coords, in Affine transform, float tolerance, FlatPath output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        float tol = Math.Max(tolerance, 1e-4f);
        double m00 = transform.M00, m01 = transform.M01, m02 = transform.M02;
        double m10 = transform.M10, m11 = transform.M11, m12 = transform.M12;

        double sx = 0, sy = 0;
        double cx = 0, cy = 0;
        bool open = false;
        int c = 0;
        for (int i = 0; i < verbs.Length; i++)
        {
            switch ((PathVerb)verbs[i])
            {
                case PathVerb.MoveTo:
                    {
                        double x = m00 * coords[c] + m01 * coords[c + 1] + m02;
                        double y = m10 * coords[c] + m11 * coords[c + 1] + m12;
                        c += 2;
                        output.MoveTo((float)x, (float)y);
                        sx = cx = x;
                        sy = cy = y;
                        open = true;
                        break;
                    }
                case PathVerb.LineTo:
                    {
                        double x = m00 * coords[c] + m01 * coords[c + 1] + m02;
                        double y = m10 * coords[c] + m11 * coords[c + 1] + m12;
                        c += 2;
                        EnsureOpen(output, ref open, cx, cy);
                        output.LineTo((float)x, (float)y);
                        cx = x;
                        cy = y;
                        break;
                    }
                case PathVerb.QuadTo:
                    {
                        double x1 = m00 * coords[c] + m01 * coords[c + 1] + m02;
                        double y1 = m10 * coords[c] + m11 * coords[c + 1] + m12;
                        double x = m00 * coords[c + 2] + m01 * coords[c + 3] + m02;
                        double y = m10 * coords[c + 2] + m11 * coords[c + 3] + m12;
                        c += 4;
                        EnsureOpen(output, ref open, cx, cy);
                        FlattenQuad(output, cx, cy, x1, y1, x, y, tol);
                        cx = x;
                        cy = y;
                        break;
                    }
                case PathVerb.CubicTo:
                    {
                        double x1 = m00 * coords[c] + m01 * coords[c + 1] + m02;
                        double y1 = m10 * coords[c] + m11 * coords[c + 1] + m12;
                        double x2 = m00 * coords[c + 2] + m01 * coords[c + 3] + m02;
                        double y2 = m10 * coords[c + 2] + m11 * coords[c + 3] + m12;
                        double x = m00 * coords[c + 4] + m01 * coords[c + 5] + m02;
                        double y = m10 * coords[c + 4] + m11 * coords[c + 5] + m12;
                        c += 6;
                        EnsureOpen(output, ref open, cx, cy);
                        FlattenCubic(output, cx, cy, x1, y1, x2, y2, x, y, tol);
                        cx = x;
                        cy = y;
                        break;
                    }
                case PathVerb.Close:
                    if (open)
                    {
                        output.Close();
                        open = false;
                    }
                    cx = sx;
                    cy = sy;
                    break;
            }
        }
        output.EndContour(closed: false);
    }

    // A drawing verb after Close continues from the subpath start (SVG semantics).
    private static void EnsureOpen(FlatPath output, ref bool open, double x, double y)
    {
        if (!open)
        {
            output.MoveTo((float)x, (float)y);
            open = true;
        }
    }

    /// <summary>Segment count for a quadratic so its chord deviation stays under <paramref name="tol"/>.</summary>
    public static int QuadSegments(double x0, double y0, double x1, double y1, double x2, double y2, double tol)
    {
        double ddx = x0 - 2 * x1 + x2;
        double ddy = y0 - 2 * y1 + y2;
        double m = Math.Sqrt(ddx * ddx + ddy * ddy);
        return Math.Clamp((int)Math.Ceiling(Math.Sqrt(0.25 * m / tol)), 1, 4096);
    }

    /// <summary>Segment count for a cubic so its chord deviation stays under <paramref name="tol"/>.</summary>
    public static int CubicSegments(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tol)
    {
        double ax = x0 - 2 * x1 + x2, ay = y0 - 2 * y1 + y2;
        double bx = x1 - 2 * x2 + x3, by = y1 - 2 * y2 + y3;
        double m = Math.Sqrt(Math.Max(ax * ax + ay * ay, bx * bx + by * by));
        return Math.Clamp((int)Math.Ceiling(Math.Sqrt(0.75 * m / tol)), 1, 4096);
    }

    private static void FlattenQuad(FlatPath output, double x0, double y0, double x1, double y1, double x2, double y2, float tol)
    {
        int n = QuadSegments(x0, y0, x1, y1, x2, y2, tol);
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            double mt = 1 - t;
            double x = mt * mt * x0 + 2 * mt * t * x1 + t * t * x2;
            double y = mt * mt * y0 + 2 * mt * t * y1 + t * t * y2;
            output.LineTo((float)x, (float)y);
        }
    }

    private static void FlattenCubic(FlatPath output, double x0, double y0, double x1, double y1,
        double x2, double y2, double x3, double y3, float tol)
    {
        int n = CubicSegments(x0, y0, x1, y1, x2, y2, x3, y3, tol);
        for (int i = 1; i <= n; i++)
        {
            double t = (double)i / n;
            double mt = 1 - t;
            double a = mt * mt * mt, b = 3 * mt * mt * t, c = 3 * mt * t * t, d = t * t * t;
            output.LineTo((float)(a * x0 + b * x1 + c * x2 + d * x3), (float)(a * y0 + b * y1 + c * y2 + d * y3));
        }
    }
}
