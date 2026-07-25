using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Gpu.Descriptors;
using Etch.Tiling.Strips;

namespace Etch.Gpu.Compositor;

public sealed unsafe class StripBufferUploader : IDisposable
{
    private const long MaxSize = 64 * 1024 * 1024;
    private const int RingSize = 4;

    private readonly Device _device;
    private readonly Buffer[] _stagingBuffers;
    private readonly Buffer[] _coverageBuffers;
    private readonly long[] _stagingSizes;
    private readonly long[] _coverageSizes;
    private readonly bool[] _stagingInitialized;
    private readonly bool[] _coverageInitialized;
    private int _currentFrame;
    private bool _disposed;

    public StripBufferUploader(Device device)
    {
        _device = device;
        _stagingBuffers = new Buffer[RingSize];
        _coverageBuffers = new Buffer[RingSize];
        _stagingSizes = new long[RingSize];
        _coverageSizes = new long[RingSize];
        _stagingInitialized = new bool[RingSize];
        _coverageInitialized = new bool[RingSize];
        _currentFrame = 0;
    }

    public StripGpuBuffers Upload(StripBuffer cpu)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long stripsSize = cpu.StripCount * (long)sizeof(Strip);
        long coverageSize = cpu.CoverageBytes.Length;
        long total = stripsSize + coverageSize;

        if (total > MaxSize)
        {
            Etch.Panic.Invariant(Etch.PanicCodes.StripBudgetExceeded, $"Strip + coverage = {total} bytes exceeds {MaxSize} byte budget");
        }

        int frame = _currentFrame;
        _currentFrame = (_currentFrame + 1) % RingSize;

        Buffer stripsBuffer = GetOrCreateBuffer(frame, stripsSize, _stagingBuffers, _stagingSizes, _stagingInitialized, _device);
        Buffer coverageBuffer = GetOrCreateBuffer(frame, coverageSize, _coverageBuffers, _coverageSizes, _coverageInitialized, _device);

        if (stripsSize > 0)
        {
            var stripsSpan = cpu.Strips;
            var stripsBytes = MemoryMarshal.AsBytes(stripsSpan);
            _device.Queue.WriteBuffer(stripsBuffer, 0, stripsBytes);
        }

        if (coverageSize > 0)
        {
            var coverageBytes = MemoryMarshal.AsBytes(cpu.CoverageBytes);
            // Queue.WriteBuffer requires the copy size to be a multiple of 4 (COPY_BUFFER_ALIGNMENT).
            int alignedSize = (int)((coverageSize + 3) & ~3);
            if (alignedSize == coverageSize)
            {
                _device.Queue.WriteBuffer(coverageBuffer, 0, coverageBytes);
            }
            else
            {
                Span<byte> padded = stackalloc byte[alignedSize];
                coverageBytes.CopyTo(padded);
                padded.Slice((int)coverageSize).Clear();
                _device.Queue.WriteBuffer(coverageBuffer, 0, padded);
            }
        }

        return new StripGpuBuffers(stripsBuffer, coverageBuffer, (uint)cpu.StripCount);
    }

    private static Buffer GetOrCreateBuffer(int index, long requiredSize, Buffer[] buffers, long[] sizes, bool[] initialized, Device device)
    {
        if (!initialized[index] || sizes[index] < requiredSize)
        {
            if (initialized[index])
            {
                buffers[index].Dispose();
            }
            sizes[index] = NextPowerOfTwo(requiredSize);
            if (sizes[index] > MaxSize)
            {
                sizes[index] = MaxSize;
            }
            buffers[index] = device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Storage | BufferUsage.CopyDst),
                Size = (ulong)sizes[index]
            });
            initialized[index] = true;
        }

        return buffers[index];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        for (int i = 0; i < RingSize; i++)
        {
            if (_stagingInitialized[i])
            {
                _stagingBuffers[i].Dispose();
            }
            if (_coverageInitialized[i])
            {
                _coverageBuffers[i].Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long NextPowerOfTwo(long value)
    {
        if (value <= 0)
        {
            return 1;
        }
        --value;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        value |= value >> 32;
        return value + 1;
    }
}