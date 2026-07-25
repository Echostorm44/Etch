using System.Runtime.InteropServices;

namespace Etch.Scene;

[StructLayout(LayoutKind.Sequential, Size = 24)]
public readonly struct NoiseSpec
{
    public readonly float Scale;
    public readonly int Octaves;
    public readonly float Persistence;
    public readonly uint Seed;
    public readonly float Opacity;

    public NoiseSpec(float scale, int octaves, float persistence, uint seed, float opacity)
    {
        if (octaves < 1 || octaves > 8)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateNoise, $"Noise octaves must be 1–8, got {octaves}");
        if (scale <= 0f)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateNoise, $"Noise scale must be positive, got {scale}");
        if (opacity <= 0f || opacity > 1f)
            Etch.Panic.Invariant(Etch.PanicCodes.DegenerateNoise, $"Noise opacity must be in (0, 1], got {opacity}");

        Scale = scale;
        Octaves = octaves;
        Persistence = persistence;
        Seed = seed;
        Opacity = opacity;
    }
}
