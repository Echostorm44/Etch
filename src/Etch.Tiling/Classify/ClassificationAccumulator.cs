using System;
using Etch.Primitives;

namespace Etch.Tiling.Classify;

#pragma warning disable CA2213 // PooledBuffer is a ref struct that needs explicit dispose
public ref struct ClassificationAccumulator
{
    private PooledBuffer<ClassificationEntry> _buffer;
    private int _count;
    private bool _finished;

    public ClassificationAccumulator(int initialCapacity = 1024)
    {
        _buffer = PooledBuffer<ClassificationEntry>.Rent(initialCapacity);
        _count = 0;
        _finished = false;
    }

    internal ClassificationAccumulator(PooledBuffer<ClassificationEntry> buffer)
    {
        _buffer = buffer;
        _count = 0;
        _finished = false;
    }

    public void Append(in ClassificationEntry entry)
    {
        if (_finished)
            Etch.Panic.Invariant(Etch.PanicCodes.AccumulatorConsumed, "Accumulator already finished");

        if (_count >= _buffer.Span.Length)
        {
            var newBuffer = PooledBuffer<ClassificationEntry>.Rent(_buffer.Span.Length * 2);
            _buffer.Span.CopyTo(newBuffer.Span);
            _buffer.Dispose();
            _buffer = newBuffer;
        }

        _buffer.Span[_count++] = entry;
    }

    public ReadOnlySpan<ClassificationEntry> Finish()
    {
        if (_finished)
            Etch.Panic.Invariant(Etch.PanicCodes.AccumulatorConsumed, "Accumulator already finished");

        _finished = true;
        return _buffer.Span[.._count];
    }

    public void Dispose()
    {
        _buffer.Dispose();
    }
}
#pragma warning restore CA2213