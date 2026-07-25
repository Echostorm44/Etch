using System;

namespace Etch.Gpu.Compositor;

public readonly struct UniformSlot
{
    public Buffer Buffer { get; }
    public uint OffsetBytes { get; }
    public uint AlignedSize { get; }

    public UniformSlot(Buffer buffer, uint offsetBytes, uint alignedSize)
    {
        Buffer = buffer;
        OffsetBytes = offsetBytes;
        AlignedSize = alignedSize;
    }
}
