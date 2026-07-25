using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Etch.Raster.Cpu;

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct Rgba16f
{
    public readonly Half R, G, B, A;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Rgba16f(Half r, Half g, Half b, Half a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba16f From(float r, float g, float b, float a)
        => new((Half)r, (Half)g, (Half)b, (Half)a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Rgba16f FromLinearBytes(byte rByte, byte gByte, byte bByte, byte aByte)
    {
        float r = rByte / 255f;
        float g = gByte / 255f;
        float b = bByte / 255f;
        float a = aByte / 255f;
        return new Rgba16f((Half)r, (Half)g, (Half)b, (Half)a);
    }

    public static Rgba16f Zero => default;
}