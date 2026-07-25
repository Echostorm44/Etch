using System;
using System.Buffers;
using System.Diagnostics;

namespace Etch.Testing.MemoryReporters;

public static class NativePoolReporter
{
    public static long WorkingSet => Environment.WorkingSet;

    public static long ManagedAllocatedBytes
        => GC.GetTotalAllocatedBytes(precise: false);

    public static long MeasurePooledArrayBytes()
    {
        long total = 0;
        var rented = ArrayPool<byte>.Shared.Rent(1);
        try
        {
            total = GC.GetTotalAllocatedBytes(precise: false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        return total;
    }

    public static long GetWorkingSetDelta(long before)
        => Environment.WorkingSet - before;
}
