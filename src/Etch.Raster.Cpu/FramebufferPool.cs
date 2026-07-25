using System;
using System.Buffers;
using System.Collections.Generic;

namespace Etch.Raster.Cpu;

public static class FramebufferPool
{
    private const int MaxRetained = 4;

    private static readonly Dictionary<(int Width, int Height), PoolEntry> _pool = new();
    private static int _poolCount;

    private struct PoolEntry
    {
        public Rgba16f[] Buffer;
    }

    public static Framebuffer Rent(int width, int height)
    {
        int stride = width;
        int needed = height * stride;

        lock (_pool)
        {
            if (_pool.TryGetValue((width, height), out var entry) && entry.Buffer.Length >= needed)
            {
                var pooledBuffer = entry.Buffer;
                _pool.Remove((width, height));
                _poolCount--;
                return new Framebuffer(width, height, stride, pooledBuffer);
            }
        }

        var buffer = ArrayPool<Rgba16f>.Shared.Rent(needed);
        return new Framebuffer(width, height, stride, buffer);
    }

    public static void Return(ref Framebuffer framebuffer)
    {
        if (framebuffer.Pixels.IsEmpty)
        {
            return;
        }

        int needed = framebuffer.Height * framebuffer.Stride;
        var array = framebuffer.Array;

        lock (_pool)
        {
            if (_pool.TryGetValue((framebuffer.Width, framebuffer.Height), out var existing))
            {
                ArrayPool<Rgba16f>.Shared.Return(existing.Buffer);
                _pool.Remove((framebuffer.Width, framebuffer.Height));
                _poolCount--;
            }

            if (_poolCount < MaxRetained && needed <= framebuffer.Pixels.Length)
            {
                var bufferCopy = ArrayPool<Rgba16f>.Shared.Rent(needed);
                framebuffer.Pixels.Span.CopyTo(bufferCopy.AsSpan());

                _pool[(framebuffer.Width, framebuffer.Height)] = new PoolEntry { Buffer = bufferCopy };
                _poolCount++;
            }

            if (array != null)
            {
                ArrayPool<Rgba16f>.Shared.Return(array);
            }
        }

        framebuffer = default;
    }
}