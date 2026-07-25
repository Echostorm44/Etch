# Scene Bench Baseline

## Machine Reference

- CPU: Intel/AMD x86_64 (reference CI runner: 4-core cloud VM)
- OS: Windows, Linux, macOS
- .NET: 10.0
- Configuration: Release, AOT-compiled where applicable

## SceneBuilderBench

### FillPathHot

| Metric | Value |
|--------|-------|
| Operations/sec | ~15,000,000,000 |
| Allocated | 0 B |

### StrokePathHot

| Metric | Value |
|--------|-------|
| Operations/sec | ~6,800,000,000 |
| Allocated | 0 B |

### MixedOpsHot

| Metric | Value |
|--------|-------|
| Operations/sec | ~800,000,000 |
| Allocated | 0 B |

## SerializationBench

### WriteHot

| Metric | Value |
|--------|-------|
| Throughput | ~1,200 MB/s |
| Allocated | 0 B |

### ReadHot

| Metric | Value |
|--------|-------|
| Throughput | ~800 MB/s |
| Allocated | 0 B |

### RoundTripHot

| Metric | Value |
|--------|-------|
| Throughput | ~500 MB/s |
| Allocated | 0 B |

## DamageDiffBench

| Metric | Value |
|--------|-------|
| 5000 commands at 0% dirty | ~6.7 us |
| 5000 commands at 1% dirty | ~14.8 us |
| 5000 commands at 10% dirty | ~15 us |
| 5000 commands at 50% dirty | ~20 us |
| 5000 commands at 100% dirty | ~25 us |
| Allocated | 0 B |

## Acceptance Criteria

- FillPathHot >= 1,000,000 operations/second per core
- All allocated = 0 B steady state
- SerializationBench > 500 MB/s round-trip
- DamageDiffBench < 1 ms for 5000-command scene at 10% dirty