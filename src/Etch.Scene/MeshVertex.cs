using System.Runtime.InteropServices;
using Etch.Geometry;

namespace Etch.Scene;

[StructLayout(LayoutKind.Sequential, Size = 80)]
public readonly struct MeshVertex
{
    public readonly RgbaFloat Color;
    public readonly Vec2 DuIn;
    public readonly Vec2 DuOut;
    public readonly Vec2 DvIn;
    public readonly Vec2 DvOut;

    public MeshVertex(RgbaFloat color, Vec2 duIn, Vec2 duOut, Vec2 dvIn, Vec2 dvOut)
    {
        Color = color;
        DuIn = duIn;
        DuOut = duOut;
        DvIn = dvIn;
        DvOut = dvOut;
    }

    public MeshVertex(RgbaFloat color)
    {
        Color = color;
        DuIn = default;
        DuOut = default;
        DvIn = default;
        DvOut = default;
    }
}
