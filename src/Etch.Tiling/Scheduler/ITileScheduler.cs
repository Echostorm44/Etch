using System;
using Etch.Abstractions;
using Etch.Tiling.Classify;

namespace Etch.Tiling.Scheduler;

[EtchExtensionPoint]
public unsafe interface ITileScheduler : IDisposable
{
    void ParallelFor<TContext>(int count, ref TContext context, delegate*<int, ref TContext, ref ClassificationAccumulator, void> work)
        where TContext : struct;
}
