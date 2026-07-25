using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading;

namespace Etch.Gpu.Diagnostics;

// ═══════════════════════════════════════════════════════════════════════════
// ValidationLogRing — a fixed-size, wrap-around log of recent wgpu validation
// messages, designed to be written from the wgpu callback thread with zero
// managed allocation. Reads happen only at crash-dump time from whichever
// thread holds the panic; concurrent readers are not supported (not needed).
//
// Concurrency model:
//   Push uses Interlocked.Increment on a 64-bit counter to claim a unique
//   write slot. We then copy the UTF-8 payload into a fixed 240-byte inline
//   buffer inside the ValidationEntry struct. Torn writes are possible if the
//   same slot is overwritten mid-copy by another producer that has wrapped
//   around, but in practice validation-log throughput is nowhere near the
//   ring capacity, and this ring is strictly a best-effort forensic aid —
//   correctness of rendering is unaffected.
//
// Memory: 256 entries * 256 bytes/entry ≈ 64 KiB per ring. The ring is
// allocated once at construction time and reused forever.
// ═══════════════════════════════════════════════════════════════════════════

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 256)]
public struct ValidationEntry
{
    public const int InlineMessageCapacity = 240;

    public long TimestampTicks; // Stopwatch.GetTimestamp()
    public uint ErrorType;      // matches Etch.Gpu.ErrorType ordinal
    public ushort MessageLength; // valid bytes in Message (<= InlineMessageCapacity)
    public ushort _reserved;    // pads the header to 16 bytes

    // Inline UTF-8 message buffer — exactly 240 bytes, no allocation.
    private unsafe fixed byte Message[InlineMessageCapacity];

    public unsafe void WriteMessage(ReadOnlySpan<byte> utf8)
    {
        int copyLen = Math.Min(utf8.Length, InlineMessageCapacity);
        fixed (byte* dest = Message)
        {
            utf8.Slice(0, copyLen).CopyTo(new Span<byte>(dest, InlineMessageCapacity));
        }
        MessageLength = (ushort)copyLen;
    }
}

public sealed class ValidationLogRing
{
    public const int DefaultCapacity = 256;

    private readonly ValidationEntry[] _buffer;
    private long _writeCounter; // monotonic: next slot index = (writeCounter - 1) % capacity
    private readonly int _capacityMask; // capacity - 1 (capacity must be power of two)

    public ValidationLogRing() : this(DefaultCapacity) { }

    public ValidationLogRing(int capacity)
    {
        if (capacity <= 0 || (capacity & (capacity - 1)) != 0)
        {
            Panic.ArgumentOutOfRange(nameof(capacity), "Capacity must be a positive power of two.");
        }

        _buffer = new ValidationEntry[capacity];
        _capacityMask = capacity - 1;
    }

    public int Capacity => _buffer.Length;

    /// <summary>
    /// Total number of Push calls ever made. If this exceeds Capacity, entries have wrapped.
    /// </summary>
    public long TotalWrites => Interlocked.Read(ref _writeCounter);

    /// <summary>
    /// Records one validation entry. Zero managed allocations on this path —
    /// callers typically invoke from the wgpu-native callback thread. The
    /// <paramref name="timestampTicks"/> argument is supplied by the caller
    /// (typically <c>IFrameClock.NowNanos</c> or <c>Stopwatch.GetTimestamp</c>
    /// routed through a determinism seam) so the ring itself stays free of
    /// non-deterministic API calls.
    /// </summary>
    public void Push(ErrorType type, ReadOnlySpan<byte> utf8Message, long timestampTicks)
    {
        long slot = Interlocked.Increment(ref _writeCounter) - 1;
        ref ValidationEntry entry = ref _buffer[slot & _capacityMask];

        entry.TimestampTicks = timestampTicks;
        entry.ErrorType = (uint)type;
        entry.WriteMessage(utf8Message);
    }

    /// <summary>
    /// Encodes the ring into a little-endian byte blob for inclusion in an .etrp section.
    /// Entries are emitted in chronological order (oldest first). Not called on hot paths.
    ///
    /// Blob layout:
    ///   u32 Count            — number of entries actually present (min(TotalWrites, Capacity))
    ///   per entry:
    ///     i64 TimestampTicks
    ///     u32 ErrorType
    ///     u16 MessageLength
    ///     UTF-8 bytes (MessageLength)
    /// </summary>
    public byte[] Snapshot()
    {
        long totalWrites = Interlocked.Read(ref _writeCounter);
        int count = (int)Math.Min(totalWrites, _buffer.Length);

        // Compute total size first so we can allocate once.
        long startIndex = totalWrites > _buffer.Length ? totalWrites - _buffer.Length : 0;
        int totalSize = 4;
        for (int i = 0; i < count; i++)
        {
            long globalSlot = startIndex + i;
            ref readonly ValidationEntry entry = ref _buffer[globalSlot & _capacityMask];
            totalSize += 8 + 4 + 2 + entry.MessageLength;
        }

        byte[] result = new byte[totalSize];
        Span<byte> dest = result.AsSpan();
        int pos = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(pos, 4), (uint)count);
        pos += 4;

        for (int i = 0; i < count; i++)
        {
            long globalSlot = startIndex + i;
            ref readonly ValidationEntry entry = ref _buffer[globalSlot & _capacityMask];

            BinaryPrimitives.WriteInt64LittleEndian(dest.Slice(pos, 8), entry.TimestampTicks);
            pos += 8;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(pos, 4), entry.ErrorType);
            pos += 4;
            BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(pos, 2), entry.MessageLength);
            pos += 2;

            unsafe
            {
                fixed (ValidationEntry* ptr = &_buffer[globalSlot & _capacityMask])
                {
                    // Message buffer starts 16 bytes into the entry (after header).
                    byte* messageStart = (byte*)ptr + 16;
                    new ReadOnlySpan<byte>(messageStart, entry.MessageLength).CopyTo(dest.Slice(pos));
                }
            }
            pos += entry.MessageLength;
        }

        return result;
    }

    public static bool TryDecode(ReadOnlySpan<byte> source, out ValidationLogSnapshot snapshot)
    {
        snapshot = default;
        if (source.Length < 4) return false;

        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4));
        var entries = new DecodedValidationEntry[count];
        int pos = 4;

        for (int i = 0; i < count; i++)
        {
            if (pos + 14 > source.Length) return false;
            long ts = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(pos, 8));
            pos += 8;
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pos, 4));
            pos += 4;
            int len = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(pos, 2));
            pos += 2;
            if (pos + len > source.Length) return false;

            string msg = System.Text.Encoding.UTF8.GetString(source.Slice(pos, len));
            pos += len;

            entries[i] = new DecodedValidationEntry(ts, type, msg);
        }

        snapshot = new ValidationLogSnapshot(entries);
        return true;
    }
}

public readonly struct DecodedValidationEntry
{
    public long TimestampTicks { get; }
    public uint ErrorType { get; }
    public string Message { get; }

    public DecodedValidationEntry(long timestampTicks, uint errorType, string message)
    {
        TimestampTicks = timestampTicks;
        ErrorType = errorType;
        Message = message;
    }
}

public readonly struct ValidationLogSnapshot
{
    private readonly DecodedValidationEntry[] _entries;

    public ValidationLogSnapshot(DecodedValidationEntry[] entries)
    {
        _entries = entries ?? Array.Empty<DecodedValidationEntry>();
    }

    public int Count => _entries?.Length ?? 0;

    public ReadOnlySpan<DecodedValidationEntry> Entries => _entries ?? Array.Empty<DecodedValidationEntry>();

    public DecodedValidationEntry this[int index] => _entries[index];
}
