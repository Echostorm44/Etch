using System;
using System.Runtime.CompilerServices;
using Etch.Gpu.Descriptors;

namespace Etch.Gpu.Compositor;

public sealed class UniformRing : IDisposable
{
    private const int RingSize = 4;
    private const int DefaultAlignment = 256;
    private const long DefaultSizePerFrame = 4 * 1024 * 1024;

    private readonly Device _device;
    private readonly Buffer[] _buffers;
    private readonly long[] _bufferSizes;
    private readonly bool[] _bufferInitialized;
    private readonly uint _alignment;
    private int _currentFrame;
    private int _offsetInFrame;
    private bool _disposed;

    public UniformRing(Device device, uint alignment = DefaultAlignment)
    {
        _device = device;
        _alignment = alignment > 0 ? alignment : DefaultAlignment;
        _buffers = new Buffer[RingSize];
        _bufferSizes = new long[RingSize];
        _bufferInitialized = new bool[RingSize];
        _currentFrame = 0;
        _offsetInFrame = 0;
    }

    public UniformSlot Reserve(int sizeBytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (sizeBytes <= 0)
        {
            sizeBytes = 1;
        }

        uint alignedSize = AlignUp((uint)sizeBytes, _alignment);

        if (_offsetInFrame + alignedSize > DefaultSizePerFrame)
        {
            _currentFrame = (_currentFrame + 1) % RingSize;
            _offsetInFrame = 0;
        }

        int frame = _currentFrame;
        Buffer buffer = GetOrCreateBuffer(frame);

        uint offset = (uint)_offsetInFrame;
        _offsetInFrame += (int)alignedSize;

        return new UniformSlot(buffer, offset, alignedSize);
    }

    public void BeginFrame(uint frameIndex)
    {
        _currentFrame = (int)(frameIndex % RingSize);
        _offsetInFrame = 0;
    }

    private Buffer GetOrCreateBuffer(int index)
    {
        if (!_bufferInitialized[index])
        {
            _buffers[index] = _device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Uniform | BufferUsage.CopyDst),
                Size = (ulong)DefaultSizePerFrame
            });
            _bufferSizes[index] = DefaultSizePerFrame;
            _bufferInitialized[index] = true;
        }

        return _buffers[index];
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
            if (_bufferInitialized[i])
            {
                _buffers[i].Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint AlignUp(uint value, uint alignment)
    {
        uint remainder = value % alignment;
        if (remainder == 0)
        {
            return value;
        }
        return value + (alignment - remainder);
    }
}
