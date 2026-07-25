using System;
using System.Runtime.InteropServices;

#pragma warning disable CA1062

namespace Etch.Scene;

[StructLayout(LayoutKind.Sequential, Size = 136)]
public struct GradientStops
{
    public int Count;
    private float _s0, _s1, _s2, _s3, _s4, _s5, _s6, _s7, _s8, _s9, _s10, _s11, _s12, _s13, _s14, _s15;
    private uint _c0, _c1, _c2, _c3, _c4, _c5, _c6, _c7, _c8, _c9, _c10, _c11, _c12, _c13, _c14, _c15;

    public static GradientStops Create(params (float offset, uint argb)[]? stops)
    {
        if (stops == null || stops.Length > 16)
            Etch.Panic.Invariant(Etch.PanicCodes.ArgumentOutOfRange, "stops is null or exceeds 16");
        if (stops.Length < 2)
            Etch.Panic.Invariant(Etch.PanicCodes.BadGradient, "Gradient must have at least 2 stops");

        var result = new GradientStops { Count = stops.Length };
        for (int i = 0; i < stops.Length; i++)
            result.SetStop(i, stops[i].offset, stops[i].argb);
        return result;
    }

    public (float offset, uint argb) GetStop(int index)
    {
        return index switch
        {
            0 => (_s0, _c0),
            1 => (_s1, _c1),
            2 => (_s2, _c2),
            3 => (_s3, _c3),
            4 => (_s4, _c4),
            5 => (_s5, _c5),
            6 => (_s6, _c6),
            7 => (_s7, _c7),
            8 => (_s8, _c8),
            9 => (_s9, _c9),
            10 => (_s10, _c10),
            11 => (_s11, _c11),
            12 => (_s12, _c12),
            13 => (_s13, _c13),
            14 => (_s14, _c14),
            15 => (_s15, _c15),
            _ => (0f, 0u),
        };
    }

    internal void SetStop(int index, float offset, uint argb)
    {
        switch (index)
        {
            case 0: _s0 = offset; _c0 = argb; break;
            case 1: _s1 = offset; _c1 = argb; break;
            case 2: _s2 = offset; _c2 = argb; break;
            case 3: _s3 = offset; _c3 = argb; break;
            case 4: _s4 = offset; _c4 = argb; break;
            case 5: _s5 = offset; _c5 = argb; break;
            case 6: _s6 = offset; _c6 = argb; break;
            case 7: _s7 = offset; _c7 = argb; break;
            case 8: _s8 = offset; _c8 = argb; break;
            case 9: _s9 = offset; _c9 = argb; break;
            case 10: _s10 = offset; _c10 = argb; break;
            case 11: _s11 = offset; _c11 = argb; break;
            case 12: _s12 = offset; _c12 = argb; break;
            case 13: _s13 = offset; _c13 = argb; break;
            case 14: _s14 = offset; _c14 = argb; break;
            case 15: _s15 = offset; _c15 = argb; break;
        }
    }
}
