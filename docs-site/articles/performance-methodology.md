# Performance Methodology

This document defines the measurement methodology, reference hardware, and statistical reporting for Etch performance benchmarks.

## Reference Hardware

### CI Gating Machine (x64)

| Component | Specification |
|---|---|
| CPU | AMD Ryzen 7 5800X / Intel Core i7-12700K |
| RAM | 32 GB DDR4-3200 |
| GPU | NVIDIA RTX 3060 / AMD Radeon RX 6600 |
| Storage | NVMe SSD (Samsung 980 Pro or equivalent) |
| OS | Windows 11 Pro / Ubuntu 24.04 LTS |

### Apple Silicon (secondary)

| Component | Specification |
|---|---|
| CPU | Apple M2 Pro (10-core) |
| RAM | 16 GB unified |
| OS | macOS 14 Sonoma |

## RIDs Under Regression

| RID | Status |
|---|---|
| `win-x64` | Primary gating target. CI runs every PR. |
| `linux-x64` | Secondary gating target. CI runs every PR. |
| `osx-arm64` | Informational only. Dev machine spot-check. |

## Measurement Tools

- **BenchmarkDotNet** (v0.14.0+): Primary harness for microbenchmarks. Provides p50/p95/p99 statistics via `[SimpleJob]` and `[MemoryDiagnoser]`.
- **`Stopwatch`**: Used for end-to-end scene render timing in regression tests. Warm-up: 10 frames discarded; measurement: 100 frames.
- **`GC.GetTotalAllocatedBytes(precise: false)`**: Per-frame allocation measurement for zero-alloc assertion.
- **`Process.WorkingSet64`**: Working set (RSS) for memory-budget regression.
- **`GC.GetTotalPauseDuration()`**: GC pause tracking for soak tests.

## Statistical Reporting

### Percentiles

Reports use `p50`, `p95`, `p99`, and `p99.9` frame times:

- **p50 (median)**: Typical performance.
- **p95**: Upper-bound for "normal" operation.
- **p99**: Worst-case in normal operation.
- **p99.9**: Soak-test outlier floor.

### Warm-Up Policy

- **Microbenchmarks**: BenchmarkDotNet auto-warmup (default: 15-20 iterations).
- **Scene renders**: 10 frames discarded, then 100 measurement frames.
- **Soak tests**: First hour discarded as warm-up baseline.

### Run Count

| Test Type | Iterations | Notes |
|---|---|---|
| PR gate | 1 run, 100 frames per scene | Fast. Catches >20% regression. |
| Nightly | 5 runs, median taken | Reduces noise. |
| Soak | 24 hours continuous | Catches GC drift, fragmentation. |

## Environmental Controls

- **Power plan**: High Performance (Windows), no throttling (Linux `cpupower`).
- **Background processes**: Minimized; no browser, IDE, or build running.
- **Thermals**: GPU/CPU within normal operating range. No throttling allowed.
- **CI isolation**: Dedicated runner; no co-located workloads.

## Regressions

### CPU Regression Guard

Any p95 exceeding target + **20%** fails the PR.

| Scene | Target (p95) |
|---|---|
| 1080p red rect | < 0.8 ms |
| 1080p 1000 solid rects | < 5 ms |
| 1080p 500 AA paths | < 20 ms |

### GPU Regression Guard

Any p95 exceeding target + **30%** fails the PR.

| Scene | Target (p95) |
|---|---|
| 1080p 1000 solid rects | < 1 ms GPU |
| 1080p 5000 AA paths | < 4 ms GPU |
| 4K 2000 AA paths | < 10 ms GPU |

### Memory Regression Guard

Any row in `ProjectPlan.md` §Memory exceeding declared budget fails.

| Scenario | Budget |
|---|---|
| Per-frame managed allocations | 0 bytes |
| SimpleCascade idle | < 20 MB |
| Medium app | < 80 MB |

## What Is NOT Compared

- **Cross-language**: Benchmarks only compare Etch against itself (regression, not competition).
- **Cross-GPU-vendor**: Single reference GPU per platform. Apple Silicon tracked separately (informational).
- **debug vs release**: Only Release builds measured. Debug builds are for functional testing, not numbers.

## Baselines

Baselines are versioned per reference machine profile in `bench/baselines/reference-machine.json`. Rebaseline requires a dedicated PR with reviewer approval — no auto-rebase.

## References

- `bench/Etch.Bench.Cpu/` — CPU benchmark suite (CPU-008)
- `bench/Etch.Bench.Gpu/` — GPU benchmark suite (GPU-011)
- `bench/baselines/reference-machine.json` — Baseline measurements
- `docs/13-correctness/COR-014.md` — Performance regression test spec
