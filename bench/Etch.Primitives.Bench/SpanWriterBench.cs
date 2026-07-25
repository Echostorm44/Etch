using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Validators;
using Etch.Bench.Shared;
using Etch.Primitives;

namespace Etch.Primitives.Bench;

[MemoryDiagnoser]
public class SpanWriterBench
{
    private byte[] _dst = new byte[4096];

    [Benchmark]
    [AllocationBudget(0)]
    public int SpanWriterHotLoop()
    {
        var w = new SpanWriter(_dst);
        for (int i = 0; i < 256; i++)
        {
            w.WriteI32LE(i);
            w.WriteVarInt((ulong)i * 131);
            w.WriteF32LE(i * 0.5f);
        }
        return w.Position;
    }
}

public class AllocationBudgetValidator : IValidator
{
    public bool TreatsWarningsAsErrors => true;

    public IEnumerable<ValidationError> Validate(ValidationParameters validationParameters)
    {
        ArgumentNullException.ThrowIfNull(validationParameters);

        foreach (var benchmarkCase in validationParameters.Benchmarks)
        {
            var descriptor = benchmarkCase.Descriptor;
            var methodAttr = descriptor.WorkloadMethod.GetCustomAttributes(typeof(AllocationBudgetAttribute), false)
                .FirstOrDefault() as AllocationBudgetAttribute;
            var classAttr = descriptor.Type.GetCustomAttributes(typeof(AllocationBudgetAttribute), false)
                .FirstOrDefault() as AllocationBudgetAttribute;

            if (methodAttr == null && classAttr == null)
            {
                yield return new ValidationError(
                    TreatsWarningsAsErrors,
                    $"Benchmark '{benchmarkCase.DisplayInfo}' has no [AllocationBudget] attribute. " +
                    $"Hot-path benchmarks must declare an allocation budget.",
                    benchmarkCase);
            }
        }
    }
}
