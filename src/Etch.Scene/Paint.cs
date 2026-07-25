using System.Runtime.InteropServices;

namespace Etch.Scene;

[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct Paint
{
    public readonly PaintKind Kind;
    public readonly byte BlendModeId;
    private readonly byte _align1, _align2;
    public readonly uint ColorOrGradientId;
    private readonly long _alignLong0, _alignLong1, _alignLong2;

    public Paint(PaintKind kind, uint colorOrGradientId, byte blendModeId = 0)
    {
        Kind = kind;
        BlendModeId = blendModeId;
        ColorOrGradientId = colorOrGradientId;
    }

    public static Paint Solid(uint argb, byte blendModeId = 0) => new Paint(PaintKind.Solid, argb, blendModeId);

    public static Paint LinearGradient(uint gradientId, byte blendModeId = 0) => new Paint(PaintKind.LinearGradient, gradientId, blendModeId);

    public static Paint RadialGradient(uint gradientId, byte blendModeId = 0) => new Paint(PaintKind.RadialGradient, gradientId, blendModeId);

    public static Paint MeshGradient(uint gradientId, byte blendModeId = 0) => new Paint(PaintKind.MeshGradient, gradientId, blendModeId);

    public static Paint Noise(uint noiseId, byte blendModeId = 0) => new Paint(PaintKind.Noise, noiseId, blendModeId);

    public uint Color => Kind == PaintKind.Solid ? ColorOrGradientId : 0;

    public uint GradientId => Kind >= PaintKind.LinearGradient ? ColorOrGradientId : 0;
}

public enum PaintKind : byte
{
    Solid = 0,
    LinearGradient = 1,
    RadialGradient = 2,
    Image = 3,
    MeshGradient = 4,
    Noise = 5,
}
