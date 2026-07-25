using System.Runtime.InteropServices;

namespace Etch.ClipBlendGradient;

[StructLayout(LayoutKind.Sequential, Size = 12)]
public readonly struct GradientStop
{
    public readonly float Position;
    public readonly Rgba16f Color;

    public GradientStop(float position, Rgba16f color)
    {
        Position = position;
        Color = color;
    }

    public GradientStop(float position, float r, float g, float b, float a)
    {
        Position = position;
        Color = Rgba16f.From(r, g, b, a);
    }
}
