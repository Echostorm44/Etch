using System;
using System.Collections.Generic;
using Etch.Gpu.Descriptors;
using Etch.Gpu.Native;

namespace Etch.Gpu;

/// <summary>
/// Collects many small writes into one texture (glyph bitmaps, coverage masks) and records them as
/// buffer-to-texture copies from a few mapped upload buffers, instead of one
/// <see cref="Queue.WriteTexture"/> per write.
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="Queue.WriteTexture"/> call stages its bytes in an upload buffer of its own, and
/// on D3D12 each such buffer occupies at least a 64 KB placement: a frame that rasterizes a few
/// hundred glyphs held tens of megabytes of upload memory at once, which the GPU allocator then
/// kept as its high-water mark. A batch writes the bytes straight into buffers mapped at creation
/// (at least <see cref="MinChunkBytes"/>, doubling), so the same frame stages a few hundred
/// kilobytes in a handful of buffers, released once the copies have run.
/// </para>
/// <para>
/// The batch holds no buffer between flushes: a frame that uploads nothing costs nothing, and an
/// idle application keeps no upload memory.
/// </para>
/// </remarks>
public sealed class TextureUploadBatch : IDisposable
{
    /// <summary>Smallest upload buffer the batch creates (one D3D12 placement).</summary>
    public const ulong MinChunkBytes = 64 * 1024;

    // WebGPU: a buffer-to-texture copy's bytes per row are a multiple of 256; D3D12 wants each copy's
    // offset 512-aligned.
    private const uint RowAlignment = 256;
    private const ulong OffsetAlignment = 512;

    private readonly Device device;
    private readonly List<Chunk> chunks = new();
    private readonly List<PendingCopy> copies = new();

    private struct Chunk
    {
        public Buffer Buffer;
        public nint Mapped;
        public ulong Capacity;
        public ulong Used;
    }

    private readonly record struct PendingCopy(int Chunk, ulong Offset, uint BytesPerRow, uint X, uint Y, uint Layer, uint Width, uint Height);

    /// <summary>Creates an empty batch for textures of <paramref name="device"/>.</summary>
    public TextureUploadBatch(Device device)
    {
        this.device = device;
    }

    /// <summary>Writes waiting for <see cref="Flush(CommandEncoder, Texture)"/>.</summary>
    public int PendingCount => copies.Count;

    /// <summary>Bytes of upload buffers held until the next <see cref="Flush(CommandEncoder, Texture)"/> (0 when nothing is pending).</summary>
    public ulong PendingBufferBytes
    {
        get
        {
            ulong total = 0;
            foreach (var chunk in chunks)
            {
                total += chunk.Capacity;
            }
            return total;
        }
    }

