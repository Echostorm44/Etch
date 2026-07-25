using System;
using System.Buffers;
using System.Diagnostics;
using Etch;

namespace Etch.Primitives;

[DebuggerDisplay("Length={Length}")]
#pragma warning disable CA1000 // Static member on generic type is intentional per FND-008 design
public ref struct PooledBuffer<T>
{
    private readonly T[] _array;
    private readonly int _length;

    public Span<T> Span { get; }
    public int Length => _length;

    public PooledBuffer(T[] array, int length)
    {
        _array = array;
        _length = length;
        Span = array.AsSpan(0, length);
    }

    public static PooledBuffer<T> Rent(int minimumLength)
    {
        var array = ArrayPool<T>.Shared.Rent(minimumLength);
        return new PooledBuffer<T>(array, minimumLength);
    }

    public void Dispose()
    {
        if (_array != null)
        {
            ArrayPool<T>.Shared.Return(_array, clearArray: true);
        }
    }
}
#pragma warning restore CA1000

#pragma warning disable CA1815 // Inline array wrappers intentionally omit equality ops per FND-008 spec
public struct MemoryBudget
{
    private long _remaining;

    public long Remaining => _remaining;
    public bool IsExhausted => _remaining <= 0;

    public MemoryBudget(long bytes)
    {
        _remaining = bytes;
    }

    public bool TryAllocate(long bytes)
    {
        if (_remaining < bytes)
        {
            return false;
        }
        _remaining -= bytes;
        return true;
    }

    public void AssertPositive(string label)
    {
        if (_remaining < 0)
        {
            Panic.Invariant(PanicCodes.InvalidState, $"Budget exceeded: {label} by {-_remaining} bytes");
        }
    }
}
#pragma warning restore CA1815
