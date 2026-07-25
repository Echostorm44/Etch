using System.Buffers.Binary;

namespace Etch.Primitives;

public static partial class Endian
{
    public static ushort ReadU16LE(ReadOnlySpan<byte> source)
        => BinaryPrimitives.ReadUInt16LittleEndian(source);

    public static void WriteU16LE(Span<byte> destination, ushort value)
        => BinaryPrimitives.WriteUInt16LittleEndian(destination, value);

    public static uint ReadU32LE(ReadOnlySpan<byte> source)
        => BinaryPrimitives.ReadUInt32LittleEndian(source);

    public static void WriteU32LE(Span<byte> destination, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(destination, value);

    public static ulong ReadU64LE(ReadOnlySpan<byte> source)
        => BinaryPrimitives.ReadUInt64LittleEndian(source);

    public static void WriteU64LE(Span<byte> destination, ulong value)
        => BinaryPrimitives.WriteUInt64LittleEndian(destination, value);

    public static short ReadI16LE(ReadOnlySpan<byte> source)
        => BinaryPrimitives.ReadInt16LittleEndian(source);

    public static void WriteI16LE(Span<byte> destination, short value)
        => BinaryPrimitives.WriteInt16LittleEndian(destination, value);

    public static int ReadI32LE(ReadOnlySpan<byte> source)
        => BinaryPrimitives.ReadInt32LittleEndian(source);

    public static void WriteI32LE(Span<byte> destination, int value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination, value);

    public static long ReadI64LE(ReadOnlySpan<byte> source)
        => BinaryPrimitives.ReadInt64LittleEndian(source);

    public static void WriteI64LE(Span<byte> destination, long value)
        => BinaryPrimitives.WriteInt64LittleEndian(destination, value);
}
