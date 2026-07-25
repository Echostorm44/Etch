using System.Runtime.InteropServices;

namespace Etch.Scene;

[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct RgbaFloat
{
    public readonly float R;
    public readonly float G;
    public readonly float B;
    public readonly float A;

    public RgbaFloat(float r, float g, float b, float a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }
}
