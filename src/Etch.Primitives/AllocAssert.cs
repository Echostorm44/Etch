using System;
using System.Diagnostics;
using Etch;

namespace Etch.Primitives;

#pragma warning disable CA1065 // Throwing EtchException on allocation detection is the intended behavior
public sealed class AllocGuard : IDisposable
{
    private readonly long _baseline;
    private bool _disposed;

    public AllocGuard()
    {
        _baseline = GC.GetAllocatedBytesForCurrentThread();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var current = GC.GetAllocatedBytesForCurrentThread();
        var delta = current - _baseline;
        if (delta > 0)
        {
            Panic.Invariant(PanicCodes.UnexpectedAllocation, $"Unexpected allocation of {delta} bytes");
        }
    }
}
#pragma warning restore CA1065

public static class AllocAssert
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static AllocGuard NoneExpected()
    {
        return new AllocGuard();
    }
}
