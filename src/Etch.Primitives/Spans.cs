using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Etch;

namespace Etch.Primitives;

[DebuggerDisplay("Position={Position}/{_length}")]
public ref struct SpanWriter
{
    private readonly Span<byte> _span;
    private int _position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanWriter(Span<byte> span)
    {
        _span = span;
        _position = 0;
    }

    public int Position => _position;
    public int Remaining => _span.Length - _position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        if (_position >= _span.Length)
        {
            Panic.Invariant(PanicCodes.BufferOverflow, "SpanWriter.WriteByte: buffer exhausted");
        }
        _span[_position++] = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteU32LE(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Slice(4), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteI32LE(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(Slice(4), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteU64LE(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Slice(8), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteI64LE(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(Slice(8), value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteF32LE(float value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Slice(4), SingleToUInt32Bits(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteF64LE(double value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(Slice(8), BitConverter.DoubleToUInt64Bits(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteVarInt(ulong value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        WriteByte((byte)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteUtf8(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarInt((ulong)byteCount);
        Encoding.UTF8.GetBytes(value, Slice(byteCount));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Span<byte> Slice(int byteCount)
    {
        if (_position + byteCount > _span.Length)
        {
            Panic.Invariant(PanicCodes.BufferOverflow, "SpanWriter.Slice: not enough room for requested slice");
        }
        var result = _span.Slice(_position, byteCount);
        _position += byteCount;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SingleToUInt32Bits(float value)
    {
        return BitConverter.SingleToUInt32Bits(value);
    }
}

[DebuggerDisplay("Position={Position}")]
public ref struct SpanReader
{
    private readonly ReadOnlySpan<byte> _span;
    private int _position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpanReader(ReadOnlySpan<byte> span)
    {
        _span = span;
        _position = 0;
    }

    public int Position => _position;
    public int Remaining => _span.Length - _position;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        return _span[_position++];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadU32LE()
    {
        var result = BinaryPrimitives.ReadUInt32LittleEndian(_span.Slice(_position, 4));
        _position += 4;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadI32LE()
    {
        var result = BinaryPrimitives.ReadInt32LittleEndian(_span.Slice(_position, 4));
        _position += 4;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadU64LE()
    {
        var result = BinaryPrimitives.ReadUInt64LittleEndian(_span.Slice(_position, 8));
        _position += 8;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadI64LE()
    {
        var result = BinaryPrimitives.ReadInt64LittleEndian(_span.Slice(_position, 8));
        _position += 8;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadF32LE()
    {
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(_span.Slice(_position, 4));
        _position += 4;
        return UInt32BitsToSingle(bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadF64LE()
    {
        var bits = BinaryPrimitives.ReadUInt64LittleEndian(_span.Slice(_position, 8));
        _position += 8;
        return BitConverter.UInt64BitsToDouble(bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadVarInt()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadUtf8(int byteCount)
    {
        var result = Encoding.UTF8.GetString(_span.Slice(_position, byteCount));
        _position += byteCount;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float UInt32BitsToSingle(uint value)
    {
        return BitConverter.UInt32BitsToSingle(value);
    }
}

#pragma warning disable CA1815 // Inline array wrappers intentionally omit equality ops per FND-008 spec
[InlineArray(16)]
public struct StackBuffer16<T>
{
    private T _element0;
}

[InlineArray(32)]
public struct StackBuffer32<T>
{
    private T _element0;
}

[InlineArray(64)]
public struct StackBuffer64<T>
{
    private T _element0;
}

[InlineArray(128)]
public struct StackBuffer128<T>
{
    private T _element0;
}

[InlineArray(256)]
public struct StackBuffer256<T>
{
    private T _element0;
}
#pragma warning restore CA1815
