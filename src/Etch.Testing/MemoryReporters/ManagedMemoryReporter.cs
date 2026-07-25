using System;

namespace Etch.Testing.MemoryReporters;

public static class ManagedMemoryReporter
{
    public static long MeasureWorkingSet()
        => Environment.WorkingSet;

    public static long MeasureGcHeap()
        => GC.GetTotalMemory(forceFullCollection: false);

    public static long MeasureGcHeapAfterFullCollect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
