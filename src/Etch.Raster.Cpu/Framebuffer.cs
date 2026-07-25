using System;
using System.Runtime.CompilerServices;
using Etch;

namespace Etch.Raster.Cpu;

public readonly struct Framebuffer
{
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public Memory<Rgba16f> Pixels { get; }

    private readonly Rgba16f[]? _array;

    public Framebuffer(int width, int height, int stride, Memory<Rgba16f> pixels)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
        _array = null;
    }

    public Framebuffer(int width, int height, int stride, Rgba16f[] array)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Pixels = array;
        _array = array;
    }

    internal Rgba16f[]? Array => _array;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<Rgba16f> RowSpan(int y)
    {
        if ((uint)y >= (uint)Height)
        {
            Panic.ArgumentOutOfRange(nameof(y));
        }

        return Pixels.Span.Slice(y * Stride, Width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ComputeStride(int width) => width;
}