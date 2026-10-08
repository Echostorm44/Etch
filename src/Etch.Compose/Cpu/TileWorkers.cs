using System.Runtime.ExceptionServices;

namespace Etch.Compose.Cpu;

/// <summary>
/// Runs a worker body on N threads — the caller plus N − 1 dedicated background threads — and
/// waits for all of them, without allocating per run.
/// </summary>
/// <remarks>
/// Dedicated threads rather than the thread pool: pool threads spin for work after finishing a
/// frame, which measurably slowed the single-threaded work that follows (building the next draw
/// list ran 5× slower). These block on an event that does not spin. Threads are created when a
/// frame first needs them and end with <see cref="Dispose"/>; an idle composer costs no CPU.
/// </remarks>
internal sealed class TileWorkers : IDisposable
{
    private const int StackSize = 256 * 1024;

    private readonly Action<int> body;
    private readonly CountdownEvent done = new(1);
    private readonly List<Worker> workers = new();
    private ExceptionDispatchInfo? failure;
    private volatile bool stopping;

    public TileWorkers(Action<int> body)
    {
        this.body = body;
    }

    /// <summary>Runs <c>body(0)</c> … <c>body(threads − 1)</c> concurrently; returns when all return.</summary>
    public void Run(int threads)
    {
        if (threads <= 1)
        {
            body(0);
            return;
        }
        while (workers.Count < threads - 1)
        {
            workers.Add(new Worker(this, workers.Count + 1));
        }
        failure = null;
        done.Reset(threads - 1);
        for (int i = 0; i < threads - 1; i++)
        {
            workers[i].Start.Set();
        }
        try
        {
            body(0);
        }
        finally
        {
            done.Wait();
        }
        failure?.Throw();
    }

    /// <summary>
    /// Bytes the worker threads have allocated, as each last observed after running the body
    /// (tests prove frames allocate nothing without counting other threads in the process).
    /// </summary>
    public long WorkerAllocatedBytes
    {
        get
        {
            long total = 0;
            foreach (var worker in workers)
            {
                total += Volatile.Read(ref worker.Allocated);
            }
            return total;
        }
    }

    public void Dispose()
    {
        stopping = true;
        foreach (var worker in workers)
        {
            worker.Start.Set();
        }
        foreach (var worker in workers)
        {
            worker.Thread.Join();
            worker.Start.Dispose();
        }
        workers.Clear();
        done.Dispose();
    }

    private sealed class Worker
    {
        private readonly TileWorkers owner;
        private readonly int index;

        public Worker(TileWorkers owner, int index)
        {
            this.owner = owner;
            this.index = index;
            Thread = new Thread(Loop, StackSize) { IsBackground = true, Name = $"Etch CPU composer {index}" };
            Thread.Start();
        }

        // No spinning: a worker sleeps in the kernel between frames.
        public ManualResetEventSlim Start { get; } = new(false, spinCount: 0);

        public Thread Thread { get; }

        // This thread's allocation counter after its last run of the body.
        public long Allocated;

        private void Loop()
        {
            while (true)
            {
                Start.Wait();
                Start.Reset();
                if (owner.stopping)
                {
                    return;
                }
                try
                {
                    owner.body(index);
                }
                catch (Exception e)
                {
                    Interlocked.CompareExchange(ref owner.failure, ExceptionDispatchInfo.Capture(e), null);
                }
                finally
                {
                    Volatile.Write(ref Allocated, GC.GetAllocatedBytesForCurrentThread());
                    owner.done.Signal();
                }
            }
        }
    }
}
