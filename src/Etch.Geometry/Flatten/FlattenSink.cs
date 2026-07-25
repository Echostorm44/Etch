using System;
using Etch.Geometry;

namespace Etch.Geometry.Flatten;

public ref struct FlattenSink
{
    private readonly Span<Point> _buffer;
    private int _count;
    public readonly bool Autoflush;

    public FlattenSink(Span<Point> buffer, bool autoflush = false)
    {
        _buffer = buffer;
        _count = 0;
        Autoflush = autoflush;
    }

    public readonly int Count => _count;

    public readonly bool IsFull => _count >= _buffer.Length;

    public void Accept(Point point)
    {
        if (_count >= _buffer.Length)
        {
            if (!Autoflush)
            {
                Etch.Panic.Invariant(
                    Etch.PanicCodes.FlattenSinkOverflow,
                    "FlattenSink overflow: Accept called on a full sink without autoflush enabled.");
            }
            return;
        }
        _buffer[_count++] = point;
    }

    public void Reset()
    {
        _count = 0;
    }

    public readonly Span<Point> Written => _buffer.Slice(0, _count);
}