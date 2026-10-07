using Etch.Scene;

namespace Etch.Compose.Coverage;

/// <summary>Stroke geometry: width, cap, join, miter limit and an optional dash pattern.</summary>
public readonly record struct StrokeParameters(float Width, StrokeCap Cap, StrokeJoin Join, float MiterLimit, float DashOn, float DashOff, float DashOffset)
{
    /// <summary>A solid stroke with the SVG default miter limit (4).</summary>
    public static StrokeParameters Solid(float width, StrokeCap cap = StrokeCap.Butt, StrokeJoin join = StrokeJoin.Miter)
        => new(width, cap, join, 4f, 0f, 0f, 0f);

    /// <summary>True when the stroke is dashed (an on and an off length greater than zero).</summary>
    public bool IsDashed => DashOn > 0f && DashOff > 0f;
}

/// <summary>
/// Turns flattened contours into the closed polygons covering their stroke: an outline per open
/// contour (left side forward, end cap, right side back, start cap) and an outer and a reversed
/// inner loop per closed contour. Filled with the non-zero rule, the pieces union exactly — the
/// overlapping inner corners of sharp turns add winding instead of leaving holes or double-blending.
/// </summary>
public sealed class PathStroker
{
    private readonly FlatPath dashed = new();

    /// <summary>
    /// Strokes every contour of <paramref name="input"/> (in its own space) into
    /// <paramref name="output"/> (cleared first). <paramref name="tolerance"/> bounds the error of
    /// round joins and caps, in input units.
    /// </summary>
    public void Stroke(FlatPath input, in StrokeParameters stroke, float tolerance, FlatPath output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        output.Clear();
        if (!(stroke.Width > 0f))
        {
            return;
        }

        FlatPath source = input;
        if (stroke.IsDashed)
        {
            Dash(input, stroke, dashed);
            source = dashed;
        }

        float hw = stroke.Width * 0.5f;
        float tol = Math.Max(tolerance, 1e-4f);
        var xs = source.X;
        var ys = source.Y;
        foreach (var contour in source.Contours)
        {
            var px = xs.Slice(contour.Start, contour.Count);
            var py = ys.Slice(contour.Start, contour.Count);
            if (IsDegenerate(px, py))
            {
                EmitDot(px[0], py[0], hw, stroke.Cap, tol, output);
                continue;
            }
            if (contour.Closed && contour.Count >= 3)
            {
                StrokeClosed(px, py, hw, stroke, tol, output);
            }
            else
            {
                StrokeOpen(px, py, hw, stroke, tol, output);
            }
        }
    }

    private static bool IsDegenerate(ReadOnlySpan<float> px, ReadOnlySpan<float> py)
    {
        for (int i = 1; i < px.Length; i++)
        {
            if (px[i] != px[0] || py[i] != py[0])
            {
                return false;
            }
        }
        return true;
    }

    // A zero-length contour draws only its caps: a disc for round, a square for square.
    private static void EmitDot(float x, float y, float hw, StrokeCap cap, float tol, FlatPath output)
    {
        if (cap == StrokeCap.Round)
        {
            output.MoveTo(x + hw, y);
            AppendArc(output, x, y, hw, 0f, 2f * MathF.PI, tol, includeStart: false);
            output.Close();
        }
        else if (cap == StrokeCap.Square)
        {
            output.MoveTo(x - hw, y - hw);
            output.LineTo(x + hw, y - hw);
            output.LineTo(x + hw, y + hw);
            output.LineTo(x - hw, y + hw);
            output.Close();
        }
    }

    private static void StrokeOpen(ReadOnlySpan<float> px, ReadOnlySpan<float> py, float hw, in StrokeParameters stroke, float tol, FlatPath output)
    {
        // Drop repeated points so every segment has a direction.
        Span<int> keep = px.Length <= 256 ? stackalloc int[px.Length] : new int[px.Length];
        int n = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (n == 0 || px[i] != px[keep[n - 1]] || py[i] != py[keep[n - 1]])
            {
                keep[n++] = i;
            }
        }

        Direction(px, py, keep[0], keep[1], out float t0x, out float t0y);
        Direction(px, py, keep[n - 2], keep[n - 1], out float tEx, out float tEy);
        float sx = px[keep[0]], sy = py[keep[0]];
        float ex = px[keep[n - 1]], ey = py[keep[n - 1]];

        // Left side, start to end.
        output.MoveTo(sx - t0y * hw, sy + t0x * hw);
        for (int k = 1; k < n - 1; k++)
        {
            Join(px, py, keep[k - 1], keep[k], keep[k + 1], hw, 1f, stroke, tol, output);
        }
        output.LineTo(ex - tEy * hw, ey + tEx * hw);

