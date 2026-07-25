using System;

namespace Etch.Bench.Shared;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AllocationBudgetAttribute : Attribute
{
    public long Bytes { get; }

    public AllocationBudgetAttribute(long bytes)
    {
        Bytes = bytes;
    }
}
