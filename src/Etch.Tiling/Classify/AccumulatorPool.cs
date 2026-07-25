using System.Buffers;

namespace Etch.Tiling.Classify;

public sealed class AccumulatorPool
{
    private readonly int _initialCapacity;

    public AccumulatorPool(int initialCapacity = 1024)
    {
        _initialCapacity = initialCapacity;
    }

    public ClassificationAccumulator Acquire()
    {
        return new ClassificationAccumulator(_initialCapacity);
    }

    public static void Release(ClassificationAccumulator accumulator)
    {
        accumulator.Dispose();
    }
}