        // End cap: left offset to right offset around the end.
        Cap(ex, ey, tEx, tEy, hw, stroke.Cap, tol, output);

        // Right side, end to start.
        output.LineTo(ex + tEy * hw, ey - tEx * hw);
        for (int k = n - 2; k >= 1; k--)
        {
            Join(px, py, keep[k + 1], keep[k], keep[k - 1], hw, 1f, stroke, tol, output);
        }
        output.LineTo(sx + t0y * hw, sy - t0x * hw);

        // Start cap: right offset back to the left offset around the start.
        Cap(sx, sy, -t0x, -t0y, hw, stroke.Cap, tol, output);
        output.Close();
    }

    private static void StrokeClosed(ReadOnlySpan<float> px, ReadOnlySpan<float> py, float hw, in StrokeParameters stroke, float tol, FlatPath output)
    {
        int count = px.Length;
        Span<int> keep = count <= 256 ? stackalloc int[count] : new int[count];
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            if (n == 0 || px[i] != px[keep[n - 1]] || py[i] != py[keep[n - 1]])
            {
                keep[n++] = i;
            }
        }
        if (n > 1 && px[keep[n - 1]] == px[keep[0]] && py[keep[n - 1]] == py[keep[0]])
        {
            n--;
        }
        if (n < 2)
        {
            EmitDot(px[0], py[0], hw, stroke.Cap, tol, output);
            return;
        }

        // Left loop forward: a join at every vertex, including the first.
        bool started = false;
        for (int k = 0; k < n; k++)
        {
            int prev = keep[(k - 1 + n) % n];
            int curr = keep[k];
            int next = keep[(k + 1) % n];
            if (!started)
            {
                Direction(px, py, prev, curr, out float tx, out float ty);
                output.MoveTo(px[curr] - ty * hw, py[curr] + tx * hw);
                started = true;
            }
            Join(px, py, prev, curr, next, hw, 1f, stroke, tol, output);
        }
        output.Close();

        // Right loop traversed backwards: the same offsets on the other side, opposite winding,
        // so the hole the two loops enclose cancels to zero.
        started = false;
        for (int k = n - 1; k >= 0; k--)
        {
            int prev = keep[(k + 1) % n];
            int curr = keep[k];
            int next = keep[(k - 1 + n) % n];
            if (!started)
            {
                Direction(px, py, prev, curr, out float tx, out float ty);
                output.MoveTo(px[curr] - ty * hw, py[curr] + tx * hw);
                started = true;
            }
            Join(px, py, prev, curr, next, hw, 1f, stroke, tol, output);
        }
        output.Close();
    }

    private static void Direction(ReadOnlySpan<float> px, ReadOnlySpan<float> py, int from, int to, out float tx, out float ty)
    {
        float dx = px[to] - px[from];
        float dy = py[to] - py[from];
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0f)
        {
            tx = 1f;
            ty = 0f;
            return;
        }
        tx = dx / len;
        ty = dy / len;
    }

    /// <summary>
    /// Emits the left-side offset of the corner at <paramref name="curr"/> (travelling prev → curr
    /// → next): the end of the incoming offset, the corner geometry, the start of the outgoing one.
    /// </summary>
    private static void Join(ReadOnlySpan<float> px, ReadOnlySpan<float> py, int prev, int curr, int next,
        float hw, float side, in StrokeParameters stroke, float tol, FlatPath output)
    {
        Direction(px, py, prev, curr, out float tax, out float tay);
        Direction(px, py, curr, next, out float tbx, out float tby);
        float vx = px[curr], vy = py[curr];

        // Left normals (rotate the tangent by +90° in this coordinate system).
        float nax = -tay * side, nay = tax * side;
        float nbx = -tby * side, nby = tbx * side;
        float ax = vx + nax * hw, ay = vy + nay * hw;
        float bx = vx + nbx * hw, by = vy + nby * hw;

        float turn = tbx * nax + tby * nay; // > 0: the path turns toward this side → inner corner
        float dot = tax * tbx + tay * tby;
        if (MathF.Abs(turn) < 1e-6f && dot > 0f)
        {
            output.LineTo(ax, ay);
            return;
        }

        if (turn > 0f)
        {
            // Inner corner: route through the vertex. The overlap this creates is unioned by the
            // non-zero fill.
            output.LineTo(ax, ay);
            output.LineTo(vx, vy);
            output.LineTo(bx, by);
            return;
        }

        switch (stroke.Join)
        {
            case StrokeJoin.Miter:
                {
                    float mx = nax + nbx, my = nay + nby;
                    float mlen = MathF.Sqrt(mx * mx + my * my);
                    if (mlen > 1e-6f)
                    {
                        mx /= mlen;
                        my /= mlen;
                        float cosHalf = mx * nax + my * nay;
                        float ratio = cosHalf > 1e-6f ? 1f / cosHalf : float.MaxValue;
                        if (ratio <= stroke.MiterLimit)
                        {
                            output.LineTo(ax, ay);
                            output.LineTo(vx + mx * hw * ratio, vy + my * hw * ratio);
                            output.LineTo(bx, by);
                            return;
                        }
                    }
                    output.LineTo(ax, ay);
                    output.LineTo(bx, by);
                    return;
                }
            case StrokeJoin.Round:
                {
                    float a0 = MathF.Atan2(nay, nax);
                    float a1 = MathF.Atan2(nby, nbx);
                    float sweep = a1 - a0;
                    // The outer arc goes the short way round, on the side away from the turn.
                    while (sweep > MathF.PI)
                    {
                        sweep -= 2f * MathF.PI;
                    }
                    while (sweep < -MathF.PI)
                    {
                        sweep += 2f * MathF.PI;
                    }
                    output.LineTo(ax, ay);
                    AppendArc(output, vx, vy, hw, a0, sweep, tol, includeStart: false);
                    return;
                }
            default:
                output.LineTo(ax, ay);
                output.LineTo(bx, by);
                return;
        }
    }

    /// <summary>
    /// Emits the cap at (<paramref name="x"/>, <paramref name="y"/>) for travel direction
    /// (<paramref name="tx"/>, <paramref name="ty"/>), from the left offset to the right offset.
    /// </summary>
    private static void Cap(float x, float y, float tx, float ty, float hw, StrokeCap cap, float tol, FlatPath output)
    {
        float nx = -ty, ny = tx;
        switch (cap)
        {
            case StrokeCap.Square:
                output.LineTo(x + (nx + tx) * hw, y + (ny + ty) * hw);
                output.LineTo(x + (-nx + tx) * hw, y + (-ny + ty) * hw);
                break;
            case StrokeCap.Round:
                {
                    float a0 = MathF.Atan2(ny, nx);
                    // From the left normal, through the travel direction, to the right normal.
                    float sweep = (nx * ty - ny * tx) > 0f ? MathF.PI : -MathF.PI;
                    AppendArc(output, x, y, hw, a0, sweep, tol, includeStart: false);
                    break;
                }
        }
    }

    /// <summary>Appends points of the arc of radius <paramref name="r"/> about (cx, cy).</summary>
    private static void AppendArc(FlatPath output, float cx, float cy, float r, float startAngle, float sweep, float tol, bool includeStart)
    {
        float step = r > tol ? 2f * MathF.Acos(1f - tol / r) : MathF.PI / 2f;
        int segments = Math.Clamp((int)MathF.Ceiling(MathF.Abs(sweep) / Math.Max(step, 1e-3f)), 1, 1024);
        for (int i = includeStart ? 0 : 1; i <= segments; i++)
        {
            float a = startAngle + sweep * i / segments;
            output.LineTo(cx + r * MathF.Cos(a), cy + r * MathF.Sin(a));
        }
    }

    // Splits every contour into its "on" intervals (closed contours dash through their closing
    // segment), as open contours.
    private static void Dash(FlatPath input, in StrokeParameters stroke, FlatPath output)
    {
        output.Clear();
        float period = stroke.DashOn + stroke.DashOff;
        var xs = input.X;
        var ys = input.Y;
        foreach (var contour in input.Contours)
        {
            float phase = stroke.DashOffset % period;
            if (phase < 0f)
            {
                phase += period;
            }
            bool on = phase < stroke.DashOn;
            float remaining = on ? stroke.DashOn - phase : period - phase;
            int segmentCount = contour.Closed ? contour.Count : contour.Count - 1;
            if (on)
            {
                output.MoveTo(xs[contour.Start], ys[contour.Start]);
            }
            for (int s = 0; s < segmentCount; s++)
            {
                int i0 = contour.Start + s;
                int i1 = contour.Start + (s + 1) % contour.Count;
                float x0 = xs[i0], y0 = ys[i0];
                float dx = xs[i1] - x0, dy = ys[i1] - y0;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                float t = 0f;
                while (len - t > remaining)
                {
                    t += remaining;
                    float x = x0 + dx * (t / len), y = y0 + dy * (t / len);
                    if (on)
                    {
                        output.LineTo(x, y);
                        output.EndContour(closed: false);
                    }
                    else
                    {
                        output.MoveTo(x, y);
                    }
                    on = !on;
                    remaining = on ? stroke.DashOn : stroke.DashOff;
                }
                remaining -= len - t;
                if (on)
                {
                    output.LineTo(xs[i1], ys[i1]);
                }
            }
            output.EndContour(closed: false);
        }
    }
}
