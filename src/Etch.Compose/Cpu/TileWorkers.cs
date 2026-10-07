using System.Runtime.ExceptionServices;

namespace Etch.Compose.Cpu;

/// <summary>
/// Runs a worker body on N threads — the caller plus N − 1 thread-pool work items — and waits for
/// all of them, without allocating: the work items and the countdown are created once and reused.
/// </summary>
internal sealed class TileWorkers : IDisposable
{
    private readonly Action<int> body;
    private readonly CountdownEvent done = new(1);
    private Item[] items = Array.Empty<Item>();
    private ExceptionDispatchInfo? failure;

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
        if (items.Length < threads)
        {
            var grown = new Item[threads];
            Array.Copy(items, grown, items.Length);
            for (int i = items.Length; i < threads; i++)
            {
                grown[i] = new Item(this, i);
            }
            items = grown;
        }
        failure = null;
        done.Reset(threads - 1);
        for (int i = 1; i < threads; i++)
        {
            ThreadPool.UnsafeQueueUserWorkItem(items[i], preferLocal: false);
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

    private void Execute(int worker)
    {
        try
        {
            body(worker);
        }
        catch (Exception e)
        {
            Interlocked.CompareExchange(ref failure, ExceptionDispatchInfo.Capture(e), null);
        }
        finally
        {
            done.Signal();
        }
    }

    public void Dispose() => done.Dispose();

    private sealed class Item(TileWorkers owner, int worker) : IThreadPoolWorkItem
    {
        public void Execute() => owner.Execute(worker);
    }
}
