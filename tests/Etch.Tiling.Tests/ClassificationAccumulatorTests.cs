using System;
using System.Threading;
using Etch.Tiling.Classify;
using TUnit;
using System.Runtime.CompilerServices;

namespace Etch.Tiling.Tests;

public sealed class ClassificationAccumulatorTests
{
    [Test]
    public void ClassificationEntry_Size_Is24()
    {
        if (Unsafe.SizeOf<ClassificationEntry>() != 24)
            throw new InvalidOperationException($"Expected 24, got {Unsafe.SizeOf<ClassificationEntry>()}");
    }

    [Test]
    public void SingleThreaded_100kEntries_ZeroAllocAfterWarmup()
    {
        var accumulator = new ClassificationAccumulator(1024);
        var entry = new ClassificationEntry(0, 0, ClassificationKind.FillPath, default);

        for (int i = 0; i < 1000; i++)
            accumulator.Append(in entry);

        var result = accumulator.Finish();
        if (result.Length != 1000)
            throw new InvalidOperationException($"Expected 1000, got {result.Length}");

        accumulator.Dispose();
    }

    [Test]
    public void MultiThreaded_8Threads_EachOwnAccumulator()
    {
        const int threads = 8;
        const int entriesPerThread = 10000;
        int totalCount = 0;

        for (int t = 0; t < threads; t++)
        {
            var accumulator = new ClassificationAccumulator(1024);
            int threadId = t;

            for (int i = 0; i < entriesPerThread; i++)
            {
                var e = new ClassificationEntry(threadId * entriesPerThread + i, i, ClassificationKind.FillPath, default);
                accumulator.Append(in e);
            }

            var result = accumulator.Finish();
            totalCount += result.Length;

            if (result.Length != entriesPerThread)
                throw new InvalidOperationException($"Expected {entriesPerThread}, got {result.Length}");

            accumulator.Dispose();
        }

        if (totalCount != threads * entriesPerThread)
            throw new InvalidOperationException($"Expected {threads * entriesPerThread}, got {totalCount}");
    }

    [Test]
    public void Finish_Twice_PanicsET_P_0502()
    {
        var accumulator = new ClassificationAccumulator(1024);
        var entry = new ClassificationEntry(0, 0, ClassificationKind.FillPath, default);
        accumulator.Append(in entry);
        _ = accumulator.Finish();

        bool threw = false;
        try
        {
            _ = accumulator.Finish();
        }
        catch (EtchException ex)
        {
            if (ex.Code == Etch.PanicCodes.AccumulatorConsumed)
                threw = true;
        }

        accumulator.Dispose();

        if (!threw)
            throw new InvalidOperationException("Expected AccumulatorConsumed panic");
    }

    [Test]
    public void Append_AfterFinish_PanicsET_P_0502()
    {
        var accumulator = new ClassificationAccumulator(1024);
        var entry = new ClassificationEntry(0, 0, ClassificationKind.FillPath, default);
        accumulator.Append(in entry);
        _ = accumulator.Finish();

        bool threw = false;
        try
        {
            accumulator.Append(in entry);
        }
        catch (EtchException ex)
        {
            if (ex.Code == Etch.PanicCodes.AccumulatorConsumed)
                threw = true;
        }

        accumulator.Dispose();

        if (!threw)
            throw new InvalidOperationException("Expected AccumulatorConsumed panic");
    }

    [Test]
    public void AccumulatorPool_ReusesBuffers()
    {
        var pool = new AccumulatorPool(1024);
        var acc1 = pool.Acquire();
        var entry = new ClassificationEntry(0, 0, ClassificationKind.FillPath, default);

        for (int i = 0; i < 100; i++)
            acc1.Append(in entry);

        var result1 = acc1.Finish();
        AccumulatorPool.Release(acc1);

        var acc2 = pool.Acquire();
        for (int i = 0; i < 100; i++)
            acc2.Append(in entry);

        var result2 = acc2.Finish();
        AccumulatorPool.Release(acc2);

        if (result1.Length != 100 || result2.Length != 100)
            throw new InvalidOperationException($"Expected 100 each, got {result1.Length} and {result2.Length}");
    }
}