# Fuzzing

This directory contains SharpFuzz fuzz targets for Etch hot-path code that handles untrusted input.

## Overview

SharpFuzz provides a libFuzzer-compatible fuzzing harness for .NET. It instruments the code during fuzzing to guide coverage-based input generation. Fuzz targets run under coverage-guided mutation to find edge cases that unit tests miss.

## Quick Start

### Install SharpFuzz instrumentation (one-time)

```powershell
dotnet tool install --global SharpFuzz
```

### Run a fuzz target locally

```powershell
# Smoke run (30 seconds)
.\tools\ci\run-fuzz-smoke.ps1 -Target Etch.Primitives.Fuzz -Seconds 30

# Full run (manual, long)
dotnet run -c Release --project fuzz/Etch.Primitives.Fuzz
```

### Adding a new fuzz target

1. Create `fuzz/Etch.<Module>.Fuzz/` directory
2. Add `.csproj` referencing your module and SharpFuzz
3. Create entry point:

```csharp
using SharpFuzz;

namespace Etch.MyModule.Fuzz;

public static class Program
{
    public static void Main(string[] args)
        => Fuzzer.Run(Fuzz);

    private static void Fuzz(ReadOnlySpan<byte> input)
    {
        FuzzGuard.Run(input, static bytes =>
        {
            // Your fuzz logic here
            // Only EtchException with known panic codes is acceptable
            // Any other exception indicates a bug
        });
    }
}
```

4. Add seed corpus in `corpus/` directory
5. Register in `tools/ci/run-fuzz-smoke.ps1` if you want CI smoke runs

## Key Concepts

### FuzzGuard

`FuzzGuard.Run` wraps your fuzz logic. It catches and swallows only `EtchException` (panic with known code). Any other exception — `IndexOutOfRangeException`, `ArgumentException`, etc. — escapes and is reported as a crash.

```csharp
FuzzGuard.Run(input, static bytes =>
{
    // Your code here
    // Only EtchException is "expected"
});
```

### Corpus

Seed corpus files in `corpus/` provide starting inputs that trigger interesting code paths. Each file should exercise a different edge case:

- `zero.bin` — all zeros
- `varint-boundary.bin` — varint continuation bytes at boundary
- `float-nan.bin` — special floating-point values

## Nightly Fuzz Hook

Full-length fuzz runs (1+ hour) should be dispatched from the nightly CI workflow. The hook point is:

```yaml
# .github/workflows/fuzz-nightly.yml (create this in COR-016 or later)
jobs:
  fuzz:
    runs-on: fuzz-runner  # self-hosted with ASAN/sanitizer coverage
    steps:
      - uses: actions/checkout@v4
      - run: dotnet run -c Release --project fuzz/Etch.Primitives.Fuzz -- -max_total_time=3600
```

## Interpreting Results

| Exit Code | Meaning |
|-----------|---------|
| 0 | No new crashes found |
| 1 | New crash detected (file a bug!) |
| 77 | libFuzzer quirk (no corpus, etc.) — usually benign |

When a crash is found, SharpFuzz outputs the crashing input to `crash-*` files. Use `SharpFuzz.Minimize` to reduce the reproducer.

## Troubleshooting

**"No司 files in corpus" warning**: The corpus directory exists but has no `.bin` files. Add seed inputs.

**Exit code 77 on first run**: This is normal — libFuzzer needs corpus files to start. Ensure `corpus/` contains valid seed inputs.

**ASAN builds**: For memory sanitization, use the `fuzz-asan` build configuration if available.
