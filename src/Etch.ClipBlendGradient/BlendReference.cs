using System;

namespace Etch.ClipBlendGradient;

public static partial class BlendReference
{
    public static LinearColor Apply(LinearColor src, LinearColor dst, BlendMode mode)
    {
        return mode switch
        {
            BlendMode.Normal => NormalBlend(src, dst),
            BlendMode.Multiply => MultiplyBlend(src, dst),
            BlendMode.Screen => ScreenBlend(src, dst),
            BlendMode.Overlay => OverlayBlend(src, dst),
            BlendMode.Darken => DarkenBlend(src, dst),
            BlendMode.Lighten => LightenBlend(src, dst),
            BlendMode.ColorDodge => ColorDodgeBlend(src, dst),
            BlendMode.ColorBurn => ColorBurnBlend(src, dst),
            BlendMode.HardLight => HardLightBlend(src, dst),
            BlendMode.SoftLight => SoftLightBlend(src, dst),
            BlendMode.Difference => DifferenceBlend(src, dst),
            BlendMode.Exclusion => ExclusionBlend(src, dst),
            BlendMode.Hue => HueBlend(src, dst),
            BlendMode.Saturation => SaturationBlend(src, dst),
            BlendMode.Color => ColorBlend(src, dst),
            BlendMode.Luminosity => LuminosityBlend(src, dst),
            _ => dst,
        };
    }

    private static LinearColor NormalBlend(LinearColor src, LinearColor dst)
    {
        return SrcOverCompositing(src, dst);
    }

