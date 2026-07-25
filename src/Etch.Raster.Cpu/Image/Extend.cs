using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu.Image;

public enum ImageExtendMode : uint
{
    Pad = 0u,
    Repeat = 1u,
    Mirror = 2u,
}

public static class Extend
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int coord, int size)
    {
        return coord < 0 ? 0 : coord >= size ? size - 1 : coord;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Repeat(int coord, int size)
    {
        int c = coord % size;
        return c < 0 ? c + size : c;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Mirror(int coord, int size)
    {
        int n = size * 2;
        int c = ((coord % n) + n) % n;
        return c < size ? c : (n - 1 - c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SampleCoord(int coord, int size, ImageExtendMode mode)
    {
        return mode switch
        {
            ImageExtendMode.Pad => Clamp(coord, size),
            ImageExtendMode.Repeat => Repeat(coord, size),
            ImageExtendMode.Mirror => Mirror(coord, size),
            _ => Clamp(coord, size)
        };
    }
}
