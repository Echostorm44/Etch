using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Etch.Gpu.Descriptors;
using Etch.Tiling;

namespace Etch.Gpu.Compositor;

public sealed unsafe class TileQuadUploader : IDisposable
{
    private readonly Device _device;
    private Buffer _unitQuadVertex;
    private Buffer _instanceBuffer;
    private int _instanceCapacity;
    private bool _unitQuadInitialized;

    private const int UnitQuadVertexCount = 4;
    private const int FloatsPerVertex = 2;
    private const int UnitQuadVertexBufferSize = UnitQuadVertexCount * FloatsPerVertex * sizeof(float);

    public TileQuadUploader(Device device)
    {
        _device = device;
        _instanceCapacity = 0;
        _unitQuadVertex = default;
        _instanceBuffer = default;
        _unitQuadInitialized = false;
    }

    public TileQuadBuffers Upload(TileQuadList list)
    {
        ObjectDisposedException.ThrowIf(_unitQuadInitialized && _instanceCapacity < 0, this);

        Buffer unitQuad = GetOrCreateUnitQuadVertexBuffer();

        int count = list.Count;
        Buffer instance = GetOrCreateInstanceBuffer(count);

        if (count > 0)
        {
            var quads = list.Quads;
            var byteSpan = MemoryMarshal.AsBytes(quads);
            _device.Queue.WriteBuffer(instance, 0, byteSpan);
        }

        return new TileQuadBuffers(unitQuad, instance);
    }

    private Buffer GetOrCreateUnitQuadVertexBuffer()
    {
        if (!_unitQuadInitialized)
        {
            float* vertices = stackalloc float[UnitQuadVertexCount * FloatsPerVertex];
            vertices[0] = 0.0f; vertices[1] = 0.0f;
            vertices[2] = 1.0f; vertices[3] = 0.0f;
            vertices[4] = 0.0f; vertices[5] = 1.0f;
            vertices[6] = 1.0f; vertices[7] = 1.0f;

            _unitQuadVertex = _device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
                Size = (ulong)UnitQuadVertexBufferSize
            });

            var span = new ReadOnlySpan<byte>(vertices, UnitQuadVertexBufferSize);
            _device.Queue.WriteBuffer(_unitQuadVertex, 0, span);
            _unitQuadInitialized = true;
        }

        return _unitQuadVertex;
    }

    private Buffer GetOrCreateInstanceBuffer(int requiredCount)
    {
        if (requiredCount > _instanceCapacity)
        {
            if (_instanceCapacity > 0)
            {
                _instanceBuffer.Dispose();
            }
            _instanceCapacity = requiredCount == 0 ? 256 : NextPowerOfTwo(requiredCount);
            _instanceBuffer = _device.CreateBuffer(new BufferDescriptor
            {
                Usage = (ulong)(BufferUsage.Vertex | BufferUsage.CopyDst),
                Size = (ulong)((long)_instanceCapacity * sizeof(TileQuad))
            });
        }

        return _instanceBuffer;
    }

    public void Dispose()
    {
        if (_instanceCapacity > 0)
        {
            _instanceBuffer.Dispose();
        }
        if (_unitQuadInitialized)
        {
            _unitQuadVertex.Dispose();
        }
        _instanceCapacity = -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int NextPowerOfTwo(int value)
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
        return value + 1;
    }
}