    private static LinearColor MultiplyBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => s * d);
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor ScreenBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => 1.0 - (1.0 - s) * (1.0 - d));
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor OverlayBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => d < 0.5 ? 2.0 * s * d : 1.0 - 2.0 * (1.0 - s) * (1.0 - d));
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor DarkenBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, Math.Min);
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor LightenBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, Math.Max);
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor ColorDodgeBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => s >= 1.0 ? 1.0 : (d / (1.0 - s)).Clamp(0.0, 1.0));
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor ColorBurnBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => s <= 0.0 ? 0.0 : (1.0 - (1.0 - d) / s).Clamp(0.0, 1.0));
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor HardLightBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => s < 0.5 ? 2.0 * s * d : 1.0 - 2.0 * (1.0 - s) * (1.0 - d));
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor SoftLightBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, SoftLightChannel);
        return SrcOverCompositing(blend, dst);
    }

    private static double SoftLightChannel(double s, double d)
    {
        if (d < 0.25)
            return ((16.0 * d - 12.0) * d + 4.0) * d * s;
        if (d < 0.5)
            return s - (1.0 - 2.0 * d) * s * (1.0 - s);
        return s + (2.0 * d - 1.0) * (D(s) - s);
    }

    private static double D(double x) => x <= 0.25 ? ((16.0 * x - 12.0) * x + 4.0) * x : Math.Sqrt(x);

    private static LinearColor DifferenceBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => Math.Abs(s - d));
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor ExclusionBlend(LinearColor src, LinearColor dst)
    {
        var blend = BlendChannels(src, dst, (s, d) => s + d - 2.0 * s * d);
        return SrcOverCompositing(blend, dst);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Non-separable blend modes — W3C Compositing and Blending Level 1 §9.17
    // Uses Lum/Sat/SetLum/SetSat/ClipColor (NOT HSL).
    // ═══════════════════════════════════════════════════════════════════════════

    private static double Lum(double r, double g, double b)
        => 0.30 * r + 0.59 * g + 0.11 * b;

    private static double Sat(double r, double g, double b)
        => Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

    private static (double r, double g, double b) ClipColor(double r, double g, double b)
    {
        double l = Lum(r, g, b);
        double n = Math.Min(r, Math.Min(g, b));
        double x = Math.Max(r, Math.Max(g, b));

        if (n < 0.0)
        {
            double denom = l - n;
            if (denom > 0.0)
            {
                r = l + (r - l) * l / denom;
                g = l + (g - l) * l / denom;
                b = l + (b - l) * l / denom;
            }
        }

        if (x > 1.0)
        {
            double denom = x - l;
            if (denom > 0.0)
            {
                r = l + (r - l) * (1.0 - l) / denom;
                g = l + (g - l) * (1.0 - l) / denom;
                b = l + (b - l) * (1.0 - l) / denom;
            }
        }

        return (r, g, b);
    }

    private static (double r, double g, double b) SetLum(double r, double g, double b, double l)
    {
        double d = l - Lum(r, g, b);
        return ClipColor(r + d, g + d, b + d);
    }

    private static (double r, double g, double b) SetSat(double r, double g, double b, double s)
    {
        // Sort channels to identify min, mid, max
        double c0 = r, c1 = g, c2 = b;
        int i0 = 0, i1 = 1, i2 = 2;

        // Simple three-element sort to get min <= mid <= max
        if (c0 > c1) { (c0, c1) = (c1, c0); (i0, i1) = (i1, i0); }
        if (c1 > c2) { (c1, c2) = (c2, c1); (i1, i2) = (i2, i1); }
        if (c0 > c1) { (c0, c1) = (c1, c0); (i0, i1) = (i1, i0); }

        double min = c0, mid = c1, max = c2;

        if (max > min)
        {
            mid = ((mid - min) * s) / (max - min);
            max = s;
            min = 0.0;
        }
        else
        {
            mid = max = min = 0.0;
        }

        // Map back to original channel positions
        double[] arr = new double[3];
        arr[i0] = min;
        arr[i1] = mid;
        arr[i2] = max;
        return (arr[0], arr[1], arr[2]);
    }

    private static LinearColor HueBlend(LinearColor src, LinearColor dst)
    {
        // Hue: source hue + backdrop saturation + backdrop luminosity
        // B(Cb, Cs) = SetLum(SetSat(Cs, Sat(Cb)), Lum(Cb))
        var sat = SetSat(src.R, src.G, src.B, Sat(dst.R, dst.G, dst.B));
        var lum = SetLum(sat.r, sat.g, sat.b, Lum(dst.R, dst.G, dst.B));
        var blend = new LinearColor(lum.r, lum.g, lum.b, src.A);
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor SaturationBlend(LinearColor src, LinearColor dst)
    {
        // Saturation: backdrop hue + source saturation + backdrop luminosity
        // B(Cb, Cs) = SetLum(SetSat(Cb, Sat(Cs)), Lum(Cb))
        var sat = SetSat(dst.R, dst.G, dst.B, Sat(src.R, src.G, src.B));
        var lum = SetLum(sat.r, sat.g, sat.b, Lum(dst.R, dst.G, dst.B));
        var blend = new LinearColor(lum.r, lum.g, lum.b, src.A);
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor ColorBlend(LinearColor src, LinearColor dst)
    {
        // Color: source hue + source saturation + backdrop luminosity
        var lum = SetLum(src.R, src.G, src.B, Lum(dst.R, dst.G, dst.B));
        var blend = new LinearColor(lum.r, lum.g, lum.b, src.A);
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor LuminosityBlend(LinearColor src, LinearColor dst)
    {
        // Luminosity: backdrop hue + backdrop saturation + source luminosity
        var lum = SetLum(dst.R, dst.G, dst.B, Lum(src.R, src.G, src.B));
        var blend = new LinearColor(lum.r, lum.g, lum.b, src.A);
        return SrcOverCompositing(blend, dst);
    }

    private static LinearColor BlendChannels(LinearColor src, LinearColor dst, Func<double, double, double> op)
    {
        double r = op(src.R, dst.R);
        double g = op(src.G, dst.G);
        double b = op(src.B, dst.B);
        return new LinearColor(r, g, b, src.A);
    }

    private static LinearColor SrcOverCompositing(LinearColor src, LinearColor dst)
    {
        double srcA = src.A;
        double dstA = dst.A;
        double resultA = srcA + dstA * (1.0 - srcA);
        if (resultA < 0.0001)
            return new LinearColor(0, 0, 0, 0);
        double resultR = (src.R * srcA + dst.R * dstA * (1.0 - srcA)) / resultA;
        double resultG = (src.G * srcA + dst.G * dstA * (1.0 - srcA)) / resultA;
        double resultB = (src.B * srcA + dst.B * dstA * (1.0 - srcA)) / resultA;
        return new LinearColor(resultR, resultG, resultB, resultA);
    }

    private static double Clamp(this double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}

public readonly struct LinearColor
{
    public readonly double R, G, B, A;

    public LinearColor(double r, double g, double b, double a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static LinearColor FromRgba32(uint rgba)
    {
        double r = ((rgba >> 24) & 0xFF) / 255.0;
        double g = ((rgba >> 16) & 0xFF) / 255.0;
        double b = ((rgba >> 8) & 0xFF) / 255.0;
        double a = (rgba & 0xFF) / 255.0;
        return new LinearColor(r, g, b, a);
    }
}
