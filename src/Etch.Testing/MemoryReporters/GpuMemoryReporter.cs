using System;

namespace Etch.Testing.MemoryReporters;

public static class GpuMemoryReporter
{
    // GPU copies texture rows into a buffer with 256-byte row alignment (WebGPU requirement).
    private const uint RowAlignment = 256u;

    // The per-draw uniform buffer (PerDrawData) the render path allocates.
    private const long UniformBufferBytes = 48;

    /// <summary>
    /// Estimates the GPU memory the offscreen render path allocates for a WxH render:
    /// the Rgba8 render-target texture, the 256-byte-row-aligned mappable readback buffer,
    /// and the per-draw uniform buffer. This is an analytical estimate of the buffer sizes
    /// (deterministic and GPU-independent) rather than a process-working-set delta, which is
    /// dominated by JIT/GC/driver noise and is not a meaningful measure of cache memory.
    /// </summary>
    public static long EstimateCacheMemory(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return 0;

        long renderTarget = (long)width * height * 4L;              // Rgba8Unorm texels
        uint bytesPerRow = (uint)width * 4u;
        uint alignedRow = (bytesPerRow + (RowAlignment - 1u)) & ~(RowAlignment - 1u);
        long readback = (long)alignedRow * height;                  // mappable copy-dst buffer

        return renderTarget + readback + UniformBufferBytes;
    }
}
