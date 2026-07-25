# Blend Strategy Benchmark Results

## Status

Placeholder — benchmarks require GPU hardware (Lavapipe/SwiftShader or real GPU)
to produce meaningful numbers. Run with:

```bash
dotnet run --project bench/Etch.Bench.Blend -c Release
```

## Hardware Matrix

| Hardware | Backend | Branchy Uber | Specialization-per-Mode | Texture LUT |
|---|---|---|---|---|
| CI x64 (Lavapipe) | Vulkan CPU | TBD | TBD | TBD |
| Apple M3 Air | Metal | TBD | TBD | TBD |
| Windows Intel Arc | D3D12 | TBD | TBD | TBD |

## Decision

Pending hardware results. See `docs/00-overview/design-decisions.md` §D-015.
