using System;
using Etch.Tiling.Classify;

namespace Etch.Tiling.Scheduler;

public sealed class WorkStealingTileScheduler : ITileScheduler
{
    private readonly int _workerCount;
    private bool _disposed;

    public int WorkerCount => _workerCount;

    public WorkStealingTileScheduler(int workerCount = -1)
    {
        _workerCount = workerCount < 1 ? Environment.ProcessorCount : workerCount;
        _disposed = false;
    }

    public unsafe void ParallelFor<TContext>(int count, ref TContext context, delegate*<int, ref TContext, ref ClassificationAccumulator, void> work)
        where TContext : struct
    {
        if (_disposed)
            Etch.Panic.Invariant(Etch.PanicCodes.InvalidState, "WorkStealingTileScheduler used after dispose");

        if (count == 0)
            return;

        var pool = new AccumulatorPool();
        var accum = pool.Acquire();
        try
        {
            for (int i = 0; i < count; i++)
            {
                work(i, ref context, ref accum);
            }
        }
        finally
        {
            AccumulatorPool.Release(accum);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}