    /// <summary>
    /// Queues a write of <paramref name="width"/> × <paramref name="height"/> texels at
    /// (<paramref name="x"/>, <paramref name="y"/>) of array layer <paramref name="layer"/>;
    /// <paramref name="pixels"/> holds the rows tightly packed (<paramref name="bytesPerPixel"/> per texel).
    /// The bytes are copied now; the texture is written by the next <see cref="Flush(CommandEncoder, Texture)"/>.
    /// </summary>
    public unsafe void Add(uint x, uint y, uint layer, int width, int height, int bytesPerPixel, ReadOnlySpan<byte> pixels)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }
        int rowBytes = width * bytesPerPixel;
        if (pixels.Length < rowBytes * height)
        {
            Panic.Invariant(PanicCodes.BufferOverflow, $"Upload of {width}x{height} texels given {pixels.Length} bytes");
        }
        uint stride = (uint)((rowBytes + RowAlignment - 1) / RowAlignment * RowAlignment);
        ulong size = (ulong)stride * (ulong)height;
        int chunkIndex = ChunkWithRoom(size);
        var chunk = chunks[chunkIndex];
        ulong offset = chunk.Used;
        byte* destination = (byte*)chunk.Mapped + offset;
        for (int row = 0; row < height; row++)
        {
            pixels.Slice(row * rowBytes, rowBytes).CopyTo(new Span<byte>(destination + (long)row * stride, rowBytes));
        }
        chunk.Used = AlignUp(offset + size, OffsetAlignment);
        chunks[chunkIndex] = chunk;
        copies.Add(new PendingCopy(chunkIndex, offset, stride, x, y, layer, (uint)width, (uint)height));
    }

    /// <summary>
    /// Records every queued write into <paramref name="encoder"/> as copies into
    /// <paramref name="texture"/> (the texture as it is now: one that grew since the writes were
    /// queued receives them at the same texels), then releases the upload buffers (the GPU keeps
    /// them until the copies have run).
    /// </summary>
    public void Flush(CommandEncoder encoder, Texture texture)
    {
        if (copies.Count == 0)
        {
            return;
        }
        UnmapAll();
        foreach (var copy in copies)
        {
            var layout = new WGPUTexelCopyBufferLayout { Offset = copy.Offset, BytesPerRow = copy.BytesPerRow, RowsPerImage = copy.Height };
            var origin = new WGPUOrigin3D { X = copy.X, Y = copy.Y, Z = copy.Layer };
            var size = new Extent3D { Width = copy.Width, Height = copy.Height, DepthOrArrayLayers = 1 };
            encoder.CopyBufferToTexture(chunks[copy.Chunk].Buffer, layout, texture, 0, origin, size);
        }
        ReleaseChunks();
    }

    /// <summary>
    /// Records the queued writes in a command buffer of their own and submits it (for callers
    /// without a frame encoder at hand).
    /// </summary>
    public void Flush(Texture texture)
    {
        if (copies.Count == 0)
        {
            return;
        }
        using var encoder = device.CreateCommandEncoder();
        Flush(encoder, texture);
        using var commands = encoder.Finish();
        Span<CommandBuffer> submit = stackalloc CommandBuffer[1];
        submit[0] = commands;
        device.Queue.Submit(submit);
    }

    /// <summary>Drops every queued write (the texture they were for is gone or forgotten).</summary>
    public void Clear()
    {
        if (chunks.Count == 0)
        {
            return;
        }
        UnmapAll();
        ReleaseChunks();
    }

    /// <summary>Drops every queued write and its upload buffers.</summary>
    public void Dispose() => Clear();

    private int ChunkWithRoom(ulong size)
    {
        if (chunks.Count > 0)
        {
            var last = chunks[^1];
            if (last.Used + size <= last.Capacity)
            {
                return chunks.Count - 1;
            }
        }
        ulong capacity = MinChunkBytes;
        if (chunks.Count > 0)
        {
            capacity = Math.Max(capacity, chunks[^1].Capacity * 2);
        }
        capacity = Math.Max(capacity, AlignUp(size, OffsetAlignment));
        var buffer = device.CreateBuffer(new BufferDescriptor
        {
            Usage = (ulong)(BufferUsage.MapWrite | BufferUsage.CopySrc),
            Size = capacity,
            MappedAtCreation = 1,
        });
        nint mapped = WebGPU.BufferGetMappedRange(buffer.Handle, 0, capacity);
        if (mapped == 0)
        {
            buffer.Dispose();
            Panic.Invariant(PanicCodes.BufferOverflow, $"Upload buffer of {capacity} bytes could not be mapped");
        }
        chunks.Add(new Chunk { Buffer = buffer, Mapped = mapped, Capacity = capacity });
        return chunks.Count - 1;
    }

    private void UnmapAll()
    {
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.Mapped != 0)
            {
                chunk.Buffer.Unmap();
                chunk.Mapped = 0;
                chunks[i] = chunk;
            }
        }
    }

    private void ReleaseChunks()
    {
        foreach (var chunk in chunks)
        {
            chunk.Buffer.Dispose();
        }
        chunks.Clear();
        copies.Clear();
    }

    private static ulong AlignUp(ulong value, ulong alignment) => (value + alignment - 1) / alignment * alignment;
}
