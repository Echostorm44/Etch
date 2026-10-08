using Etch.Compose.Cpu;

namespace Etch.Compose.Tests;

/// <summary>The CPU composer's worker threads: allocation-free runs whatever the timing.</summary>
[NotInParallel]
internal sealed class TileWorkersTests
{
    /// <summary>
    /// Waiting must not allocate the first time it blocks. BCL wait primitives create their lock
    /// object on the first wait that blocks (24 bytes for a <see cref="CountdownEvent"/>), so a run
    /// that happened to be the first where the caller outlasted its spin allocated, and which run
    /// that was depended on scheduling: on a 3-core macOS runner it fell in the soak test's second
    /// scroll pass (<see cref="MaskAtlasSoakTests"/>, "found 24").
    /// </summary>
    [Test]
    public async Task Run_AllocatesNothing_WhenTheCallerFirstBlocksLate()
    {
        bool callerSlow = true;
        using var workers = new TileWorkers(index =>
        {
            if ((index == 0) == callerSlow)
            {
                Thread.Sleep(30);
            }
        });

        // The caller is the slow one: the workers finish first, so the caller never blocks.
        for (int i = 0; i < 3; i++)
        {
            workers.Run(3);
        }

        // Now the workers are slow and the caller blocks waiting for them, for the first time.
        callerSlow = false;
        long before = GC.GetAllocatedBytesForCurrentThread() + workers.WorkerAllocatedBytes;
        for (int i = 0; i < 3; i++)
        {
            workers.Run(3);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() + workers.WorkerAllocatedBytes - before;
        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task Run_RunsEveryIndexOnce_AndRethrowsAWorkerFailure()
    {
        var seen = new int[4];
        bool fail = false;
        using var workers = new TileWorkers(index =>
        {
            Interlocked.Increment(ref seen[index]);
            if (fail && index == 2)
            {
                throw new InvalidOperationException("worker 2");
            }
        });
        for (int i = 0; i < 50; i++)
        {
            workers.Run(4);
        }
        workers.Run(2);
        await Assert.That(string.Join(',', seen)).IsEqualTo("51,51,50,50");

        fail = true;
        await Assert.That(() => workers.Run(4)).Throws<InvalidOperationException>();
        fail = false;
        workers.Run(4);
        await Assert.That(seen[2]).IsEqualTo(52);
    }
}
