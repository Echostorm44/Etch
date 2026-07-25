using System;
using System.Runtime.CompilerServices;
using Etch.Scene;

namespace Etch.Raster.Cpu.Gradients;

public static class MeshGradientSampler
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static RgbaFloat Sample(MeshGradient mesh, float u, float v)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        u = Math.Clamp(u, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);

        int rows = mesh.Rows;
        int cols = mesh.Cols;
        int lastRow = rows - 1;
        int lastCol = cols - 1;

        float ru = u * lastCol;
        float rv = v * lastRow;

        int col0 = Math.Min((int)ru, lastCol - 1);
        int row0 = Math.Min((int)rv, lastRow - 1);
        int col1 = col0 + 1;
        int row1 = row0 + 1;

        float lu = ru - col0;
        float lv = rv - row0;

        var v00 = mesh.GetVertex(row0, col0);
        var v10 = mesh.GetVertex(row1, col0);
        var v01 = mesh.GetVertex(row0, col1);
        var v11 = mesh.GetVertex(row1, col1);

        var top = HermiteInterpU(v00, v01, lu);
        var bot = HermiteInterpU(v10, v11, lu);

        var topV = new HermiteSpan(top, MixedTangent(v00.DvOut, v01.DvOut));
        var botV = new HermiteSpan(bot, MixedTangent(v10.DvIn, v11.DvIn));

        return HermiteInterpV(topV, botV, lv);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RgbaFloat HermiteInterpU(MeshVertex start, MeshVertex end, float t)
    {
        var delta = Subtract(end.Color, start.Color);
        var tangent0 = ScaleColor(delta, TangentScalar(start.DuOut));
        var tangent1 = ScaleColor(delta, TangentScalar(end.DuIn));
        return HermiteScalar(start.Color, end.Color, tangent0, tangent1, t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RgbaFloat HermiteInterpV(HermiteSpan start, HermiteSpan end, float t)
    {
        var delta = Subtract(end.Color, start.Color);
        return HermiteScalar(start.Color, end.Color, start.Tangent, end.Tangent, t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RgbaFloat HermiteScalar(RgbaFloat p0, RgbaFloat p1, RgbaFloat m0, RgbaFloat m1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return new RgbaFloat(
            Clamp(h00 * p0.R + h10 * m0.R + h01 * p1.R + h11 * m1.R),
            Clamp(h00 * p0.G + h10 * m0.G + h01 * p1.G + h11 * m1.G),
            Clamp(h00 * p0.B + h10 * m0.B + h01 * p1.B + h11 * m1.B),
            Clamp(h00 * p0.A + h10 * m0.A + h01 * p1.A + h11 * m1.A));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float TangentScalar(Geometry.Vec2 t)
    {
        return (float)((t.X + t.Y) * 0.5);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RgbaFloat MixedTangent(Geometry.Vec2 a, Geometry.Vec2 b)
    {
        return ScaleColor(default, TangentScalar(new Geometry.Vec2(
            (a.X + b.X) * 0.5,
            (a.Y + b.Y) * 0.5)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RgbaFloat ScaleColor(RgbaFloat c, float s)
    {
        return new RgbaFloat(c.R * s, c.G * s, c.B * s, c.A * s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static RgbaFloat Subtract(RgbaFloat a, RgbaFloat b)
    {
        return new RgbaFloat(a.R - b.R, a.G - b.G, a.B - b.B, a.A - b.A);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Clamp(float v)
    {
        return v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    private readonly struct HermiteSpan
    {
        public readonly RgbaFloat Color;
        public readonly RgbaFloat Tangent;

        public HermiteSpan(RgbaFloat color, RgbaFloat tangent)
        {
            Color = color;
            Tangent = tangent;
        }
    }
}
