# Benchmarks

This directory contains BenchmarkDotNet benchmark projects for Etch hot paths.

## Running a benchmark

```powershell
dotnet run -c Release --project bench/Etch.Primitives.Bench
```

Filter to a specific benchmark:

```powershell
dotnet run -c Release --project bench/Etch.Primitives.Bench -- --filter *SpanWriterHotLoop*
```

Run smoke benchmarks (fast subset for CI):

```powershell
./tools/ci/run-bench-smoke.ps1 -Project bench/Etch.Primitives.Bench
```

## Interpreting the `Allocated` column

The `MemoryDiagnoser` reports bytes allocated per benchmark invocation. A value of `-` or `0` means the benchmark is zero-allocating in the hot path.

## Allocation budget rules

Every hot-path benchmark must declare an `[AllocationBudget]` attribute. Default budget is `0 B` — any non-zero budget requires justification in the task that added the benchmark.

Example:

```csharp
[Benchmark]
[AllocationBudget(1024)] // Justification: temporary allocation for complex floating-point formatting
public double ComplexCalculation() { ... }
```

The `AllocationBudgetValidator` enforces that every benchmark has an allocation budget attribute. Allocation enforcement is done via post-run analysis in CI (the smoke script parses benchmark output and fails if allocations exceed budget).

## How to add a new benchmark project

1. Create a new directory under `bench/`, e.g. `bench/Etch.Tiling.Bench/`.
2. Add a `.csproj` that imports `build/Bench.props` and references the project you are benchmarking plus `Etch.Bench.Shared`.
3. Create a class with `[MemoryDiagnoser]` and add `[Benchmark]` methods with `[AllocationBudget]` attributes.
4. Add the project to the solution if desired: `dotnet sln add bench/Etch.Tiling.Bench/Etch.Tiling.Bench.csproj`.

## BenchmarkDotNet and AOT

BenchmarkDotNet requires JIT compilation to dynamically compile and execute benchmarks at runtime. This means benchmark projects themselves cannot be AOT-published. However, the hot-path code being benchmarked (e.g., `Etch.Primitives`) is verified AOT-compatible separately via the `aot-publish` CI job.

## AOT smoke testing

The `aot-publish` CI job runs on `Etch.Abstractions` and other core libraries to catch AOT compatibility issues in hot-path code. Benchmark projects serve as integration tests for runtime performance but cannot be AOT-compiled due to BenchmarkDotNet's reliance on dynamic compilation.
