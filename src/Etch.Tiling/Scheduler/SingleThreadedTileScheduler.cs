using System;
using System.Threading;
using Etch.Tiling.Classify;

namespace Etch.Tiling.Scheduler;

public sealed class SingleThreadedTileScheduler : ITileScheduler
{
    public unsafe void ParallelFor<TContext>(int count, ref TContext context, delegate*<int, ref TContext, ref ClassificationAccumulator, void> work)
        where TContext : struct
    {
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
    }
}
