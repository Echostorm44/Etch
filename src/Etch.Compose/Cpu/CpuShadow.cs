using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Etch.Compose.Cpu;

/// <summary>
/// Box-shadow coverage for a whole row of an axis-aligned shadow. The shadow integral's per-row
/// terms (sample offsets, Gaussian weights, curved half-widths) are computed once, and the per-pixel
/// error-function work runs 8 or 4 pixels at a time. Every value is the one
/// <see cref="CpuShading.ShapeCoverage"/> computes — the same operations in the same order, which
/// IEEE single precision rounds identically in vector and scalar form.
/// </summary>
internal static class CpuShadow
{
    private const float C1 = 0.278393f;
    private const float C2 = 0.230389f;
    private const float C3 = 0.078108f;

    /// <summary>
    /// Writes the coverage of pixels <paramref name="x0"/> … <paramref name="x0"/> + count − 1 on row
    /// <paramref name="y"/> into <paramref name="output"/>. <paramref name="inst"/> must be an
    /// axis-aligned (<see cref="CpuSpans.IsAxisAligned"/>) shadow.
    /// </summary>
    public static void Row(in ShapeInstance inst, int y, int x0, Span<float> output)
        => Row(inst, y, x0, output, Vector256.IsHardwareAccelerated ? 8 : Vector128.IsHardwareAccelerated ? 4 : 1);

    /// <summary>
    /// <see cref="Row(in ShapeInstance, int, int, Span{float})"/> with an explicit vector width (8, 4
    /// or 1 for scalar), so tests can run every path on any machine.
    /// </summary>
    internal static void Row(in ShapeInstance inst, int y, int x0, Span<float> output, int vectorWidth)
    {
        float loX = inst.P0, loY = inst.P1, hiX = inst.P2, hiY = inst.P3;
        float sigma = inst.Q1;
        float cx = (loX + hiX) * 0.5f;
        float cy = (loY + hiY) * 0.5f;
        float halfX = (hiX - loX) * 0.5f;
        float halfY = (hiY - loY) * 0.5f;
        float corner = MathF.Min(inst.Q0, MathF.Min(halfX, halfY));
        float py = y + 0.5f;
        float ly = inst.FrameC * (x0 + 0.5f) + inst.FrameD * py + inst.FrameTy;
        float yc = ly - cy;
        float low = yc - halfY;
        float high = yc + halfY;
        float start = Math.Clamp(-3f * sigma, low, high);
        float end = Math.Clamp(3f * sigma, low, high);
        float step = (end - start) / 4f;
        float t = start + step * 0.5f;
        Span<float> g = stackalloc float[4];
        Span<float> curved = stackalloc float[4];
        for (int i = 0; i < 4; i++)
        {
            g[i] = MathF.Exp(-(t * t) / (2f * sigma * sigma)) / (2.50662827f * sigma);
            float delta = MathF.Min(halfY - corner - MathF.Abs(yc - t), 0f);
            curved[i] = halfX - corner + MathF.Sqrt(MathF.Max(0f, corner * corner - delta * delta));
            t += step;
        }
        float k = 0.70710678f / sigma;
        float rowTerm = inst.FrameB * py;

        int x = 0;
        if (vectorWidth == 8)
        {
            var lane = Vector256.Create(0.5f, 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f, 7.5f);
            for (; x + 8 <= output.Length; x += 8)
            {
                var px = Vector256.Create((float)(x0 + x)) + lane;
                var xs = (Vector256.Create(inst.FrameA) * px + Vector256.Create(rowTerm)) + Vector256.Create(inst.FrameTx) - Vector256.Create(cx);
                var value = Vector256<float>.Zero;
                for (int i = 0; i < 4; i++)
                {
                    var c = Vector256.Create(curved[i]);
                    var kk = Vector256.Create(k);
                    var e0 = Erf((xs - c) * kk);
                    var e1 = Erf((xs + c) * kk);
                    var half = Vector256.Create(0.5f);
                    var sx = (half + half * e1) - (half + half * e0);
                    value += sx * Vector256.Create(g[i]) * Vector256.Create(step);
                }
                value = Vector256.Min(Vector256.Max(value, Vector256<float>.Zero), Vector256<float>.One);
                value.CopyTo(output.Slice(x, 8));
            }
        }
        else if (vectorWidth == 4)
        {
            var lane = Vector128.Create(0.5f, 1.5f, 2.5f, 3.5f);
            for (; x + 4 <= output.Length; x += 4)
            {
                var px = Vector128.Create((float)(x0 + x)) + lane;
                var xs = (Vector128.Create(inst.FrameA) * px + Vector128.Create(rowTerm)) + Vector128.Create(inst.FrameTx) - Vector128.Create(cx);
                var value = Vector128<float>.Zero;
                for (int i = 0; i < 4; i++)
                {
                    var c = Vector128.Create(curved[i]);
                    var kk = Vector128.Create(k);
                    var e0 = Erf((xs - c) * kk);
                    var e1 = Erf((xs + c) * kk);
                    var half = Vector128.Create(0.5f);
                    var sx = (half + half * e1) - (half + half * e0);
                    value += sx * Vector128.Create(g[i]) * Vector128.Create(step);
                }
                value = Vector128.Min(Vector128.Max(value, Vector128<float>.Zero), Vector128<float>.One);
                value.CopyTo(output.Slice(x, 4));
            }
        }
        for (; x < output.Length; x++)
        {
            float px = x0 + x + 0.5f;
            float xs = inst.FrameA * px + rowTerm + inst.FrameTx - cx;
            float value = 0f;
            for (int i = 0; i < 4; i++)
            {
                float e0 = Erf((xs - curved[i]) * k);
                float e1 = Erf((xs + curved[i]) * k);
                float sx = (0.5f + 0.5f * e1) - (0.5f + 0.5f * e0);
                value += sx * g[i] * step;
            }
            output[x] = Math.Clamp(value, 0f, 1f);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Erf(float x)
    {
        float s = MathF.Sign(x);
        float a = MathF.Abs(x);
        float r = 1f + (C1 + (C2 + C3 * (a * a)) * a) * a;
        r *= r;
        return s - s / (r * r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Erf(Vector256<float> x)
    {
        var zero = Vector256<float>.Zero;
        var one = Vector256<float>.One;
        var s = Vector256.ConditionalSelect(Vector256.GreaterThan(x, zero), one,
            Vector256.ConditionalSelect(Vector256.LessThan(x, zero), -one, zero));
        var a = Vector256.Abs(x);
        var r = one + (Vector256.Create(C1) + (Vector256.Create(C2) + Vector256.Create(C3) * (a * a)) * a) * a;
        r *= r;
        return s - s / (r * r);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Erf(Vector128<float> x)
    {
        var zero = Vector128<float>.Zero;
        var one = Vector128<float>.One;
        var s = Vector128.ConditionalSelect(Vector128.GreaterThan(x, zero), one,
            Vector128.ConditionalSelect(Vector128.LessThan(x, zero), -one, zero));
        var a = Vector128.Abs(x);
        var r = one + (Vector128.Create(C1) + (Vector128.Create(C2) + Vector128.Create(C3) * (a * a)) * a) * a;
        r *= r;
        return s - s / (r * r);
    }
}
