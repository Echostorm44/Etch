using System.Runtime.ExceptionServices;

namespace Etch.Compose.Cpu;

/// <summary>
/// Runs a worker body on N threads — the caller plus N − 1 dedicated background threads — and
/// waits for all of them, without allocating per run.
/// </summary>
/// <remarks>
/// <para>
/// Dedicated threads rather than the thread pool: pool threads spin for work after finishing a
/// frame, which measurably slowed the single-threaded work that follows (building the next draw
/// list ran 5× slower). These block on a monitor and do not spin. Threads are created when a
/// frame first needs them and end with <see cref="Dispose"/>; an idle composer costs no CPU.
/// </para>
/// <para>
/// The handshakes are monitors on lock objects allocated up front, not
/// <see cref="ManualResetEventSlim"/> or <see cref="CountdownEvent"/>: those create their lock
/// object on the first wait that actually blocks, so whichever run first had to wait allocated
/// (24 bytes), and which run that was depended on thread scheduling.
/// </para>
/// </remarks>
internal sealed class TileWorkers : IDisposable
{
    private const int StackSize = 256 * 1024;

    private readonly Action<int> body;
    private readonly object doneGate = new();
    private readonly List<Worker> workers = new();
    private ExceptionDispatchInfo? failure;
    private int pending;
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
        Volatile.Write(ref pending, threads - 1);
        for (int i = 0; i < threads - 1; i++)
        {
            workers[i].Signal();
        }
        try
        {
            body(0);
        }
        finally
        {
            WaitForWorkers();
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
            worker.Signal();
        }
        foreach (var worker in workers)
        {
            worker.Thread.Join();
        }
        workers.Clear();
    }

    // Workers usually finish within a few microseconds of the caller: spin briefly before sleeping.
    private void WaitForWorkers()
    {
        var spinner = default(SpinWait);
        while (Volatile.Read(ref pending) != 0)
        {
            if (spinner.NextSpinWillYield)
            {
                lock (doneGate)
                {
                    while (Volatile.Read(ref pending) != 0)
                    {
                        Monitor.Wait(doneGate);
                    }
                }
                return;
            }
            spinner.SpinOnce(sleep1Threshold: -1);
        }
    }

    private void WorkerDone()
    {
        if (Interlocked.Decrement(ref pending) != 0)
        {
            return;
        }
        lock (doneGate)
        {
            Monitor.Pulse(doneGate);
        }
    }

    private sealed class Worker
    {
        private readonly TileWorkers owner;
        private readonly int index;
        private readonly object gate = new();
        private bool started;

        public Worker(TileWorkers owner, int index)
        {
            this.owner = owner;
            this.index = index;
            Thread = new Thread(Loop, StackSize) { IsBackground = true, Name = $"Etch CPU composer {index}" };
            Thread.Start();
        }

        public Thread Thread { get; }

        // This thread's allocation counter after its last run of the body.
        public long Allocated;

        public void Signal()
        {
            lock (gate)
            {
                started = true;
                Monitor.Pulse(gate);
            }
        }

        // No spinning: a worker sleeps in the kernel between frames.
        private void Loop()
        {
            while (true)
            {
                lock (gate)
                {
                    while (!started)
                    {
                        Monitor.Wait(gate);
                    }
                    started = false;
                }
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
                    owner.WorkerDone();
                }
            }
        }
    }
}
