using System.Runtime.InteropServices;

namespace Etch.ClipBlendGradient;

[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly struct Rgba16f
{
    public readonly Half R, G, B, A;

    public Rgba16f(Half r, Half g, Half b, Half a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public static Rgba16f From(float r, float g, float b, float a)
        => new((Half)r, (Half)g, (Half)b, (Half)a);

    public static Rgba16f FromLinearBytes(byte rByte, byte gByte, byte bByte, byte aByte)
    {
        float r = rByte / 255f;
        float g = gByte / 255f;
        float b = bByte / 255f;
        float a = aByte / 255f;
        return new Rgba16f((Half)r, (Half)g, (Half)b, (Half)a);
    }

    public float RLinear => (float)R;
    public float GLinear => (float)G;
    public float BLinear => (float)B;
    public float ALinear => (float)A;

    public Rgba16f WithR(float r) => new((Half)r, G, B, A);
    public Rgba16f WithG(float g) => new(R, (Half)g, B, A);
    public Rgba16f WithB(float b) => new(R, G, (Half)b, A);
    public Rgba16f WithA(float a) => new(R, G, B, (Half)a);

    public static Rgba16f Zero => default;
}
